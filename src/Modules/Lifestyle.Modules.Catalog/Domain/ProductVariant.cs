using System.Globalization;
using Lifestyle.SharedKernel.Domain;
using Lifestyle.SharedKernel.Results;

namespace Lifestyle.Modules.Catalog.Domain;

/// <summary>
/// One purchasable SKU: a specific combination of the category's variant axes (FRD §7.3).
/// "Red / M" is a variant; the product is the thing you browse to.
/// </summary>
internal sealed class ProductVariant : Entity
{
    private ProductVariant() { }

    public Guid ProductId { get; private set; }

    /// <summary>Vendor-supplied stock keeping unit. Unique within the product.</summary>
    public string Sku { get; private set; } = null!;

    /// <summary>
    /// The axis values that define this variant, e.g. <c>{"colour":"Red","size":"M"}</c>.
    /// Stored as jsonb: read whole, never filtered on directly.
    /// </summary>
    public Dictionary<string, string> AxisValues { get; private set; } = [];

    /// <summary>
    /// A canonical, order-independent rendering of <see cref="AxisValues"/>, used to enforce
    /// uniqueness. Comparing dictionaries in SQL is not practical; comparing this string is.
    /// </summary>
    public string AxisSignature { get; private set; } = string.Empty;

    public decimal Price { get; private set; }

    /// <summary>The "was" price shown struck through. Must exceed <see cref="Price"/> or be null.</summary>
    public decimal? CompareAtPrice { get; private set; }

    public int StockQuantity { get; private set; }
    public bool IsActive { get; private set; } = true;

    public decimal? WeightGrams { get; private set; }
    public string? BarCode { get; private set; }

    public static ProductVariant Create(
        Guid productId, string sku, IReadOnlyDictionary<string, string> axisValues,
        decimal price, decimal? compareAtPrice, int stock, DateTimeOffset now) => new()
        {
            ProductId = productId,
            Sku = sku.Trim(),
            AxisValues = axisValues.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            AxisSignature = BuildSignature(axisValues),
            Price = price,
            CompareAtPrice = compareAtPrice,
            StockQuantity = Math.Max(0, stock),
            IsActive = true
        };

    /// <summary>
    /// Lower-cased, key-sorted <c>key=value</c> pairs. Sorting is what makes
    /// {colour:Red,size:M} and {size:M,colour:Red} the same variant, which they are.
    /// </summary>
    public static string BuildSignature(IReadOnlyDictionary<string, string> axisValues) =>
        string.Join('|', axisValues
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => string.Create(CultureInfo.InvariantCulture,
                $"{kv.Key.ToLowerInvariant()}={kv.Value.Trim().ToLowerInvariant()}")));

    public void Update(decimal price, decimal? compareAtPrice, int stock, bool isActive, DateTimeOffset now)
    {
        Price = price;
        CompareAtPrice = compareAtPrice;
        StockQuantity = Math.Max(0, stock);
        IsActive = isActive;
    }

    public Result AdjustStock(int delta, DateTimeOffset now)
    {
        var updated = StockQuantity + delta;

        if (updated < 0)
            return Error.Validation("catalog.stock_negative",
                $"That would take stock below zero (currently {StockQuantity}).");

        StockQuantity = updated;
        return Result.Success();
    }

    public void SetLogistics(decimal? weightGrams, string? barCode)
    {
        WeightGrams = weightGrams;
        BarCode = string.IsNullOrWhiteSpace(barCode) ? null : barCode.Trim();
    }
}

/// <summary>An image attached to a product, in display order. Position 0 is the primary image.</summary>
internal sealed class ProductImage : Entity
{
    private ProductImage() { }

    public Guid ProductId { get; private set; }
    public string MediaId { get; private set; } = null!;
    public string? AltText { get; private set; }
    public int Position { get; private set; }

    public static ProductImage Create(Guid productId, string mediaId, string? altText, int position) => new()
    {
        ProductId = productId,
        MediaId = mediaId,
        AltText = altText?.Trim(),
        Position = position
    };

    public void SetPosition(int position) => Position = position;
}

/// <summary>A product-level attribute value — one that does not vary by SKU.</summary>
internal sealed class ProductAttributeValue : Entity
{
    private ProductAttributeValue() { }

    public Guid ProductId { get; private set; }
    public Guid AttributeId { get; private set; }

    /// <summary>Denormalised attribute code, so reads do not need to join the definition.</summary>
    public string Code { get; private set; } = null!;

    public string Value { get; private set; } = null!;

    public static ProductAttributeValue Create(Guid productId, Guid attributeId, string code, string value) => new()
    {
        ProductId = productId,
        AttributeId = attributeId,
        Code = code.ToLowerInvariant(),
        Value = value.Trim()
    };

    public void SetValue(string value) => Value = value.Trim();
}
