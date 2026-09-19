using Lifestyle.SharedKernel.Results;

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
    /// The images in a vendor's library. Bulk import uses this to match filenames against product
    /// codes (docs/08 §4) — Catalog cannot read Media's tables, so the library is offered here.
    /// </summary>
    /// <param name="unusedOnly">
    /// True to return only images not yet on a product, which is what an import should consider:
    /// an image already placed on one product should not be silently claimed by another.
    /// </param>
    Task<IReadOnlyList<MediaAsset>> ListForVendorAsync(Guid vendorId, bool unusedOnly, CancellationToken ct);

    /// <summary>
    /// Downloads an image the seller named by URL and files it in their library (docs/08 §7.2).
    /// <para>
    /// <b>Off unless <c>Media:RemoteImageImport:Enabled</c> is set</b>, and it returns a failure
    /// saying so when it is not. Fetching a seller-chosen URL means letting them choose what this
    /// server connects to, which is why the image library is the preferred path.
    /// </para>
    /// </summary>
    Task<Result<MediaAsset>> ImportFromUrlAsync(string url, CancellationToken ct);

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
    IReadOnlyDictionary<string, string> Derivatives,
    /// <summary>
    /// EXIF capture time, or null when the file carries none. Bulk import groups a dump of
    /// unnamed phone photos by the gaps between these (docs/08 §4.3).
    /// </summary>
    DateTimeOffset? CapturedAt = null);

/// <summary>
/// What a stored file belongs to. A file with no owner is an orphan and the sweeper deletes it
/// after <c>Media:OrphanRetentionHours</c>, so anything meant to survive must claim one of these.
/// <para>
/// Constants rather than loose strings because the value decides whether a file lives or is
/// deleted, and a typo in a literal would be a silent data loss rather than a compile error.
/// </para>
/// </summary>
public static class MediaOwnerTypes
{
    /// <summary>Attached to a product's image set.</summary>
    public const string Product = "product";

    /// <summary>
    /// Held in a vendor's image library: uploaded deliberately, not yet on any product, and not to
    /// be swept. <c>OwnerId</c> is the vendor id.
    /// </summary>
    public const string VendorLibrary = "vendor_library";

    /// <summary>
    /// A KYC document on a vendor application — trade licence, owner identity, bank statement.
    /// <c>OwnerId</c> is the vendor id.
    /// </summary>
    public const string VendorDocument = "vendor_document";

    /// <summary>A vendor's shop logo or banner. <c>OwnerId</c> is the vendor id.</summary>
    public const string VendorBranding = "vendor_branding";
}

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
