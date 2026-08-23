using Lifestyle.Modules.Vendors.Domain.Events;
using Lifestyle.SharedKernel.Domain;
using Lifestyle.SharedKernel.Results;

namespace Lifestyle.Modules.Vendors.Domain;

/// <summary>
/// A selling business. Owns its storefront identity (slug, branding) and its staff. Created by the
/// applicant, then approved by a platform admin before anything it publishes becomes visible.
/// </summary>
internal sealed class Vendor : AggregateRoot, ISoftDeletable
{
    private readonly List<VendorStaff> _staff = [];
    private readonly List<VendorDocument> _documents = [];

    private Vendor() { }

    public string LegalName { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;

    /// <summary>Subdomain label. Unique, immutable in practice — changing it breaks every link.</summary>
    public string Slug { get; private set; } = null!;

    public string? CustomDomain { get; private set; }
    public CustomDomainStatus CustomDomainStatus { get; private set; } = CustomDomainStatus.None;

    public string ContactEmail { get; private set; } = null!;
    public string ContactPhone { get; private set; } = null!;

    /// <summary>Business registration number (SSM in Malaysia, trade licence in Bangladesh).</summary>
    public string? RegistrationNumber { get; private set; }

    public VendorStatus Status { get; private set; } = VendorStatus.Draft;
    public string? StatusReason { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }

    // ── Storefront presentation ──
    public string? LogoMediaId { get; private set; }
    public string? BannerMediaId { get; private set; }
    public string? About { get; private set; }
    public string? AccentColour { get; private set; }

    /// <summary>Where "Order on WhatsApp" sends the buyer in v1 (Plan §6.2).</summary>
    public string? WhatsAppNumber { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public IReadOnlyCollection<VendorStaff> Staff => _staff.AsReadOnly();
    public IReadOnlyCollection<VendorDocument> Documents => _documents.AsReadOnly();

    public bool CanSell => Status == VendorStatus.Approved;

    public static Vendor Apply(
        string legalName, string displayName, string slug, string contactEmail, string contactPhone,
        string? registrationNumber, Guid ownerUserId, DateTimeOffset now)
    {
        var vendor = new Vendor
        {
            LegalName = legalName.Trim(),
            DisplayName = displayName.Trim(),
            Slug = slug,
            ContactEmail = contactEmail.Trim().ToLowerInvariant(),
            ContactPhone = contactPhone.Trim(),
            RegistrationNumber = string.IsNullOrWhiteSpace(registrationNumber) ? null : registrationNumber.Trim(),
            Status = VendorStatus.Draft,
            CreatedAt = now
        };

        vendor._staff.Add(VendorStaff.Create(vendor.Id, ownerUserId, VendorStaffRole.Owner, now));
        vendor.Raise(new VendorApplied(vendor.Id, vendor.DisplayName, ownerUserId, now));
        return vendor;
    }

    public void AddDocument(VendorDocumentKind kind, string mediaId, string fileName, DateTimeOffset now)
    {
        // Re-uploading a document type replaces the previous one rather than accumulating —
        // the reviewer should see one current copy of each, not a pile.
        _documents.RemoveAll(d => d.Kind == kind);
        _documents.Add(VendorDocument.Create(Id, kind, mediaId, fileName, now));
        UpdatedAt = now;
    }

    /// <summary>
    /// Moves Draft → PendingReview. Requires the KYC documents that let a human actually decide;
    /// letting an incomplete application into the queue just moves the work to the reviewer.
    /// </summary>
    public Result SubmitForReview(DateTimeOffset now)
    {
        if (Status is not (VendorStatus.Draft or VendorStatus.Rejected))
            return Error.Conflict("vendors.not_submittable", $"A vendor in {Status} state cannot be submitted for review.");

        if (!_documents.Any(d => d.Kind == VendorDocumentKind.BusinessRegistration))
            return Error.Validation("vendors.registration_document_missing", "A business registration document is required.");

        if (!_documents.Any(d => d.Kind == VendorDocumentKind.OwnerIdentity))
            return Error.Validation("vendors.identity_document_missing", "An owner identity document is required.");

        Status = VendorStatus.PendingReview;
        StatusReason = null;
        SubmittedAt = now;
        UpdatedAt = now;

        Raise(new VendorSubmittedForReview(Id, DisplayName, now));
        return Result.Success();
    }

    public Result Approve(Guid approvedBy, DateTimeOffset now)
    {
        if (Status != VendorStatus.PendingReview)
            return Error.Conflict("vendors.not_pending", "Only a vendor awaiting review can be approved.");

        Status = VendorStatus.Approved;
        StatusReason = null;
        ApprovedAt = now;
        ApprovedBy = approvedBy;
        UpdatedAt = now;

        Raise(new VendorApproved(Id, DisplayName, Slug, OwnerUserId, now));
        return Result.Success();
    }

    public Result Reject(Guid rejectedBy, string reason, DateTimeOffset now)
    {
        if (Status != VendorStatus.PendingReview)
            return Error.Conflict("vendors.not_pending", "Only a vendor awaiting review can be rejected.");

        Status = VendorStatus.Rejected;
        StatusReason = reason;
        UpdatedAt = now;

        Raise(new VendorRejected(Id, reason, rejectedBy, now));
        return Result.Success();
    }

    public Result Suspend(Guid suspendedBy, string reason, DateTimeOffset now)
    {
        if (Status != VendorStatus.Approved)
            return Error.Conflict("vendors.not_approved", "Only an approved vendor can be suspended.");

        Status = VendorStatus.Suspended;
        StatusReason = reason;
        UpdatedAt = now;

        // Everything this vendor has published must come down; Catalog reacts to this event.
        Raise(new VendorSuspended(Id, reason, suspendedBy, now));
        return Result.Success();
    }

    public Result Reinstate(DateTimeOffset now)
    {
        if (Status != VendorStatus.Suspended)
            return Error.Conflict("vendors.not_suspended", "Only a suspended vendor can be reinstated.");

        Status = VendorStatus.Approved;
        StatusReason = null;
        UpdatedAt = now;

        Raise(new VendorReinstated(Id, now));
        return Result.Success();
    }

    public void UpdateStorefront(
        string displayName, string? about, string? logoMediaId, string? bannerMediaId,
        string? accentColour, string? whatsAppNumber, DateTimeOffset now)
    {
        DisplayName = displayName.Trim();
        About = string.IsNullOrWhiteSpace(about) ? null : about.Trim();
        LogoMediaId = logoMediaId;
        BannerMediaId = bannerMediaId;
        AccentColour = string.IsNullOrWhiteSpace(accentColour) ? null : accentColour.Trim();
        WhatsAppNumber = string.IsNullOrWhiteSpace(whatsAppNumber) ? null : whatsAppNumber.Trim();
        UpdatedAt = now;
    }

    public void UpdateContact(string contactEmail, string contactPhone, DateTimeOffset now)
    {
        ContactEmail = contactEmail.Trim().ToLowerInvariant();
        ContactPhone = contactPhone.Trim();
        UpdatedAt = now;
    }

    public Result RequestCustomDomain(string domain, DateTimeOffset now)
    {
        if (Status != VendorStatus.Approved)
            return Error.Conflict("vendors.not_approved", "A custom domain can only be attached to an approved vendor.");

        CustomDomain = domain.Trim().ToLowerInvariant();
        CustomDomainStatus = CustomDomainStatus.PendingVerification;
        UpdatedAt = now;
        return Result.Success();
    }

    public void ActivateCustomDomain(DateTimeOffset now)
    {
        CustomDomainStatus = CustomDomainStatus.Active;
        UpdatedAt = now;
    }

    public Result AddStaff(Guid userId, VendorStaffRole role, DateTimeOffset now)
    {
        if (_staff.Any(s => s.UserId == userId))
            return Error.Conflict("vendors.staff_exists", "That person is already on this vendor's team.");

        if (role == VendorStaffRole.Owner)
            return Error.Validation("vendors.single_owner", "A vendor has exactly one owner; transfer ownership instead.");

        _staff.Add(VendorStaff.Create(Id, userId, role, now));
        UpdatedAt = now;
        return Result.Success();
    }

    public Result RemoveStaff(Guid userId, DateTimeOffset now)
    {
        var member = _staff.FirstOrDefault(s => s.UserId == userId);
        if (member is null) return Error.NotFound("vendors.staff_not_found");

        if (member.Role == VendorStaffRole.Owner)
            return Error.Validation("vendors.cannot_remove_owner", "The owner cannot be removed; transfer ownership first.");

        _staff.Remove(member);
        UpdatedAt = now;
        return Result.Success();
    }

    public Guid OwnerUserId => _staff.First(s => s.Role == VendorStaffRole.Owner).UserId;

    public bool HasStaff(Guid userId) => _staff.Any(s => s.UserId == userId);
}

internal enum VendorStatus
{
    /// <summary>Application started, not yet submitted. Not visible anywhere.</summary>
    Draft = 1,
    PendingReview = 2,
    Approved = 3,
    Rejected = 4,
    Suspended = 5
}

internal enum CustomDomainStatus
{
    None = 0,
    PendingVerification = 1,
    Active = 2,
    Failed = 3
}
