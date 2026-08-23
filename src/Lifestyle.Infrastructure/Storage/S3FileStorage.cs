using System.Globalization;
using Amazon.S3;
using Amazon.S3.Model;
using Lifestyle.Modules.Media.Internal;
using Microsoft.Extensions.Options;

namespace Lifestyle.Infrastructure.Storage;

/// <summary>
/// S3-compatible object storage. Works against AWS S3 and against MinIO — MinIO needs
/// <c>ForcePathStyle</c>, which is why the client is constructed here rather than taken from DI
/// defaults.
/// </summary>
internal sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly StorageOptions _options;
    private readonly AmazonS3Client _client;

    public S3FileStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;

        var config = new AmazonS3Config
        {
            ForcePathStyle = true,
            ServiceURL = _options.ServiceUrl
        };

        if (!string.IsNullOrWhiteSpace(_options.Region))
            config.AuthenticationRegion = _options.Region;

        _client = new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
    }

    private string Bucket => _options.BucketName
        ?? throw new InvalidOperationException("Storage:BucketName is required when Storage:Provider is 's3'.");

    public async Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = true
        }, ct);

        return key;
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        try
        {
            var response = await _client.GetObjectAsync(Bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct) =>
        _client.DeleteObjectAsync(Bucket, key, ct);

    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        try
        {
            await _client.GetObjectMetadataAsync(Bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public string GetPublicUrl(string key) =>
        string.Create(CultureInfo.InvariantCulture, $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}");

    public void Dispose() => _client.Dispose();
}
