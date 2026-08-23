using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Internal;

/// <summary>
/// Loads a product and proves the caller's vendor owns it. Every vendor-surface write goes through
/// this, so "can this caller touch this row" is answered in one place rather than repeated — and
/// forgotten — in each handler.
/// </summary>
internal sealed class VendorScope(ICatalogDbContext db, ICurrentUser currentUser)
{
    /// <summary>The vendor the caller acts for, taken from the token — never from the request.</summary>
    public Result<Guid> RequireVendorId() =>
        currentUser.VendorId is { } vendorId
            ? vendorId
            : Error.Forbidden("catalog.no_vendor_context", "This token is not scoped to a vendor.");

    public async Task<Result<Product>> LoadOwnedProductAsync(Guid productId, CancellationToken ct)
    {
        var vendorId = RequireVendorId();
        if (vendorId.IsFailure) return vendorId.Error;

        var product = await db.Products
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Include(p => p.AttributeValues)
            .FirstOrDefaultAsync(p => p.Id == productId, ct);

        if (product is null)
            return Error.NotFound("catalog.product_not_found");

        // Deliberately 404, not 403: telling a caller "that exists but is not yours" leaks which
        // product ids are real.
        if (product.VendorId != vendorId.Value)
            return Error.NotFound("catalog.product_not_found");

        return product;
    }
}
