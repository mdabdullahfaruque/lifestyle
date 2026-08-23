using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lifestyle.Modules.Catalog.Internal;

/// <summary>
/// Reacts to a vendor being suspended by taking their live products down. This is the whole reason
/// integration events exist: Vendors must not reach into Catalog's tables, and Catalog must not be
/// asked to poll (docs/04 §3.6).
/// <para>
/// Delivery is at-least-once, so this is written to be safely repeatable — a product already
/// unpublished is skipped rather than treated as an error.
/// </para>
/// </summary>
internal sealed class UnpublishProductsOnVendorSuspended(
    ICatalogDbContext db,
    IClock clock,
    ILogger<UnpublishProductsOnVendorSuspended> logger)
    : IIntegrationEventHandler<VendorSuspendedEvent>
{
    public async Task Handle(VendorSuspendedEvent integrationEvent, CancellationToken ct)
    {
        var products = await db.Products
            .Include(p => p.Variants)
            .Where(p => p.VendorId == integrationEvent.VendorId && p.Status == ProductStatus.Published)
            .ToListAsync(ct);

        var count = products.Count;
        if (count == 0) return;

        foreach (var product in products)
            product.Unpublish($"vendor_suspended:{integrationEvent.Reason}", clock.UtcNow);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Unpublished {Count} products after vendor {VendorId} was suspended.",
            count, integrationEvent.VendorId);
    }
}
