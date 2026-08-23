using Lifestyle.Modules.Vendors.Domain;
using Lifestyle.Modules.Vendors.Features.Vendors;
using Lifestyle.Modules.Vendors.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Vendors.Features.Storefronts;

/// <summary>
/// Public shop lookup by slug — the marketplace shop-profile page and the standalone shop site both
/// start here. Only approved vendors resolve; a suspended shop must read as gone, not as broken.
/// </summary>
internal static class GetStorefront
{
    internal sealed class Handler(IVendorsDbContext db) : IHandler<string, Result<StorefrontResponse>>
    {
        public async Task<Result<StorefrontResponse>> Handle(string slug, CancellationToken ct)
        {
            var vendor = await db.Vendors
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Slug == slug && v.Status == VendorStatus.Approved, ct);

            return vendor is null
                ? Error.NotFound("vendors.storefront_not_found", "No shop exists at that address.")
                : vendor.ToStorefront();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{slug}", async (string slug, Handler handler, CancellationToken ct) =>
                (await handler.Handle(slug, ct)).ToHttpResult())
            .WithName("GetStorefront")
            .WithSummary("Public shop profile for a storefront slug.")
            .AllowAnonymous()
            .Produces<StorefrontResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>
/// Resolves the storefront implied by the request host. The client passes nothing — the tenant
/// middleware has already worked out which shop this host belongs to (FRD §5.2, §19.4).
/// </summary>
internal static class GetCurrentStorefront
{
    internal sealed class Handler(IVendorsDbContext db, ITenantContext tenant)
        : IHandler<Unit, Result<StorefrontResponse>>
    {
        public async Task<Result<StorefrontResponse>> Handle(Unit _, CancellationToken ct)
        {
            if (tenant.VendorId is not { } vendorId)
                return Error.NotFound("vendors.not_a_storefront_host", "This host is not a shop address.");

            var vendor = await db.Vendors
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == vendorId && v.Status == VendorStatus.Approved, ct);

            return vendor is null
                ? Error.NotFound("vendors.storefront_not_found", "No shop exists at that address.")
                : vendor.ToStorefront();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/current", async (Handler handler, CancellationToken ct) =>
                (await handler.Handle(Unit.Value, ct)).ToHttpResult())
            .WithName("GetCurrentStorefront")
            .WithSummary("The shop this request's host belongs to.")
            .AllowAnonymous()
            .Produces<StorefrontResponse>();
}
