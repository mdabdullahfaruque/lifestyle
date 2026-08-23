using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.SharedKernel.Identifiers;
using Lifestyle.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Internal;

/// <summary>
/// Produces a slug that is unique within one vendor's catalogue. Shared by create and rename so
/// both behave identically.
/// </summary>
internal sealed class ProductSlugFactory(ICatalogDbContext db)
{
    public async Task<Result<string>> CreateAsync(Guid vendorId, string name, Guid? excludingProductId, CancellationToken ct)
    {
        var baseSlug = Slug.From(name);

        if (!Slug.IsValid(baseSlug))
            return Error.Validation("catalog.slug_invalid",
                "That product name cannot be turned into a web address. Add some letters or numbers.");

        for (var attempt = 1; attempt <= 100; attempt++)
        {
            var candidate = Slug.WithSuffix(baseSlug, attempt);

            var taken = await db.Products
                .IgnoreQueryFilters()
                .AnyAsync(p => p.VendorId == vendorId
                               && p.Slug == candidate
                               && (excludingProductId == null || p.Id != excludingProductId), ct);

            if (!taken) return candidate;
        }

        return Error.Conflict("catalog.slug_unavailable",
            "Too many products share this name. Give this one a more specific name.");
    }
}
