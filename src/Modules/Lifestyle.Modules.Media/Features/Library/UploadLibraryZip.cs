using System.IO.Compression;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Media.Contracts;
using Lifestyle.Modules.Media.Features.Uploads;
using Lifestyle.Modules.Media.Internal;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Media.Features.Library;

/// <summary>
/// Unpacks a ZIP of product photos into the vendor's library (docs/08 §5, §7.1).
/// <para>
/// This is the organised seller's path: they already keep <c>LS-1001/main.jpg</c> on disk, so the
/// folder names carry the product codes and the import matcher can place almost everything without
/// the seller touching the grid.
/// </para>
/// <para>
/// A folder is <b>flattened into the display name</b> — <c>LS-1001/main.jpg</c> is stored as
/// <c>LS-1001_main.jpg</c>. The matcher resolves the longest leading run of a filename against the
/// known product codes, so a flattened name matches on the folder exactly as a path would, while
/// nothing anywhere has to store or trust a directory that came out of an archive.
/// </para>
/// </summary>
internal static class UploadLibraryZip
{
    /// <summary>
    /// Guards against a decompression bomb — a few KB of ZIP that expands to gigabytes. Enforced
    /// while reading, never after: checking the total afterwards means having already absorbed it.
    /// </summary>
    private const long MaxTotalUncompressedBytes = 300L * 1024 * 1024;

    private const int MaxEntries = 500;

    /// <summary>
    /// Entries compressing better than this are refused. Real photographs are already compressed
    /// and barely shrink; a ratio like this means zeros, not pixels.
    /// </summary>
    private const int MaxCompressionRatio = 60;

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    internal sealed class Handler(
        UploadFile.Handler upload,
        IMediaModule media,
        ICurrentUser currentUser,
        IOptions<MediaModuleOptions> options)
        : IHandler<Handler.Command, Result<BulkUploadResponse>>
    {
        private readonly MediaModuleOptions _options = options.Value;

        internal sealed record Command(IFormFile Archive);

        public async Task<Result<BulkUploadResponse>> Handle(Command command, CancellationToken ct)
        {
            if (currentUser.VendorId is not { } vendorId)
                return Error.Forbidden("media.no_vendor_context", "This token is not scoped to a vendor.");

            if (command.Archive.Length == 0)
                return Error.Validation("media.empty_file", "That archive is empty.");

            ZipArchive? archive = null;
            var source = command.Archive.OpenReadStream();

            try
            {
                try
                {
                    archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
                }
                catch (InvalidDataException)
                {
                    return Error.Validation("media.not_a_zip", "That file is not a readable ZIP archive.");
                }

                return await IngestAsync(archive, vendorId, ct);
            }
            finally
            {
                archive?.Dispose();
                await source.DisposeAsync();
            }
        }

        private async Task<Result<BulkUploadResponse>> IngestAsync(
            ZipArchive archive, Guid vendorId, CancellationToken ct)
        {
            var entries = archive.Entries
                .Where(e => e.Length > 0 && !string.IsNullOrEmpty(e.Name))
                .ToList();

            if (entries.Count == 0)
                return Error.Validation("media.zip_empty", "That archive contains no files.");

            if (entries.Count > MaxEntries)
                return Error.Validation("media.zip_too_many",
                    $"That archive has {entries.Count} files; at most {MaxEntries} can be unpacked at once.");

            var declaredTotal = entries.Sum(e => e.Length);
            if (declaredTotal > MaxTotalUncompressedBytes)
                return Error.Validation("media.zip_too_large",
                    $"That archive unpacks to {declaredTotal / (1024 * 1024)} MB; the limit is "
                    + $"{MaxTotalUncompressedBytes / (1024 * 1024)} MB.");

            var items = new List<BulkUploadItemResponse>(entries.Count);
            var uploaded = new List<string>(entries.Count);
            long readSoFar = 0;

            // One entry at a time. ImageSharp decodes each image into uncompressed memory and this
            // server is shared with four other products (docs/07 §1) — unpacking in parallel would
            // multiply peak memory by the degree of parallelism for no real gain on shared cores.
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                var displayName = SafeDisplayName(entry.FullName);

                if (displayName is null)
                {
                    items.Add(Failed(entry.FullName, "media.zip_unsafe_path",
                        "That file's name could not be read safely and was skipped."));
                    continue;
                }

                var extension = Path.GetExtension(displayName).ToLowerInvariant();

                if (!ImageExtensions.Contains(extension, StringComparer.Ordinal))
                {
                    items.Add(Failed(displayName, "media.zip_not_an_image",
                        "Only JPEG, PNG and WebP images are unpacked from an archive."));
                    continue;
                }

                if (entry.Length > _options.MaxUploadBytes)
                {
                    items.Add(Failed(displayName, "media.file_too_large",
                        $"Files must be {_options.MaxUploadBytes / (1024 * 1024)} MB or smaller."));
                    continue;
                }

                if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxCompressionRatio)
                {
                    items.Add(Failed(displayName, "media.zip_suspicious_entry",
                        "That entry compresses far too well to be a photograph and was skipped."));
                    continue;
                }

                readSoFar += entry.Length;
                if (readSoFar > MaxTotalUncompressedBytes)
                {
                    items.Add(Failed(displayName, "media.zip_too_large",
                        "The archive exceeded the unpacked size limit before this file."));
                    break;
                }

                var file = await ReadEntryAsync(entry, displayName, extension, ct);

                if (file is null)
                {
                    items.Add(Failed(displayName, "media.zip_entry_unreadable",
                        "That file could not be read from the archive."));
                    continue;
                }

                // Reuses the single-file upload path rather than restating its size, type and
                // decoder checks — one upload path, one set of rules.
                var result = await upload.Handle(
                    new UploadFile.Handler.Command(file, IsPrivate: false, SquareCanvas: true), ct);

                if (result.IsFailure)
                {
                    items.Add(Failed(displayName, result.Error.Code, result.Error.Message));
                    continue;
                }

                uploaded.Add(result.Value.Id);
                items.Add(new BulkUploadItemResponse(
                    displayName, Succeeded: true, result.Value.Id, result.Value.Url, null, null));
            }

