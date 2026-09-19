using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Internal;

/// <summary>
/// Which columns a bulk-import sheet has for a given category, and what each one accepts.
/// <para>
/// One description of the contract, used by both the template writer and the parser, so a column
/// cannot be offered in the template and then rejected on upload (docs/08 §3.1).
/// </para>
/// </summary>
internal sealed record ImportSchema(
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    IReadOnlyList<ImportColumn> Columns)
{
    public ImportColumn? Find(string header) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, header, StringComparison.OrdinalIgnoreCase));
}

internal sealed record ImportColumn(
    string Name,
    ImportColumnKind Kind,
    bool Required,
    string Help,
    IReadOnlyList<string> AllowedValues)
{
    public bool HasDropdown => AllowedValues.Count > 0;
}

internal enum ImportColumnKind
{
    /// <summary>A column on the product: written once, read from the first row of the group.</summary>
    Product = 1,

    /// <summary>A column on the variant: read from every row.</summary>
    Variant = 2,

    /// <summary>A variant axis, <c>axis:colour</c>. Values multiply out into SKUs.</summary>
    Axis = 3,

    /// <summary>A product-level attribute, <c>attr:material</c>.</summary>
    Attribute = 4
}

/// <summary>The fixed column names. Constants so the writer and the parser cannot drift apart.</summary>
internal static class ImportColumns
{
    public const string ProductCode = "product_code";
    public const string Name = "name";
    public const string CategorySlug = "category_slug";
    public const string Brand = "brand";
    public const string ShortDescription = "short_description";
    public const string Description = "description";
    public const string Sku = "sku";
    public const string Price = "price";
    public const string CompareAtPrice = "compare_at_price";
    public const string Stock = "stock";
    public const string WeightGrams = "weight_grams";
    public const string BarCode = "barcode";

    public const string AxisPrefix = "axis:";
    public const string AttributePrefix = "attr:";
}

/// <summary>
/// Builds the schema for one category from its attribute set. The attribute set already records
/// which attributes are variant axes, which are required and what values a Select accepts
/// (docs/08 §3), so the template is generated rather than maintained by hand.
/// </summary>
internal sealed class ImportSchemaFactory(ICatalogDbContext db)
{
    public async Task<Result<ImportSchema>> ForCategoryAsync(Guid categoryId, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == categoryId, ct);

        if (category is null)
            return Error.NotFound("catalog.category_not_found");

        if (!category.IsLeaf)
            return Error.Validation("catalog.category_not_leaf",
                "Products belong to a leaf category. Pick one of its subcategories.");

        var attributes = category.AttributeSetId is { } setId
            ? await db.ProductAttributes.AsNoTracking()
                .Where(a => a.AttributeSetId == setId)
                .OrderBy(a => a.SortOrder)
                .ToListAsync(ct)
            : [];

        var columns = new List<ImportColumn>
        {
            Core(ImportColumns.ProductCode, ImportColumnKind.Product, required: true,
                "Your own code for this product. Repeat it on every row of the same product."),
            Core(ImportColumns.Name, ImportColumnKind.Product, required: true, "Product title, up to 300 characters."),
            Core(ImportColumns.CategorySlug, ImportColumnKind.Product, required: true,
                $"Leave as '{category.Slug}' unless the product belongs elsewhere."),
            Core(ImportColumns.Brand, ImportColumnKind.Product, required: false, "Optional."),
            Core(ImportColumns.ShortDescription, ImportColumnKind.Product, required: false, "Up to 500 characters."),
            Core(ImportColumns.Description, ImportColumnKind.Product, required: false,
                "Required before the product can be sent for review."),
            Core(ImportColumns.Sku, ImportColumnKind.Variant, required: true,
                "One row per sellable variant. Unique within the product."),
            Core(ImportColumns.Price, ImportColumnKind.Variant, required: true, "Selling price, greater than zero."),
            Core(ImportColumns.CompareAtPrice, ImportColumnKind.Variant, required: false,
                "The struck-through 'was' price. Must be higher than the price."),
            Core(ImportColumns.Stock, ImportColumnKind.Variant, required: false, "Whole number. Defaults to 0."),
            Core(ImportColumns.WeightGrams, ImportColumnKind.Variant, required: false, "Used for shipping rates."),
            Core(ImportColumns.BarCode, ImportColumnKind.Variant, required: false, "Optional.")
        };

        foreach (var attribute in attributes)
        {
            var isAxis = attribute.IsVariantAxis;

            columns.Add(new ImportColumn(
                (isAxis ? ImportColumns.AxisPrefix : ImportColumns.AttributePrefix) + attribute.Code,
                isAxis ? ImportColumnKind.Axis : ImportColumnKind.Attribute,
                attribute.IsRequired,
                HelpFor(attribute, isAxis),
                attribute.DataType == AttributeDataType.Select ? [.. attribute.AllowedValues] : []));
        }

        return new ImportSchema(category.Id, category.Name, category.Slug, columns);
    }

    private static ImportColumn Core(string name, ImportColumnKind kind, bool required, string help) =>
        new(name, kind, required, help, []);

    private static string HelpFor(ProductAttribute attribute, bool isAxis)
    {
        var what = isAxis
            ? "Varies by variant — a different value on each row makes a different SKU."
            : "Same for the whole product.";

        var type = attribute.DataType switch
        {
            AttributeDataType.Select => "Pick from the list.",
            AttributeDataType.Number => attribute.Unit is { } unit ? $"A number in {unit}." : "A number.",
            AttributeDataType.Boolean => "true or false.",
            _ => "Free text."
        };

        return $"{attribute.Name}. {what} {type}";
    }
}
