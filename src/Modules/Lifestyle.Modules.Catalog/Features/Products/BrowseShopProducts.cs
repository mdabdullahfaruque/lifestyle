using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Internal;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Paging;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Catalog.Features.Products;

/// <summary>
/// One shop's published products, addressed by shop slug — the marketplace's shop-profile page.
/// <para>
/// This exists rather than a <c>vendorId</c> filter on <see cref="BrowseCatalog"/> because those
/// are different guarantees. On a storefront host the browse endpoint derives the vendor from the
/// <c>Host</c> header and accepts no vendor from the client, which is what stops a shop being made
/// to serve another shop's catalogue (FRD §19.4). Adding a client-supplied vendor filter there
/// would put a hole in exactly that rule. Here the shop *is* the address, so scoping to it is the
/// whole point rather than a bypass.
/// </para>
/// <para>
/// A storefront host still wins: if this is somehow called on one, the host's own vendor is
/// enforced, so a shop cannot serve a rival's products from its own domain.
/// </para>
/// </summary>
internal static class BrowseShopProducts
{
    public sealed record Request(string Slug, string? Sort, int Page = 1, int PageSize = 24);

    internal sealed class Handler(
        ICatalogDbContext db,
        ITenantContext tenant,
        IVendorsModule vendors,
        IOptions<CatalogModuleOptions> options)
        : IHandler<Request, Result<PagedResult<ProductListItemResponse>>>
    {
        public async Task<Result<PagedResult<ProductListItemResponse>>> Handle(Request request, CancellationToken ct)
        {
            var vendor = await vendors.GetBySlugAsync(request.Slug, ct);

            // A shop that cannot sell reads as absent, not as empty — same rule as GetStorefront.
            if (vendor is null || !vendor.CanSell)
                return Error.NotFound("vendors.storefront_not_found", "No shop exists at that address.");

            if (tenant.VendorId is { } hostVendorId && hostVendorId != vendor.Id)
                return Error.NotFound("vendors.storefront_not_found", "No shop exists at that address.");

            var query = db.Products
                .AsNoTracking()
                .Where(p => p.Status == ProductStatus.Published && p.VendorId == vendor.Id);

            query = request.Sort switch
            {
                "price_asc" => query.OrderBy(p => p.MinPrice).ThenBy(p => p.Id),
                "price_desc" => query.OrderByDescending(p => p.MinPrice).ThenBy(p => p.Id),
                "name" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
                _ => query.OrderByDescending(p => p.PublishedAt).ThenBy(p => p.Id)
            };

            var page = new PageRequest
            {
                Page = request.Page,
                PageSize = Math.Min(request.PageSize, options.Value.MaxPageSize)
            };

            var total = await query.CountAsync(ct);

            var rows = await query
                .Skip(page.Skip)
                .Take(page.Size)
                .Select(p => new ProductListRow(
                    p.Id, p.VendorId, p.Name, p.Slug, p.Status, p.MinPrice, p.MaxPrice, p.Currency,
                    p.TotalStock,
                    p.Images.OrderBy(i => i.Position).Select(i => i.MediaId).FirstOrDefault(),
                    p.PublishedAt,
                    p.Variants.Where(v => v.IsActive).Max(v => v.CompareAtPrice)))
                .ToListAsync(ct);

            return PagedResult<ProductListItemResponse>.From([.. rows.Select(r => r.ToResponse())], page, total);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/shops/{slug}/products", async (
                [AsParameters] Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("BrowseShopProducts")
            .WithSummary("Published products for one shop, by shop slug.")
            .AllowAnonymous()
            .Produces<PagedResult<ProductListItemResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}