            if (uploaded.Count > 0)
                await media.AttachAsync(uploaded, MediaOwnerTypes.VendorLibrary, vendorId, ct);

            return new BulkUploadResponse(uploaded.Count, items.Count - uploaded.Count, items);
        }

        private static async Task<IFormFile?> ReadEntryAsync(
            ZipArchiveEntry entry, string displayName, string extension, CancellationToken ct)
        {
            try
            {
                var buffer = new MemoryStream((int)entry.Length);

                await using (var entryStream = entry.Open())
                    await entryStream.CopyToAsync(buffer, ct);

                buffer.Position = 0;
                return new ZipEntryFormFile(buffer, displayName, ContentTypeFor(extension));
            }
            catch (InvalidDataException)
            {
                // A corrupt entry mid-archive must not cost the seller the other 399 photos.
                return null;
            }
        }

        private static BulkUploadItemResponse Failed(string name, string code, string message) =>
            new(name, Succeeded: false, null, null, code, message);

        private static string ContentTypeFor(string extension) => extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }

    /// <summary>
    /// Turns an archive entry path into a safe display name, folding one level of folder into it,
    /// or null when the entry must not be accepted at all.
    /// <para>
    /// The path is never used to open or write anything — a storage key is derived from a generated
    /// public id — so zip-slip cannot reach the file system from here. It is still <b>refused</b>
    /// rather than quietly sanitised: an archive carrying <c>../../etc/passwd</c> is not a product
    /// shoot, and sanitising instead would leave the guarantee resting on the storage layer never
    /// changing its mind about how it builds keys.
    /// </para>
    /// <para>
    /// The parent folder is folded in with an underscore because, by convention, it is the product
    /// code — and the import matcher resolves the longest leading run of a filename, so
    /// <c>LS-1001_main.jpg</c> matches on the folder exactly as a real path would.
    /// </para>
    /// </summary>
    internal static string? SafeDisplayName(string entryPath)
    {
        var full = entryPath.Replace('\\', '/');

        if (full.Contains("..", StringComparison.Ordinal)
            || full.StartsWith('/')
            || full.Contains(':', StringComparison.Ordinal))
            return null;

        var segments = full.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        var name = segments[^1];

        // Skip the junk macOS and Windows leave in archives.
        if (name.StartsWith('.') || segments.Contains("__MACOSX", StringComparer.Ordinal))
            return null;

        var folder = segments.Length >= 2 ? segments[^2] : null;
        var composed = folder is null ? name : $"{folder}_{name}";

        var cleaned = new string([.. composed.Where(c => !Path.GetInvalidFileNameChars().Contains(c))]);

        return string.IsNullOrWhiteSpace(cleaned)
            ? null
            : cleaned.Length > 255 ? cleaned[..255] : cleaned;
    }

    /// <summary>
    /// Presents one unpacked archive entry as an <see cref="IFormFile"/>.
    /// <para>
    /// An adapter rather than a refactor of <see cref="UploadFile"/>: the single-file upload path
    /// carries the size, content-type and magic-byte checks and is the one place they live, so ZIP
    /// ingest bends to fit it instead of growing a second ingest path that could drift from it.
    /// </para>
    /// </summary>
    private sealed class ZipEntryFormFile(MemoryStream content, string fileName, string contentType) : IFormFile
    {
        public string ContentType => contentType;
        public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{fileName}\"";
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => content.Length;
        public string Name => "file";
        public string FileName => fileName;

        public void CopyTo(Stream target)
        {
            content.Position = 0;
            content.CopyTo(target);
        }

        public async Task CopyToAsync(Stream target, CancellationToken ct = default)
        {
            content.Position = 0;
            await content.CopyToAsync(target, ct);
        }

        /// <summary>
        /// Each call returns an independent reader over the same bytes, because the upload path
        /// opens the file once per derivative size and would otherwise find the stream at its end.
        /// </summary>
        public Stream OpenReadStream() => new MemoryStream(content.GetBuffer(), 0, (int)content.Length, writable: false);
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/zip", async (IFormFile archive, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(archive), ct)).ToHttpResult())
            .WithName("UploadLibraryZip")
            .WithSummary("Unpack a ZIP of product photos into the library. Folder names become product codes.")
            .RequirePermission(Permissions.Media.UploadOwn)
            .DisableAntiforgery()
            .Produces<BulkUploadResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
