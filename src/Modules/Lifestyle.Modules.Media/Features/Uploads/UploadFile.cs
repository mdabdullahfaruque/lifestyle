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

        internal sealed record Command(IFormFile File);

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
            var originalKey = BuildKey(publicId, "original", extension);

            int? width = null, height = null;
            var isImage = contentType.StartsWith("image/", StringComparison.Ordinal);

            await using (var stream = file.OpenReadStream())
            {
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
                width, height, userId, currentUser.VendorId, clock.UtcNow);

            if (isImage)
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

        private static string BuildKey(string publicId, string variant, string extension) =>
            string.Create(CultureInfo.InvariantCulture, $"{publicId[..2]}/{publicId}/{variant}{extension}");

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
        storage.GetPublicUrl(media.StorageKey),
        media.Derivatives.ToDictionary(d => d.Variant, d => storage.GetPublicUrl(d.StorageKey), StringComparer.Ordinal));

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/", async (IFormFile file, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(file), ct)).ToHttpResult())
            .WithName("UploadFile")
            .WithSummary("Upload an image or document and generate its derivative sizes.")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Produces<MediaAsset>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
