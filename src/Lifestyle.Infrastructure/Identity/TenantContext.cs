using Lifestyle.SharedKernel.Abstractions;

namespace Lifestyle.Infrastructure.Identity;

/// <summary>
/// Mutable holder populated once per request by the tenant-resolution middleware, then read as the
/// immutable <see cref="ITenantContext"/> everywhere else. Registered scoped, so one instance per
/// request.
/// </summary>
internal sealed class TenantContext : ITenantContext
{
    public HostKind HostKind { get; private set; } = HostKind.Api;
    public string Host { get; private set; } = string.Empty;
    public string RootDomain { get; private set; } = string.Empty;
    public Guid? VendorId { get; private set; }
    public string? StorefrontSlug { get; private set; }
    public bool IsCustomDomain { get; private set; }

    public void Set(HostKind kind, string host, string rootDomain, Guid? vendorId, string? slug, bool isCustomDomain)
    {
        HostKind = kind;
        Host = host;
        RootDomain = rootDomain;
        VendorId = vendorId;
        StorefrontSlug = slug;
        IsCustomDomain = isCustomDomain;
    }
}
