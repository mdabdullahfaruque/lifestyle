using Lifestyle.Modules.Catalog.Domain;

namespace Lifestyle.Modules.Catalog.Features.Products;

/// <summary>
/// Money crosses the wire as a decimal string, never a float (FRD §19.1). These records are the
/// only place product shapes are defined, so a new field has one place to be added.
/// </summary>
public sealed record ProductResponse(
    Guid Id,
    Guid VendorId,
    Guid CategoryId,
    string Name,
    string Slug,
    string? Description,
    string? ShortDescription,
    string? Brand,
    string Status,
    string? ModerationNote,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? PublishedAt,
    MoneyRange Price,
    int TotalStock,
    IReadOnlyList<VariantResponse> Variants,
    IReadOnlyList<ProductImageResponse> Images,
    IReadOnlyDictionary<string, string> Attributes,
    /// <summary>
    /// The shop that sells this product. Populated on the public product endpoint only — a buyer
    /// needs the shop's name and WhatsApp number to place an order, and without it the client would
    /// have to look the vendor up by a slug the product response does not carry. Null on the
    /// vendor's own views, where the caller already knows which shop they are.
    /// </summary>
    ProductShopResponse? Shop = null);

/// <summary>The seller, as a buyer needs to see them on a product page.</summary>
public sealed record ProductShopResponse(
    Guid Id,
    string DisplayName,
    string Slug,
    string? WhatsAppNumber,
    string? AccentColour,
    string? LogoMediaId);

public sealed record MoneyRange(string Min, string Max, string Currency);

public sealed record VariantResponse(
    Guid Id,
    string Sku,
    IReadOnlyDictionary<string, string> Options,
    string Price,
    string? CompareAtPrice,
    string Currency,
    int StockQuantity,
    bool IsActive,
    decimal? WeightGrams,
    string? BarCode);

public sealed record ProductImageResponse(string MediaId, string? AltText, int Position);

/// <summary>The narrow shape used in grids and search results.</summary>
public sealed record ProductListItemResponse(
    Guid Id,
    Guid VendorId,
    string Name,
    string Slug,
    string Status,
    MoneyRange Price,
    int TotalStock,
    string? PrimaryImageMediaId,
    DateTimeOffset? PublishedAt);

internal static class ProductMapping
{
    public static ProductResponse ToResponse(this Product p) => new(
        p.Id, p.VendorId, p.CategoryId, p.Name, p.Slug, p.Description, p.ShortDescription, p.Brand,
        p.Status.ToString(), p.ModerationNote, p.SubmittedAt, p.PublishedAt,
        new MoneyRange(Format(p.MinPrice), Format(p.MaxPrice), p.Currency),
        p.TotalStock,
        [.. p.Variants.OrderBy(v => v.Sku, StringComparer.Ordinal).Select(v => v.ToResponse(p.Currency))],
        [.. p.Images.OrderBy(i => i.Position).Select(i => new ProductImageResponse(i.MediaId, i.AltText, i.Position))],
        p.AttributeValues.ToDictionary(a => a.Code, a => a.Value, StringComparer.Ordinal));

    public static VariantResponse ToResponse(this ProductVariant v, string currency) => new(
        v.Id, v.Sku, v.AxisValues, Format(v.Price),
        v.CompareAtPrice is { } c ? Format(c) : null,
        currency, v.StockQuantity, v.IsActive, v.WeightGrams, v.BarCode);

    /// <summary>Two decimals, invariant culture — the wire format never depends on server locale.</summary>
    public static string Format(decimal amount) =>
        amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The SQL-projectable shape of a product list row: raw columns only, no formatting calls that EF
/// cannot translate. Mapped to <see cref="ProductListItemResponse"/> after materialisation.
/// </summary>
internal sealed record ProductListRow(
    Guid Id,
    Guid VendorId,
    string Name,
    string Slug,
    ProductStatus Status,
    decimal MinPrice,
    decimal MaxPrice,
    string Currency,
    int TotalStock,
    string? PrimaryImageMediaId,
    DateTimeOffset? PublishedAt)
{
    public ProductListItemResponse ToResponse() => new(
        Id, VendorId, Name, Slug, Status.ToString(),
        new MoneyRange(ProductMapping.Format(MinPrice), ProductMapping.Format(MaxPrice), Currency),
        TotalStock, PrimaryImageMediaId, PublishedAt);
}
