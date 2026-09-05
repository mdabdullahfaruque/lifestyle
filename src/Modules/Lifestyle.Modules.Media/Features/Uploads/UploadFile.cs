using System.Globalization;
using System.Security.Cryptography;
using Lifestyle.Modules.Media.Contracts;
using Lifestyle.Modules.Media.Domain;
using Lifestyle.Modules.Media.Internal;
using Lifestyle.Modules.Media.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Media.Features.Uploads;

/// <summary>
/// Accepts one file, stores the original, and generates the derivative sizes. Returns a media id
/// the caller then attaches to a product, a vendor logo, or a KYC document.
/// </summary>
internal static class UploadFile
{
    internal sealed class Handler(
        IMediaDbContext db,
        IFileStorage storage,
        IImageProcessor images,
        ICurrentUser currentUser,
        IClock clock,
        IOptions<MediaModuleOptions> options,
        ILogger<Handler> logger)
        : IHandler<Handler.Command, Result<MediaAsset>>
    {
        private readonly MediaModuleOptions _options = options.Value;

        internal sealed record Command(IFormFile File, bool IsPrivate);

        public async Task<Result<MediaAsset>> Handle(Command command, CancellationToken ct)
        {
            if (currentUser.UserId is not { } userId)
                return Error.Unauthorized("identity.not_authenticated");

            var file = command.File;

            if (file.Length == 0)
                return Error.Validation("media.empty_file", "The uploaded file is empty.");

            if (file.Length > _options.MaxUploadBytes)
                return Error.Validation("media.file_too_large",
                    $"Files must be {_options.MaxUploadBytes / (1024 * 1024)} MB or smaller.");

            var contentType = file.ContentType?.Split(';')[0].Trim().ToLowerInvariant() ?? string.Empty;
            if (!_options.AllowedContentTypes.Contains(contentType, StringComparer.Ordinal))
                return Error.Validation("media.unsupported_type",
                    $"'{contentType}' is not an accepted file type.");

            var publicId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var extension = ExtensionFor(contentType);
            var originalKey = BuildKey(publicId, "original", extension, command.IsPrivate);

            int? width = null, height = null;
            var isImage = contentType.StartsWith("image/", StringComparison.Ordinal);

            await using (var stream = file.OpenReadStream())
            {
                // The multipart content type is client-asserted. Images are already verified by the
                // decoder below; PDFs get a magic-byte check so an arbitrary file cannot be parked
                // in storage wearing a pdf label.
                if (contentType == "application/pdf")
                {
                    var head = new byte[5];
                    var read = await stream.ReadAtLeastAsync(head, 5, throwOnEndOfStream: false, ct);
                    if (read < 5 || head[0] != 0x25 || head[1] != 0x50 || head[2] != 0x44 || head[3] != 0x46 || head[4] != 0x2D)
                        return Error.Validation("media.not_a_pdf", "That file is not a readable PDF.");
                    stream.Position = 0;
                }

                if (isImage)
                {
                    var dimensions = await images.ReadDimensionsAsync(stream, ct);
                    if (dimensions is null)
                        return Error.Validation("media.not_an_image", "That file is not a readable image.");

                    (width, height) = dimensions.Value;
                    stream.Position = 0;
                }

                await storage.SaveAsync(originalKey, stream, contentType, ct);
            }

            var media = MediaFile.Record(
                publicId, SafeFileName(file.FileName), contentType, file.Length, originalKey,
                width, height, userId, currentUser.VendorId, command.IsPrivate, clock.UtcNow);

            // Private files get no public derivatives: they are documents for a reviewer, not
            // storefront imagery, and every derivative would be another key to protect.
            if (isImage && !command.IsPrivate)
                await GenerateDerivativesAsync(media, file, publicId, extension, ct);

            db.MediaFiles.Add(media);
            await db.SaveChangesAsync(ct);

            return ToAsset(media, storage);
        }

        private async Task GenerateDerivativesAsync(
            MediaFile media, IFormFile file, string publicId, string extension, CancellationToken ct)
        {
            foreach (var variant in MediaVariants.All)
            {
                if (!_options.VariantSizes.TryGetValue(variant, out var maxEdge)) continue;

                try
                {
                    await using var source = file.OpenReadStream();
                    using var resized = await images.ResizeAsync(source, maxEdge, ct);

                    // Null means the source was already smaller than this variant — reuse the
                    // original rather than upscaling it into a bigger, blurrier file.
                    if (resized is null) continue;

                    var key = BuildKey(publicId, variant, extension);
                    await storage.SaveAsync(key, resized.Content, resized.ContentType, ct);
                    media.AddDerivative(variant, key, resized.Width, resized.Height, resized.Content.Length);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed derivative must not fail the upload: the original is stored and
                    // usable, and the missing size falls back to it at render time.
                    logger.LogWarning(ex, "Could not generate the {Variant} derivative for {PublicId}.", variant, publicId);
                }
            }
        }

        /// <summary>
        /// Private files live under the private/ prefix, which the edge never serves — they are
        /// only reachable through the authorised download endpoint. KYC documents are the reason
        /// this exists: an identity card must never sit on a public, CDN-cached URL.
        /// </summary>
        private static string BuildKey(string publicId, string variant, string extension, bool isPrivate = false) =>
            string.Create(CultureInfo.InvariantCulture,
                $"{(isPrivate ? "private/" : "")}{publicId[..2]}/{publicId}/{variant}{extension}");

        private static string ExtensionFor(string contentType) => contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };

        /// <summary>
        /// Keeps the name for display but strips anything that could be read as a path. The stored
        /// key never uses this value, so this is defence in depth rather than the only guard.
        /// </summary>
        private static string SafeFileName(string fileName)
        {
            var name = Path.GetFileName(fileName ?? string.Empty);
            var cleaned = new string([.. name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))]);
            return string.IsNullOrWhiteSpace(cleaned)
                ? "upload"
                : cleaned.Length > 255 ? cleaned[..255] : cleaned;
        }
    }

    internal static MediaAsset ToAsset(MediaFile media, IFileStorage storage) => new(
        media.PublicId,
        media.FileName,
        media.ContentType,
        media.SizeBytes,
        media.Width,
        media.Height,
        // A private file is addressed through the authorised endpoint, never the media host.
        media.IsPrivate ? $"/v1/media/private/{media.PublicId}" : storage.GetPublicUrl(media.StorageKey),
        media.IsPrivate
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : media.Derivatives.ToDictionary(d => d.Variant, d => storage.GetPublicUrl(d.StorageKey), StringComparer.Ordinal));

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/", async (IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm(Name = "private")] bool? isPrivate, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(file, isPrivate ?? false), ct)).ToHttpResult())
            .WithName("UploadFile")
            .WithSummary("Upload an image or document and generate its derivative sizes.")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Produces<MediaAsset>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
