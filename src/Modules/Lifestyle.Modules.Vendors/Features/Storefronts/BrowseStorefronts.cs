using Lifestyle.Modules.Vendors.Domain;
using Lifestyle.Modules.Vendors.Features.Vendors;
using Lifestyle.Modules.Vendors.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Paging;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Vendors.Features.Storefronts;

/// <summary>
/// The marketplace's shop directory — every approved shop, for the "Shops" surface a buyer browses
/// before they know what they want.
/// <para>
/// Only approved vendors appear. A draft or rejected application is not a shop, and a suspended one
/// must read as gone rather than as broken, which is the same rule <see cref="GetStorefront"/>
/// applies to a single lookup.
/// </para>
/// <para>
/// Unlike the product browse, this is never scoped to the request host: a shop's own storefront has
/// no reason to list its competitors, and the marketplace is the only surface that calls it.
/// </para>
/// </summary>
internal static class BrowseStorefronts
{
    public sealed record Request(string? Search, int Page = 1, int PageSize = 24);

    internal sealed class Handler(IVendorsDbContext db)
        : IHandler<Request, Result<PagedResult<StorefrontResponse>>>
    {
        public async Task<Result<PagedResult<StorefrontResponse>>> Handle(Request request, CancellationToken ct)
        {
            var query = db.Vendors
                .AsNoTracking()
                .Where(v => v.Status == VendorStatus.Approved);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var term = $"%{request.Search.Trim().ToLowerInvariant()}%";
#pragma warning disable CA1304, CA1311 // Translated to SQL lower(); see VendorApprovalQueue for the full note.
                query = query.Where(v => EF.Functions.Like(v.DisplayName.ToLower(), term));
#pragma warning restore CA1304, CA1311
            }

            var page = new PageRequest { Page = request.Page, PageSize = Math.Min(request.PageSize, 100) };
            var total = await query.CountAsync(ct);

            // Alphabetical, with a stable tiebreak so paging cannot repeat or skip a shop when two
            // share a display name.
            var vendors = await query
                .OrderBy(v => v.DisplayName)
                .ThenBy(v => v.Id)
                .Skip(page.Skip)
                .Take(page.Size)
                .ToListAsync(ct);

            return PagedResult<StorefrontResponse>.From([.. vendors.Select(v => v.ToStorefront())], page, total);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/", async ([AsParameters] Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("BrowseStorefronts")
            .WithSummary("Public directory of approved shops.")
            .AllowAnonymous()
            .Produces<PagedResult<StorefrontResponse>>();
}
