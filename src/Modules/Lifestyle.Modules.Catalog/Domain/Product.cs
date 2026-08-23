using Lifestyle.Modules.Catalog.Domain.Events;
using Lifestyle.SharedKernel.Domain;
using Lifestyle.SharedKernel.Results;

namespace Lifestyle.Modules.Catalog.Domain;

/// <summary>
/// A sellable product belonging to one vendor. The aggregate root for its variants, images and
/// attribute values — nothing below it is loaded or saved on its own.
/// </summary>
internal sealed class Product : AggregateRoot, ISoftDeletable
{
    private readonly List<ProductVariant> _variants = [];
    private readonly List<ProductImage> _images = [];
    private readonly List<ProductAttributeValue> _attributeValues = [];

    private Product() { }

    public Guid VendorId { get; private set; }
    public Guid CategoryId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Unique per vendor, not globally — two shops may both sell a "Red Silk Scarf".</summary>
    public string Slug { get; private set; } = null!;

    public string? Description { get; private set; }
    public string? ShortDescription { get; private set; }
    public string? Brand { get; private set; }

    public ProductStatus Status { get; private set; } = ProductStatus.Draft;
    public string? ModerationNote { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Denormalised from the variants so listing queries never touch the variant table.</summary>
    public decimal MinPrice { get; private set; }
    public decimal MaxPrice { get; private set; }
    public string Currency { get; private set; } = null!;
    public int TotalStock { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();
    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();
    public IReadOnlyCollection<ProductAttributeValue> AttributeValues => _attributeValues.AsReadOnly();

    public bool IsVisible => Status == ProductStatus.Published;

    public static Product Create(
        Guid vendorId, Guid categoryId, string name, string slug,
        string? description, string? shortDescription, string? brand,
        string currency, DateTimeOffset now) => new()
        {
            VendorId = vendorId,
            CategoryId = categoryId,
            Name = name.Trim(),
            Slug = slug,
            Description = description?.Trim(),
            ShortDescription = shortDescription?.Trim(),
            Brand = string.IsNullOrWhiteSpace(brand) ? null : brand.Trim(),
            Currency = currency,
            Status = ProductStatus.Draft,
            CreatedAt = now
        };

    public void UpdateDetails(
        Guid categoryId, string name, string? description, string? shortDescription,
        string? brand, DateTimeOffset now)
    {
        CategoryId = categoryId;
        Name = name.Trim();
        Description = description?.Trim();
        ShortDescription = shortDescription?.Trim();
        Brand = string.IsNullOrWhiteSpace(brand) ? null : brand.Trim();
        UpdatedAt = now;

        // A published product that changes materially goes back for review. Otherwise "publish a
        // bland product, then edit it into anything" defeats moderation entirely.
        if (Status == ProductStatus.Published)
        {
            Status = ProductStatus.PendingReview;
            PublishedAt = null;
            SubmittedAt = now;
            Raise(new ProductUnpublished(Id, VendorId, "edited_after_publish", now));
        }
    }

    // ── Variants ──

    public Result AddVariant(string sku, IReadOnlyDictionary<string, string> axisValues, decimal price, decimal? compareAtPrice, int stock, DateTimeOffset now)
    {
        if (_variants.Any(v => string.Equals(v.Sku, sku, StringComparison.OrdinalIgnoreCase)))
            return Error.Conflict("catalog.sku_duplicate", $"SKU '{sku}' is already used on this product.");

        var signature = ProductVariant.BuildSignature(axisValues);
        if (_variants.Any(v => v.AxisSignature == signature))
            return Error.Conflict("catalog.variant_duplicate",
                "A variant with that combination of options already exists.");

        if (price <= 0)
            return Error.Validation("catalog.price_invalid", "Price must be greater than zero.");

        if (compareAtPrice is { } compare && compare <= price)
            return Error.Validation("catalog.compare_price_invalid",
                "The compare-at price must be higher than the selling price.");

        _variants.Add(ProductVariant.Create(Id, sku, axisValues, price, compareAtPrice, stock, now));
        RecalculateAggregates(now);
        return Result.Success();
    }

    public Result UpdateVariant(Guid variantId, decimal price, decimal? compareAtPrice, int stock, bool isActive, DateTimeOffset now)
    {
        var variant = _variants.FirstOrDefault(v => v.Id == variantId);
        if (variant is null) return Error.NotFound("catalog.variant_not_found");

        if (price <= 0)
            return Error.Validation("catalog.price_invalid", "Price must be greater than zero.");

        if (compareAtPrice is { } compare && compare <= price)
            return Error.Validation("catalog.compare_price_invalid",
                "The compare-at price must be higher than the selling price.");

        variant.Update(price, compareAtPrice, stock, isActive, now);
        RecalculateAggregates(now);
        return Result.Success();
    }

    public Result RemoveVariant(Guid variantId, DateTimeOffset now)
    {
        var variant = _variants.FirstOrDefault(v => v.Id == variantId);
        if (variant is null) return Error.NotFound("catalog.variant_not_found");

        if (_variants.Count == 1)
            return Error.Validation("catalog.last_variant", "A product must keep at least one variant.");

        _variants.Remove(variant);
        RecalculateAggregates(now);
        return Result.Success();
    }

    /// <summary>
    /// Adjusts stock for one variant. Positive to receive, negative to consume. v1 stock is
    /// vendor-maintained and advisory (Plan §6.2), so this never goes below zero silently.
    /// </summary>
    public Result AdjustStock(Guid variantId, int delta, DateTimeOffset now)
    {
        var variant = _variants.FirstOrDefault(v => v.Id == variantId);
        if (variant is null) return Error.NotFound("catalog.variant_not_found");

        var result = variant.AdjustStock(delta, now);
        if (result.IsFailure) return result;

        RecalculateAggregates(now);
        return Result.Success();
    }

    // ── Images ──

    public void AddImage(string mediaId, string? altText, DateTimeOffset now)
    {
        if (_images.Any(i => i.MediaId == mediaId)) return;

        var position = _images.Count == 0 ? 0 : _images.Max(i => i.Position) + 1;
        _images.Add(ProductImage.Create(Id, mediaId, altText, position));
        UpdatedAt = now;
    }

    public void RemoveImage(string mediaId, DateTimeOffset now)
    {
        _images.RemoveAll(i => i.MediaId == mediaId);
        Reindex();
        UpdatedAt = now;
    }

    /// <summary>Reorders images to match the given media ids. Unknown ids are ignored.</summary>
    public void ReorderImages(IReadOnlyList<string> mediaIdsInOrder, DateTimeOffset now)
    {
        for (var i = 0; i < mediaIdsInOrder.Count; i++)
        {
            var image = _images.FirstOrDefault(x => x.MediaId == mediaIdsInOrder[i]);
            image?.SetPosition(i);
        }

        Reindex();
        UpdatedAt = now;
    }

    private void Reindex()
    {
        var ordered = _images.OrderBy(i => i.Position).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].SetPosition(i);
    }

    // ── Attribute values ──

    public void SetAttributeValue(Guid attributeId, string code, string? value)
    {
        var existing = _attributeValues.FirstOrDefault(a => a.AttributeId == attributeId);

        if (string.IsNullOrWhiteSpace(value))
        {
            if (existing is not null) _attributeValues.Remove(existing);
            return;
        }

        if (existing is null)
            _attributeValues.Add(ProductAttributeValue.Create(Id, attributeId, code, value));
        else
            existing.SetValue(value);
    }

    // ── Lifecycle (FRD §7.5) ──

    /// <summary>
    /// Draft → PendingReview. The completeness rules live here rather than in a validator because
    /// they are invariants of "a product fit to be seen", not of one request shape.
    /// </summary>
    public Result SubmitForReview(DateTimeOffset now)
    {
        if (Status is not (ProductStatus.Draft or ProductStatus.Rejected))
            return Error.Conflict("catalog.not_submittable", $"A product in {Status} state cannot be submitted.");

        if (_variants.Count == 0)
            return Error.Validation("catalog.no_variants", "Add at least one variant before submitting.");

        if (_images.Count == 0)
            return Error.Validation("catalog.no_images", "Add at least one image before submitting.");

        if (string.IsNullOrWhiteSpace(Description))
            return Error.Validation("catalog.no_description", "Add a description before submitting.");

        Status = ProductStatus.PendingReview;
        ModerationNote = null;
        SubmittedAt = now;
        UpdatedAt = now;

        Raise(new ProductSubmittedForReview(Id, VendorId, Name, now));
        return Result.Success();
    }

    public Result Approve(DateTimeOffset now)
    {
        if (Status != ProductStatus.PendingReview)
            return Error.Conflict("catalog.not_pending", "Only a product awaiting review can be approved.");

        Status = ProductStatus.Published;
        ModerationNote = null;
        PublishedAt = now;
        UpdatedAt = now;

        Raise(new ProductPublished(Id, VendorId, Name, Slug, now));
        return Result.Success();
    }

    public Result Reject(string note, DateTimeOffset now)
    {
        if (Status != ProductStatus.PendingReview)
            return Error.Conflict("catalog.not_pending", "Only a product awaiting review can be rejected.");

        Status = ProductStatus.Rejected;
        ModerationNote = note;
        UpdatedAt = now;

        Raise(new ProductRejected(Id, VendorId, note, now));
        return Result.Success();
    }

    /// <summary>
    /// Takes a live product down. Called by the vendor, by a moderator, and by the handler that
    /// reacts to vendor suspension.
    /// </summary>
    public Result Unpublish(string reason, DateTimeOffset now)
    {
        if (Status != ProductStatus.Published)
            return Error.Conflict("catalog.not_published", "Only a published product can be unpublished.");

        Status = ProductStatus.Unpublished;
        ModerationNote = reason;
        PublishedAt = null;
        UpdatedAt = now;

        Raise(new ProductUnpublished(Id, VendorId, reason, now));
        return Result.Success();
    }

    public Result Republish(DateTimeOffset now)
    {
        if (Status != ProductStatus.Unpublished)
            return Error.Conflict("catalog.not_unpublished", "Only an unpublished product can be restored.");

        // Back through review: whatever caused the takedown must be confirmed as resolved.
        Status = ProductStatus.PendingReview;
        SubmittedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    private void RecalculateAggregates(DateTimeOffset now)
    {
        var active = _variants.Where(v => v.IsActive).ToList();

        MinPrice = active.Count == 0 ? 0 : active.Min(v => v.Price);
        MaxPrice = active.Count == 0 ? 0 : active.Max(v => v.Price);
        TotalStock = active.Sum(v => v.StockQuantity);
        UpdatedAt = now;
    }
}

internal enum ProductStatus
{
    Draft = 1,
    PendingReview = 2,
    Published = 3,
    Rejected = 4,

    /// <summary>Was live, taken down. Distinct from Draft so history reads correctly.</summary>
    Unpublished = 5
}
