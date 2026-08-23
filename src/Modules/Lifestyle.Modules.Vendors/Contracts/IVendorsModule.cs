using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Vendors.Contracts;

/// <summary>The only surface other modules may use. Catalog uses it to check a vendor may sell.</summary>
public interface IVendorsModule
{
    Task<VendorSnapshot?> GetAsync(Guid vendorId, CancellationToken ct);

    Task<VendorSnapshot?> GetBySlugAsync(string slug, CancellationToken ct);

    /// <summary>Resolves a storefront host (subdomain label or custom domain) to a vendor.</summary>
    Task<VendorSnapshot?> ResolveStorefrontAsync(string slugOrDomain, bool isCustomDomain, CancellationToken ct);

    Task<IReadOnlyList<VendorSnapshot>> GetManyAsync(IReadOnlyCollection<Guid> vendorIds, CancellationToken ct);

    /// <summary>True when the vendor exists, is approved and is not suspended.</summary>
    Task<bool> CanSellAsync(Guid vendorId, CancellationToken ct);

    /// <summary>True when the user is on that vendor's staff. Used for ownership checks.</summary>
    Task<bool> IsStaffAsync(Guid vendorId, Guid userId, CancellationToken ct);
}

public sealed record VendorSnapshot(
    Guid Id,
    string DisplayName,
    string Slug,
    string? CustomDomain,
    string? LogoMediaId,
    string? BannerMediaId,
    string? AccentColour,
    string? WhatsAppNumber,
    string? About,
    bool CanSell);

// ── Integration events ──

public sealed record VendorApprovedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid VendorId,
    string DisplayName,
    string Slug,
    Guid OwnerUserId) : IIntegrationEvent;

/// <summary>
/// Catalog listens for this and unpublishes everything the vendor has live — a suspended vendor's
/// products must not stay purchasable.
/// </summary>
public sealed record VendorSuspendedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid VendorId,
    string Reason) : IIntegrationEvent;

public sealed record VendorReinstatedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid VendorId) : IIntegrationEvent;
