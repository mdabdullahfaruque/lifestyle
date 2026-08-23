namespace Lifestyle.Modules.Media.Contracts;

/// <summary>
/// Media owns files. Other modules store a <see cref="MediaAsset.Id"/> and ask for URLs — they
/// never learn the storage layout, which is what lets us move from local disk to S3 without
/// touching Catalog or Vendors.
/// </summary>
public interface IMediaModule
{
    Task<MediaAsset?> GetAsync(string mediaId, CancellationToken ct);

    Task<IReadOnlyList<MediaAsset>> GetManyAsync(IReadOnlyCollection<string> mediaIds, CancellationToken ct);

    /// <summary>
    /// Marks assets as belonging to something real. Uploads start orphaned and are swept after
    /// 24 hours, so a failed product save cannot leave files behind forever.
    /// </summary>
    Task AttachAsync(IReadOnlyCollection<string> mediaIds, string ownerType, Guid ownerId, CancellationToken ct);
}

/// <summary>One stored file plus the derivative sizes generated for it.</summary>
public sealed record MediaAsset(
    string Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    int? Width,
    int? Height,
    string Url,
    IReadOnlyDictionary<string, string> Derivatives);

/// <summary>
/// The derivative sizes we generate for every image. Named rather than numeric so a call site
/// asks for "thumbnail", not for 200px — the pixel value can change.
/// </summary>
public static class MediaVariants
{
    /// <summary>Grid tiles and cart rows.</summary>
    public const string Thumbnail = "thumb";

    /// <summary>Product cards in listings.</summary>
    public const string Card = "card";

    /// <summary>Product detail hero.</summary>
    public const string Large = "large";

    public static IReadOnlyList<string> All { get; } = [Thumbnail, Card, Large];
}
