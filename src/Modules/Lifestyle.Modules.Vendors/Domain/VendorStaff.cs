using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Vendors.Domain;

/// <summary>
/// Links a user to a vendor. The matching role grant lives in Identity (scoped by vendor id) —
/// this row is the vendor's own view of its team, and the source of truth for who may act for it.
/// </summary>
internal sealed class VendorStaff : Entity
{
    private VendorStaff() { }

    public Guid VendorId { get; private set; }
    public Guid UserId { get; private set; }
    public VendorStaffRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }

    public static VendorStaff Create(Guid vendorId, Guid userId, VendorStaffRole role, DateTimeOffset now) => new()
    {
        VendorId = vendorId,
        UserId = userId,
        Role = role,
        JoinedAt = now
    };
}

internal enum VendorStaffRole
{
    Owner = 1,
    Manager = 2,

    /// <summary>Can edit the catalogue but not billing, staff or payouts.</summary>
    Staff = 3
}

/// <summary>A KYC document supporting a vendor application (Plan Phase 1).</summary>
internal sealed class VendorDocument : Entity
{
    private VendorDocument() { }

    public Guid VendorId { get; private set; }
    public VendorDocumentKind Kind { get; private set; }

    /// <summary>Id of the stored object in Media. The file itself never touches this table.</summary>
    public string MediaId { get; private set; } = null!;

    public string FileName { get; private set; } = null!;
    public DateTimeOffset UploadedAt { get; private set; }

    public static VendorDocument Create(Guid vendorId, VendorDocumentKind kind, string mediaId, string fileName, DateTimeOffset now) => new()
    {
        VendorId = vendorId,
        Kind = kind,
        MediaId = mediaId,
        FileName = fileName,
        UploadedAt = now
    };
}

internal enum VendorDocumentKind
{
    BusinessRegistration = 1,
    OwnerIdentity = 2,
    BankStatement = 3,
    TaxCertificate = 4,
    Other = 99
}
