using System.Globalization;
using Lifestyle.Modules.Media.Internal;
using Microsoft.Extensions.Options;

namespace Lifestyle.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary><c>local</c> or <c>s3</c>.</summary>
    public string Provider { get; init; } = "local";

    /// <summary>Filesystem root for the local provider.</summary>
    public string LocalRoot { get; init; } = ".storage";

    /// <summary>Base URL objects are served from.</summary>
    public string PublicBaseUrl { get; init; } = "/media";

    public string? BucketName { get; init; }
    public string? ServiceUrl { get; init; }
    public string? AccessKey { get; init; }
    public string? SecretKey { get; init; }
    public string? Region { get; init; }
}

/// <summary>
/// Local-disk storage for development. Production uses <see cref="S3FileStorage"/> against S3 or
/// MinIO; both sit behind <see cref="IFileStorage"/> so nothing above them changes.
/// </summary>
internal sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly StorageOptions _options = options.Value;

    public async Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);

        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = ResolvePath(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = ResolvePath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct) =>
        Task.FromResult(File.Exists(ResolvePath(key)));

    public string GetPublicUrl(string key) =>
        string.Create(CultureInfo.InvariantCulture, $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}");

    /// <summary>
    /// Resolves a key under the storage root and refuses anything that escapes it. Keys are
    /// generated internally, so this is defence in depth — but path traversal is exactly the bug
    /// that gets found later.
    /// </summary>
    private string ResolvePath(string key)
    {
        var root = Path.GetFullPath(_options.LocalRoot);
        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));

        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && path != root)
            throw new InvalidOperationException($"Storage key '{key}' resolves outside the storage root.");

        return path;
    }
}
