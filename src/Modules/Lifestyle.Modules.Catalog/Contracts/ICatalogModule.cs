using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Catalog.Contracts;

/// <summary>
/// Catalog's public surface. Ordering will use <see cref="GetVariantSnapshotsAsync"/> at checkout
/// to price a cart from the server's numbers rather than the client's.
/// </summary>
public interface ICatalogModule
{
    Task<IReadOnlyList<VariantSnapshot>> GetVariantSnapshotsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct);

    Task<ProductSnapshot?> GetProductAsync(Guid productId, CancellationToken ct);

    /// <summary>Number of published products a vendor has. Used on shop profiles.</summary>
    Task<int> CountPublishedAsync(Guid vendorId, CancellationToken ct);
}

public sealed record VariantSnapshot(
    Guid VariantId,
    Guid ProductId,
    Guid VendorId,
    string ProductName,
    string Sku,
    IReadOnlyDictionary<string, string> Options,
    decimal Price,
    string Currency,
    int StockQuantity,
    bool IsAvailable);

public sealed record ProductSnapshot(
    Guid Id,
    Guid VendorId,
    string Name,
    string Slug,
    string Status,
    decimal MinPrice,
    decimal MaxPrice,
    string Currency);

/// <summary>Published when a product goes live. Search indexing and notifications listen for it.</summary>
public sealed record ProductPublishedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid ProductId,
    Guid VendorId,
    string Name,
    string Slug) : IIntegrationEvent;

public sealed record ProductUnpublishedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid ProductId,
    Guid VendorId,
    string Reason) : IIntegrationEvent;
