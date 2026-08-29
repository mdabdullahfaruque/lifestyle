namespace Lifestyle.SharedKernel.Abstractions;

/// <summary>Which of the five surfaces (Plan §1.1) the request arrived on.</summary>
public enum HostKind
{
    /// <summary>www.{platform}.com — the marketplace.</summary>
    Marketplace,

    /// <summary>{slug}.{platform}.com or a vendor's custom domain — a standalone shop site.</summary>
    Storefront,

    /// <summary>seller.{platform}.com</summary>
    Seller,

    /// <summary>admin.{platform}.com</summary>
    Admin,

    /// <summary>api.{platform}.com and anything unrecognised.</summary>
    Api
}

/// <summary>
/// Resolved once per request by TenantResolutionMiddleware from the Host header (FRD §5.2).
/// On a storefront host, catalog reads are implicitly scoped to <see cref="VendorId"/> — the client
/// never passes a vendor id, so parameter tampering cannot leak another vendor's data.
/// </summary>
public interface ITenantContext
{
    HostKind HostKind { get; }
    string Host { get; }

    /// <summary>The platform's apex domain (<c>example.com</c>), from configuration — not derived
    /// from the request, so it is trustworthy even on internal or spoofed-Host calls.</summary>
    string RootDomain { get; }

    /// <summary>The storefront's vendor, when the host resolves to one.</summary>
    Guid? VendorId { get; }

    string? StorefrontSlug { get; }

    /// <summary>True when the request arrived on a vendor-owned custom domain rather than a subdomain.</summary>
    bool IsCustomDomain { get; }
}
