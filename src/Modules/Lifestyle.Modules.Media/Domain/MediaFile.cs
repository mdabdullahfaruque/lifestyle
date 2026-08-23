using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Media.Domain;

/// <summary>
/// The record of one uploaded file. The bytes live in object storage; this row is the index, the
/// ownership claim and the audit trail.
/// </summary>
internal sealed class MediaFile : AggregateRoot
{
    private readonly List<MediaDerivative> _derivatives = [];

    private MediaFile() { }

    /// <summary>
    /// A storage-safe public id (32 hex chars), not the primary key. It appears in URLs, so it is
    /// deliberately not a Guid the rest of the system might correlate against.
    /// </summary>
    public string PublicId { get; private set; } = null!;

    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string StorageKey { get; private set; } = null!;

    public int? Width { get; private set; }
    public int? Height { get; private set; }

    /// <summary>Who uploaded it — used for quota, abuse tracing and vendor scoping.</summary>
    public Guid UploadedByUserId { get; private set; }
    public Guid? VendorId { get; private set; }

    /// <summary>
    /// Null until something claims the file. Orphans older than 24 hours are swept, so an
    /// abandoned upload does not become permanent storage cost.
    /// </summary>
    public string? OwnerType { get; private set; }
    public Guid? OwnerId { get; private set; }
    public DateTimeOffset? AttachedAt { get; private set; }

    public IReadOnlyCollection<MediaDerivative> Derivatives => _derivatives.AsReadOnly();

    public bool IsOrphan => OwnerType is null;

    public static MediaFile Record(
        string publicId, string fileName, string contentType, long sizeBytes, string storageKey,
        int? width, int? height, Guid uploadedBy, Guid? vendorId, DateTimeOffset now) => new()
        {
            PublicId = publicId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StorageKey = storageKey,
            Width = width,
            Height = height,
            UploadedByUserId = uploadedBy,
            VendorId = vendorId,
            CreatedAt = now
        };

    public void AddDerivative(string variant, string storageKey, int width, int height, long sizeBytes)
    {
        _derivatives.RemoveAll(d => d.Variant == variant);
        _derivatives.Add(MediaDerivative.Create(Id, variant, storageKey, width, height, sizeBytes));
    }

    public void Attach(string ownerType, Guid ownerId, DateTimeOffset now)
    {
        OwnerType = ownerType;
        OwnerId = ownerId;
        AttachedAt = now;
        UpdatedAt = now;
    }
}

/// <summary>A resized copy of a <see cref="MediaFile"/>.</summary>
internal sealed class MediaDerivative : Entity
{
    private MediaDerivative() { }

    public Guid MediaFileId { get; private set; }
    public string Variant { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public long SizeBytes { get; private set; }

    public static MediaDerivative Create(Guid mediaFileId, string variant, string storageKey, int width, int height, long sizeBytes) => new()
    {
        MediaFileId = mediaFileId,
        Variant = variant,
        StorageKey = storageKey,
        Width = width,
        Height = height,
        SizeBytes = sizeBytes
    };
}
