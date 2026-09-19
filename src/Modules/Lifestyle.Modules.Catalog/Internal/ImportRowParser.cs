using System.Globalization;
using Lifestyle.SharedKernel.Results;

namespace Lifestyle.Modules.Catalog.Internal;

/// <summary>One sheet row understood as a variant, with its product's columns alongside.</summary>
internal sealed record ParsedRow(
    int RowNumber,
    string ProductCode,
    string? Name,
    string? CategorySlug,
    string? Brand,
    string? ShortDescription,
    string? Description,
    string Sku,
    decimal Price,
    decimal? CompareAtPrice,
    int Stock,
    decimal? WeightGrams,
    string? BarCode,
    Dictionary<string, string> Axes,
    Dictionary<string, string> Attributes);

/// <summary>
/// Turns one row of strings into a <see cref="ParsedRow"/>, or an error naming the column
/// (docs/08 §3.1).
/// <para>
/// Used twice on purpose: once at upload, to decide what the review grid shows, and again at
/// commit, over the same stored cells. Parsing in one place is what guarantees the grid's verdict
/// and the commit's behaviour cannot disagree.
/// </para>
/// </summary>
internal static class ImportRowParser
{
    public static Result<ParsedRow> Parse(ImportSchema schema, SheetRow row)
    {
        var code = row.Get(ImportColumns.ProductCode);
        if (string.IsNullOrWhiteSpace(code))
            return Error.Validation("import.product_code_missing", "product_code is required on every row.");

        if (code.Length > 64)
            return Error.Validation("import.product_code_too_long", "product_code must be 64 characters or fewer.");

        var sku = row.Get(ImportColumns.Sku);
        if (string.IsNullOrWhiteSpace(sku))
            return Error.Validation("import.sku_missing", "sku is required on every row.");

        if (sku.Length > 80)
            return Error.Validation("import.sku_too_long", "sku must be 80 characters or fewer.");

        var priceText = row.Get(ImportColumns.Price);
        if (string.IsNullOrWhiteSpace(priceText))
            return Error.Validation("import.price_missing", "price is required on every row.");

        if (!TryParseDecimal(priceText, out var price))
            return Error.Validation("import.price_invalid", $"'{priceText}' is not a number.");

        if (price <= 0)
            return Error.Validation("import.price_invalid", "price must be greater than zero.");

        decimal? compareAt = null;
        var compareText = row.Get(ImportColumns.CompareAtPrice);
        if (compareText is not null)
        {
            if (!TryParseDecimal(compareText, out var parsed))
                return Error.Validation("import.compare_at_price_invalid", $"'{compareText}' is not a number.");

            if (parsed <= price)
                return Error.Validation("import.compare_at_price_invalid",
                    "compare_at_price must be higher than price.");

            compareAt = parsed;
        }

        var stock = 0;
        var stockText = row.Get(ImportColumns.Stock);
        if (stockText is not null)
        {
            if (!int.TryParse(stockText, NumberStyles.Integer, CultureInfo.InvariantCulture, out stock))
                return Error.Validation("import.stock_invalid", $"'{stockText}' is not a whole number.");

            if (stock < 0)
                return Error.Validation("import.stock_invalid", "stock cannot be negative.");
        }

        decimal? weight = null;
        var weightText = row.Get(ImportColumns.WeightGrams);
        if (weightText is not null)
        {
            if (!TryParseDecimal(weightText, out var parsedWeight) || parsedWeight < 0)
                return Error.Validation("import.weight_invalid", $"'{weightText}' is not a valid weight.");

            weight = parsedWeight;
        }

        var axes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in schema.Columns)
        {
            if (column.Kind is not (ImportColumnKind.Axis or ImportColumnKind.Attribute)) continue;

            var value = row.Get(column.Name);

            if (value is null)
            {
                if (column.Required)
                    return Error.Validation("import.attribute_missing", $"{column.Name} is required.");

                continue;
            }

            // A Select attribute is the reason the template has dropdowns. A value outside the list
            // means the seller typed over one, and accepting it would put a colour in the catalogue
            // that no filter will ever match.
            if (column.HasDropdown
                && !column.AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return Error.Validation("import.attribute_not_allowed",
                    $"'{value}' is not a valid {column.Name}. Allowed: {string.Join(", ", column.AllowedValues)}.");
            }

            var code2 = StripPrefix(column.Name);

            if (column.Kind == ImportColumnKind.Axis) axes[code2] = value;
            else attributes[code2] = value;
        }

