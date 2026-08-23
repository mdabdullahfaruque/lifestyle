using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Internal;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Paging;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Features.Products;

/// <summary>The vendor's own view of one product, in any state.</summary>
internal static class GetOwnProduct
{
    internal sealed class Handler(VendorScope scope) : IHandler<Guid, Result<ProductResponse>>
    {
        public async Task<Result<ProductResponse>> Handle(Guid productId, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedProductAsync(productId, ct);
            return loaded.IsFailure ? loaded.Error : loaded.Value.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{productId:guid}", async (Guid productId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(productId, ct)).ToHttpResult())
            .WithName("GetOwnProduct")
            .WithSummary("One of the caller's own products, in any state.")
            .RequirePermission(Permissions.Catalog.ReadOwn)
            .Produces<ProductResponse>();
}

/// <summary>The vendor's product list — the main Vendor Admin grid.</summary>
internal static class ListOwnProducts
{
    public sealed record Request(string? Status, string? Search, Guid? CategoryId, int Page = 1, int PageSize = 20);

    internal sealed class Handler(ICatalogDbContext db, VendorScope scope)
        : IHandler<Request, Result<PagedResult<ProductListItemResponse>>>
    {
        public async Task<Result<PagedResult<ProductListItemResponse>>> Handle(Request request, CancellationToken ct)
        {
            var vendorId = scope.RequireVendorId();
            if (vendorId.IsFailure) return vendorId.Error;

            var query = db.Products.AsNoTracking().Where(p => p.VendorId == vendorId.Value);

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<ProductStatus>(request.Status, ignoreCase: true, out var status))
                    return Error.Validation("catalog.status_invalid", $"Unknown status '{request.Status}'.");

                query = query.Where(p => p.Status == status);
            }

            if (request.CategoryId is { } categoryId)
                query = query.Where(p => p.CategoryId == categoryId);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var term = $"%{request.Search.Trim().ToLowerInvariant()}%";
#pragma warning disable CA1304, CA1311 // Translated to SQL lower(); see VendorApprovalQueue for the full note.
                query = query.Where(p => EF.Functions.Like(p.Name.ToLower(), term) || EF.Functions.Like(p.Slug, term));
#pragma warning restore CA1304, CA1311
            }

            var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
            var total = await query.CountAsync(ct);

            // Project the raw columns in SQL, then format. Decimal.ToString(format) has no SQL
            // translation, so doing it inline would throw at runtime rather than at compile time.
            var rows = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip(page.Skip)
                .Take(page.Size)
                .Select(p => new ProductListRow(
                    p.Id, p.VendorId, p.Name, p.Slug, p.Status, p.MinPrice, p.MaxPrice, p.Currency,
                    p.TotalStock,
                    p.Images.OrderBy(i => i.Position).Select(i => i.MediaId).FirstOrDefault(),
                    p.PublishedAt))
                .ToListAsync(ct);

            return PagedResult<ProductListItemResponse>.From([.. rows.Select(r => r.ToResponse())], page, total);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/", async ([AsParameters] Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("ListOwnProducts")
            .WithSummary("The caller's product catalogue, filterable by status and category.")
            .RequirePermission(Permissions.Catalog.ReadOwn)
            .Produces<PagedResult<ProductListItemResponse>>();
}
