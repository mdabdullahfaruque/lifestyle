using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lifestyle.Modules.Vendors.Features.Storefronts;

/// <summary>
/// The authorisation callback for Caddy's on-demand TLS (docs/05 §7).
/// <para>
/// When an unknown hostname arrives, Caddy asks this endpoint whether we recognise it before
/// requesting a certificate. Without the check, the edge is an open certificate-request relay:
/// anyone could point DNS at us and make us hammer Let's Encrypt until we hit its rate limits,
/// which is a denial-of-service on our own ability to issue certificates.
/// </para>
/// <para>
/// 200 means "issue a certificate", anything else means "refuse". Approved:
/// a registered custom domain belonging to an approved vendor, or — for the DNS-only edge mode,
/// where the wildcard has no proxy in front of it — <c>{slug}.{root}</c> for an approved vendor's
/// slug. The root domain comes from configuration via <see cref="ITenantContext.RootDomain"/>,
/// never from the request, so a spoofed Host header cannot widen the check.
/// </para>
/// </summary>
internal static class CheckCustomDomain
{
    internal sealed class Handler(IVendorsModule vendors, ITenantContext tenant)
        : IHandler<string, bool>
    {
        public async Task<bool> Handle(string domain, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(domain) || domain.Length > 253) return false;

            var host = domain.Trim().TrimEnd('.').ToLowerInvariant();
            var root = tenant.RootDomain.ToLowerInvariant();

            if (!string.IsNullOrEmpty(root) && host.EndsWith($".{root}", StringComparison.Ordinal))
            {
                var label = host[..^(root.Length + 1)];

                // Exactly one label ({slug}.{root}); anything deeper is not a storefront host.
                if (label.Length == 0 || label.Contains('.', StringComparison.Ordinal)) return false;

                return await vendors.ResolveStorefrontAsync(label, isCustomDomain: false, ct) is not null;
            }

            return await vendors.ResolveStorefrontAsync(host, isCustomDomain: true, ct) is not null;
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/tls-check", async (string? domain, Handler handler, CancellationToken ct) =>
                await handler.Handle(domain ?? string.Empty, ct)
                    ? Results.Ok()
                    : Results.NotFound())
            .WithName("CheckCustomDomain")
            .WithSummary("Caddy on-demand TLS authorisation. Not publicly routed.")
            .AllowAnonymous()
            .ExcludeFromDescription();
}