        return new ParsedRow(
            row.RowNumber,
            code,
            row.Get(ImportColumns.Name),
            row.Get(ImportColumns.CategorySlug),
            row.Get(ImportColumns.Brand),
            row.Get(ImportColumns.ShortDescription),
            row.Get(ImportColumns.Description),
            sku,
            price,
            compareAt,
            stock,
            weight,
            row.Get(ImportColumns.BarCode),
            axes,
            attributes);
    }

    /// <summary>
    /// Checks the rows of one product together — the things a single row cannot know.
    /// </summary>
    public static Result ValidateGroup(IReadOnlyList<ParsedRow> group)
    {
        var first = group[0];

        if (string.IsNullOrWhiteSpace(first.Name))
            return Error.Validation("import.name_missing",
                "The first row of each product must have a name.");

        if (first.Name.Length > 300)
            return Error.Validation("import.name_too_long", "name must be 300 characters or fewer.");

        if (first.Description is { Length: > 20000 })
            return Error.Validation("import.description_too_long", "description must be 20000 characters or fewer.");

        if (first.ShortDescription is { Length: > 500 })
            return Error.Validation("import.short_description_too_long",
                "short_description must be 500 characters or fewer.");

        if (first.Brand is { Length: > 150 })
            return Error.Validation("import.brand_too_long", "brand must be 150 characters or fewer.");

        // The same limits the single-product editor enforces (CreateProduct.Validator). Restating
        // the number here would be a second place to forget; they are checked again by the
        // aggregate when the commit builds the product.
        if (group.Count > 200)
            return Error.Validation("import.too_many_variants",
                $"A product may have at most 200 variants; this one has {group.Count}.");

        var duplicateSku = group
            .GroupBy(r => r.Sku, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateSku is not null)
            return Error.Validation("import.sku_duplicate",
                $"SKU '{duplicateSku.Key}' appears on more than one row of this product.");

        var duplicateAxes = group
            .GroupBy(r => ProductVariantSignature(r.Axes), StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        return duplicateAxes is not null
            ? Error.Validation("import.variant_duplicate",
                "Two rows of this product have the same combination of options.")
            : Result.Success();
    }

    public static string StripPrefix(string columnName) =>
        columnName.StartsWith(ImportColumns.AxisPrefix, StringComparison.OrdinalIgnoreCase)
            ? columnName[ImportColumns.AxisPrefix.Length..]
            : columnName.StartsWith(ImportColumns.AttributePrefix, StringComparison.OrdinalIgnoreCase)
                ? columnName[ImportColumns.AttributePrefix.Length..]
                : columnName;

    private static string ProductVariantSignature(Dictionary<string, string> axes) =>
        string.Join('|', axes
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key.ToLowerInvariant()}={kv.Value.Trim().ToLowerInvariant()}"));

    /// <summary>
    /// Parses a number written the way any of our markets writes it.
    /// <para>
    /// Italy is a target market and Italian Excel writes <c>1.234,56</c>, while Malaysia and
    /// Bangladesh write <c>1,234.56</c>. Guessing per-locale from the token is more reliable here
    /// than trusting a request header: the separator that appears last is the decimal one.
    /// </para>
    /// </summary>
    private static bool TryParseDecimal(string text, out decimal value)
    {
        var trimmed = text.Trim();

        var lastComma = trimmed.LastIndexOf(',');
        var lastDot = trimmed.LastIndexOf('.');

        var normalised = lastComma > lastDot
            ? trimmed.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
            : trimmed.Replace(",", string.Empty, StringComparison.Ordinal);

        return decimal.TryParse(normalised, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
