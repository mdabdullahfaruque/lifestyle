using Microsoft.AspNetCore.Http;

namespace Lifestyle.Modules.Media.Internal;

/// <summary>
/// Presents bytes already in memory as an <see cref="IFormFile"/>.
/// <para>
/// Used by the paths that produce a file without a browser having posted one — unpacking an
/// archive, fetching a URL. An adapter rather than a second ingest path: the single-file upload
/// carries the size, content-type and magic-byte checks and is the one place they live, so these
/// callers bend to fit it instead of growing a parallel set of rules that could drift.
/// </para>
/// </summary>
internal sealed class InMemoryFormFile(MemoryStream content, string fileName, string contentType) : IFormFile
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
    /// Each call returns an independent reader over the same bytes, because the upload path opens
    /// the file once per derivative size and would otherwise find the stream already at its end.
    /// </summary>
    public Stream OpenReadStream() =>
        new MemoryStream(content.GetBuffer(), 0, (int)content.Length, writable: false);
}
