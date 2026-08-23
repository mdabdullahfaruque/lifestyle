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
    /// <summary>Reads the pixel dimensions without decoding the whole image.</summary>
    Task<(int Width, int Height)?> ReadDimensionsAsync(Stream image, CancellationToken ct);

    /// <summary>
    /// Produces a resized copy that fits inside <paramref name="maxEdge"/> without cropping or
    /// enlarging. Returns null when the source is already smaller — re-encoding a small image just
    /// makes it worse.
    /// </summary>
    Task<ProcessedImage?> ResizeAsync(Stream source, int maxEdge, CancellationToken ct);
}

internal sealed record ProcessedImage(Stream Content, int Width, int Height, string ContentType) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
