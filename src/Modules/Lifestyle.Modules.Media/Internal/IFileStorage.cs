namespace Lifestyle.Modules.Media.Internal;

/// <summary>
/// Object storage, abstracted so local disk (development) and S3/MinIO (everywhere else) are the
/// same to callers. Implemented in Infrastructure — Media declares the need, Infrastructure
/// satisfies it.
/// </summary>
internal interface IFileStorage
{
    Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct);

    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);

    Task<bool> ExistsAsync(string key, CancellationToken ct);

    /// <summary>The URL a browser should use for this object.</summary>
    string GetPublicUrl(string key);
}

/// <summary>Resizes images into the named derivative sizes.</summary>
internal interface IImageProcessor
{
    /// <summary>
    /// Reads dimensions and capture time from the header, without decoding the pixels.
    /// <para>
    /// Both in one pass: the capture time is only on the original, and reading it later would mean
    /// fetching the whole file back out of storage to parse a header we already had open.
    /// </para>
    /// </summary>
    Task<ImageMetadata?> ReadMetadataAsync(Stream image, CancellationToken ct);

    /// <summary>
    /// Produces a resized copy that fits inside <paramref name="maxEdge"/> without cropping or
    /// enlarging. Returns null when the source is already smaller — re-encoding a small image just
    /// makes it worse.
    /// </summary>
    Task<ProcessedImage?> ResizeAsync(Stream source, int maxEdge, CancellationToken ct);
}

/// <summary>What the image header tells us before any pixel is decoded.</summary>
internal sealed record ImageMetadata(int Width, int Height, DateTimeOffset? CapturedAt);

internal sealed record ProcessedImage(Stream Content, int Width, int Height, string ContentType) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
