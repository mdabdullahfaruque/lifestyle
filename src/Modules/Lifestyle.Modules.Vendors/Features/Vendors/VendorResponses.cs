using Lifestyle.Modules.Vendors.Domain;

namespace Lifestyle.Modules.Vendors.Features.Vendors;

/// <summary>
/// Response shapes shared across this resource's features, with their mapping. Kept in one file so
/// a field added to the entity has exactly one place to be surfaced (docs/04 §3.3).
/// </summary>
public sealed record VendorResponse(
    Guid Id,
    string LegalName,
    string DisplayName,
    string Slug,
    string ContactEmail,
    string ContactPhone,
    string? RegistrationNumber,
    string Status,
    string? StatusReason,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    string? CustomDomain,
    string CustomDomainStatus,
    string? LogoMediaId,
    string? BannerMediaId,
    string? AccentColour,
    string? About,
    string? WhatsAppNumber,
    IReadOnlyList<VendorDocumentResponse> Documents,
    IReadOnlyList<VendorStaffResponse> Staff);

public sealed record VendorDocumentResponse(Guid Id, string Kind, string MediaId, string FileName, DateTimeOffset UploadedAt);

public sealed record VendorStaffResponse(Guid UserId, string Role, DateTimeOffset JoinedAt);

/// <summary>The list row in the admin approval queue — deliberately narrow.</summary>
public sealed record VendorListItemResponse(
    Guid Id,
    string DisplayName,
    string LegalName,
    string Slug,
    string Status,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset CreatedAt);

/// <summary>What a buyer sees on a storefront. No contact email, no KYC, no staff.</summary>
public sealed record StorefrontResponse(
    Guid Id,
    string DisplayName,
    string Slug,
    string? About,
    string? LogoMediaId,
    string? BannerMediaId,
    string? AccentColour,
    string? WhatsAppNumber,
    string? CustomDomain);

internal static class VendorMapping
{
    public static VendorResponse ToResponse(this Vendor v) => new(
        v.Id, v.LegalName, v.DisplayName, v.Slug, v.ContactEmail, v.ContactPhone, v.RegistrationNumber,
        v.Status.ToString(), v.StatusReason, v.SubmittedAt, v.ApprovedAt,
        v.CustomDomain, v.CustomDomainStatus.ToString(),
        v.LogoMediaId, v.BannerMediaId, v.AccentColour, v.About, v.WhatsAppNumber,
        [.. v.Documents.Select(d => new VendorDocumentResponse(d.Id, d.Kind.ToString(), d.MediaId, d.FileName, d.UploadedAt))],
        [.. v.Staff.Select(s => new VendorStaffResponse(s.UserId, s.Role.ToString(), s.JoinedAt))]);

    public static StorefrontResponse ToStorefront(this Vendor v) => new(
        v.Id, v.DisplayName, v.Slug, v.About, v.LogoMediaId, v.BannerMediaId,
        v.AccentColour, v.WhatsAppNumber, v.CustomDomain);
}
