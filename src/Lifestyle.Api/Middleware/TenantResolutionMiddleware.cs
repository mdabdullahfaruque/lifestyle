using Lifestyle.Api.Composition;
using Lifestyle.Infrastructure.Identity;
using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Lifestyle.Api.Middleware;

/// <summary>
/// Works out which of the five surfaces (Plan §1.1) a request arrived on, and — for a storefront —
/// which vendor owns the host (FRD §5.2).
/// <para>
/// This is a security boundary, not a convenience. Catalog reads on a storefront host are filtered
/// to the resolved vendor, so a storefront cannot be made to serve another shop's products by
/// tampering with a query parameter (FRD §19.4).
/// </para>
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next, IOptions<PlatformOptions> options)
{
    private readonly PlatformOptions _platform = options.Value;

    public async Task InvokeAsync(HttpContext context, TenantContext tenant, IVendorsModule vendors, IMemoryCache cache)
    {
        var host = context.Request.Host.Host.ToLowerInvariant();
        var root = _platform.RootDomain.ToLowerInvariant();

        if (host.Equals(_platform.ApiHost, StringComparison.OrdinalIgnoreCase) ||
            host is "localhost" or "127.0.0.1")
        {
            // Local development and the API host itself carry no tenant. A storefront can still be
            // exercised locally by passing ?storefront=slug, which is honoured below.
            var devSlug = context.Request.Query["storefront"].ToString();

            if (!string.IsNullOrWhiteSpace(devSlug))
            {
                await ResolveStorefrontAsync(tenant, vendors, cache, host, root, devSlug, isCustomDomain: false, context);
                await next(context);
                return;
            }

            tenant.Set(HostKind.Api, host, root, null, null, false);
            await next(context);
            return;
        }

        if (host.Equals(_platform.MarketplaceHost, StringComparison.OrdinalIgnoreCase) ||
            host.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            tenant.Set(HostKind.Marketplace, host, root, null, null, false);
        }
        else if (host.Equals(_platform.SellerHost, StringComparison.OrdinalIgnoreCase))
        {
            tenant.Set(HostKind.Seller, host, root, null, null, false);
        }
        else if (host.Equals(_platform.AdminHost, StringComparison.OrdinalIgnoreCase))
        {
            tenant.Set(HostKind.Admin, host, root, null, null, false);
        }
        else if (host.EndsWith($".{root}", StringComparison.OrdinalIgnoreCase))
        {
            // {slug}.{root} — a storefront subdomain.
            var slug = host[..^(root.Length + 1)];
            await ResolveStorefrontAsync(tenant, vendors, cache, host, root, slug, isCustomDomain: false, context);
        }
        else
        {
            // Anything else is a vendor's own custom domain, or noise.
            await ResolveStorefrontAsync(tenant, vendors, cache, host, root, host, isCustomDomain: true, context);
        }

        await next(context);
    }

    private static async Task ResolveStorefrontAsync(
        TenantContext tenant, IVendorsModule vendors, IMemoryCache cache,
        string host, string root, string slugOrDomain, bool isCustomDomain, HttpContext context)
    {
        var cacheKey = $"tenant:{(isCustomDomain ? "domain" : "slug")}:{slugOrDomain}";

        // Every storefront request needs this lookup, and vendor hosts change rarely. A short TTL
        // keeps a suspension from staying live for long while still absorbing the traffic.
        var vendor = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return await vendors.ResolveStorefrontAsync(slugOrDomain, isCustomDomain, context.RequestAborted);
        });

        if (vendor is null)
        {
            // Unknown host: no tenant. Public catalog endpoints then serve the marketplace view,
            // and the storefront endpoint returns 404, which is the honest answer.
            tenant.Set(HostKind.Marketplace, host, root, null, null, isCustomDomain);
            return;
        }

        tenant.Set(HostKind.Storefront, host, root, vendor.Id, vendor.Slug, isCustomDomain);
    }
}
