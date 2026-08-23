using Lifestyle.Modules.Catalog.Contracts;
using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Internal;

internal sealed class CatalogFacade(ICatalogDbContext db) : ICatalogModule
{
    public async Task<IReadOnlyList<VariantSnapshot>> GetVariantSnapshotsAsync(
        IReadOnlyCollection<Guid> variantIds, CancellationToken ct)
    {
        if (variantIds.Count == 0) return [];

        // Joined in-module only. Checkout will price from these numbers, never from the client's.
        var rows = await db.ProductVariants
            .AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Join(db.Products.AsNoTracking(), v => v.ProductId, p => p.Id, (v, p) => new { v, p })
            .Select(x => new
            {
                x.v.Id,
                x.v.ProductId,
                x.p.VendorId,
                ProductName = x.p.Name,
                x.v.Sku,
                x.v.AxisValues,
                x.v.Price,
                x.p.Currency,
                x.v.StockQuantity,
                x.v.IsActive,
                ProductStatus = x.p.Status
            })
            .ToListAsync(ct);

        return [.. rows.Select(r => new VariantSnapshot(
            r.Id, r.ProductId, r.VendorId, r.ProductName, r.Sku, r.AxisValues, r.Price, r.Currency,
            r.StockQuantity,
            r.IsActive && r.ProductStatus == Domain.ProductStatus.Published && r.StockQuantity > 0))];
    }

    public async Task<ProductSnapshot?> GetProductAsync(Guid productId, CancellationToken ct) =>
        await db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new ProductSnapshot(
                p.Id, p.VendorId, p.Name, p.Slug, p.Status.ToString(), p.MinPrice, p.MaxPrice, p.Currency))
            .FirstOrDefaultAsync(ct);

    public Task<int> CountPublishedAsync(Guid vendorId, CancellationToken ct) =>
        db.Products.CountAsync(p => p.VendorId == vendorId && p.Status == Domain.ProductStatus.Published, ct);
}
