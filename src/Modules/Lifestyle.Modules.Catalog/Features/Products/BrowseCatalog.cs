using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.Modules.Catalog.Internal;
using Lifestyle.Modules.Catalog.Persistence;
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
/// The public product list. On a storefront host it is implicitly scoped to that shop — the client
/// never passes a vendor id, so no amount of parameter tampering reaches another shop's catalogue
/// (FRD §19.4).
/// </summary>
internal static class BrowseProducts
{
    public sealed record Request(
        Guid? CategoryId,
        string? VendorSlug,
        string? Search,
        decimal? MinPrice,
        decimal? MaxPrice,
        string? Sort,
        int Page = 1,
        int PageSize = 24);

    internal sealed class Handler(
        ICatalogDbContext db,
        ITenantContext tenant,
        IOptions<CatalogModuleOptions> options)
        : IHandler<Request, Result<PagedResult<ProductListItemResponse>>>
    {
        public async Task<Result<PagedResult<ProductListItemResponse>>> Handle(Request request, CancellationToken ct)
        {
            var query = db.Products
                .AsNoTracking()
                .Where(p => p.Status == ProductStatus.Published);

            // The host decides the tenant, not the query string.
            if (tenant.VendorId is { } hostVendorId)
                query = query.Where(p => p.VendorId == hostVendorId);

            if (request.CategoryId is { } categoryId)
            {
                var category = await db.Categories.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == categoryId, ct);

                if (category is null) return Error.NotFound("catalog.category_not_found");

                // Match the category and everything beneath it, via the materialised path.
                var descendants = db.Categories
                    .Where(c => c.Id == categoryId || c.Path.StartsWith(category.DescendantPathPrefix))
                    .Select(c => c.Id);

                query = query.Where(p => descendants.Contains(p.CategoryId));
            }

            if (request.MinPrice is { } min) query = query.Where(p => p.MaxPrice >= min);
            if (request.MaxPrice is { } max) query = query.Where(p => p.MinPrice <= max);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var term = $"%{request.Search.Trim().ToLowerInvariant()}%";
#pragma warning disable CA1304, CA1311 // Translated to SQL lower(); see VendorApprovalQueue for the full note.
                query = query.Where(p =>
                    EF.Functions.Like(p.Name.ToLower(), term) ||
                    (p.Brand != null && EF.Functions.Like(p.Brand.ToLower(), term)));
#pragma warning restore CA1304, CA1311
            }

            query = request.Sort switch
            {
                "price_asc" => query.OrderBy(p => p.MinPrice).ThenBy(p => p.Id),
                "price_desc" => query.OrderByDescending(p => p.MinPrice).ThenBy(p => p.Id),
                "name" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
                // Default is newest-first. A stable ThenBy(Id) keeps paging from repeating or
                // skipping rows when two products share a timestamp.
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
                    // Highest "was" price across active variants, so a grid card can badge the
                    // saving without a second request per product.
                    p.Variants.Where(v => v.IsActive).Max(v => v.CompareAtPrice)))
                .ToListAsync(ct);

            return PagedResult<ProductListItemResponse>.From([.. rows.Select(r => r.ToResponse())], page, total);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/products", async ([AsParameters] Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("BrowseProducts")
            .WithSummary("Public product list. Scoped to the shop when called on a storefront host.")
            .AllowAnonymous()
            .Produces<PagedResult<ProductListItemResponse>>();
}

/// <summary>
/// The public product detail page, addressed by vendor slug + product slug so URLs are readable
/// and stable.
/// </summary>
internal static class GetPublicProduct
{
    internal sealed class Handler(ICatalogDbContext db, ITenantContext tenant, IVendorsModule vendors)
        : IHandler<Handler.Query, Result<ProductResponse>>
    {
        internal sealed record Query(Guid VendorId, string ProductSlug);

        public async Task<Result<ProductResponse>> Handle(Query query, CancellationToken ct)
        {
            // A storefront host may only serve its own products, whatever the route says.
            if (tenant.VendorId is { } hostVendorId && hostVendorId != query.VendorId)
                return Error.NotFound("catalog.product_not_found");

            var product = await db.Products
                .Include(p => p.Variants)
                .Include(p => p.Images)
                .Include(p => p.AttributeValues)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.VendorId == query.VendorId
                                          && p.Slug == query.ProductSlug
                                          && p.Status == ProductStatus.Published, ct);

            if (product is null) return Error.NotFound("catalog.product_not_found");

            // The shop rides along with the product. A buyer cannot order without the seller's
            // WhatsApp number, and the alternative — a second lookup keyed by a slug this response
            // does not carry — is a round trip the client has no way to make.
            var shop = await vendors.GetAsync(product.VendorId, ct);

            return product.ToResponse() with
            {
                Shop = shop is null
                    ? null
                    : new ProductShopResponse(shop.Id, shop.DisplayName, shop.Slug,
                        shop.WhatsAppNumber, shop.AccentColour, shop.LogoMediaId),
            };
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/vendors/{vendorId:guid}/products/{slug}", async (
                Guid vendorId, string slug, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Query(vendorId, slug), ct)).ToHttpResult())
            .WithName("GetPublicProduct")
            .WithSummary("A published product by shop and product slug.")
            .AllowAnonymous()
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}
