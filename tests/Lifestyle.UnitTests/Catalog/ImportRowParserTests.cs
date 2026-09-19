using Lifestyle.Modules.Catalog.Internal;
using Shouldly;
using Xunit;

namespace Lifestyle.UnitTests.Catalog;

/// <summary>
/// The parser decides what the review grid says and what the commit writes — it runs twice over
/// the same cells, so a disagreement between those two is impossible by construction (docs/08 §5.3).
/// </summary>
public sealed class ImportRowParserTests
{
    private static ImportSchema Schema() => new(
        Guid.CreateVersion7(), "Dresses", "dresses",
        [
            new ImportColumn(ImportColumns.ProductCode, ImportColumnKind.Product, true, "", []),
            new ImportColumn(ImportColumns.Name, ImportColumnKind.Product, true, "", []),
            new ImportColumn(ImportColumns.Description, ImportColumnKind.Product, false, "", []),
            new ImportColumn(ImportColumns.Sku, ImportColumnKind.Variant, true, "", []),
            new ImportColumn(ImportColumns.Price, ImportColumnKind.Variant, true, "", []),
            new ImportColumn(ImportColumns.CompareAtPrice, ImportColumnKind.Variant, false, "", []),
            new ImportColumn(ImportColumns.Stock, ImportColumnKind.Variant, false, "", []),
            new ImportColumn($"{ImportColumns.AxisPrefix}colour", ImportColumnKind.Axis, true, "",
                ["Red", "Blue"]),
            new ImportColumn($"{ImportColumns.AttributePrefix}material", ImportColumnKind.Attribute, false, "", [])
        ]);

    private static SheetRow Row(params (string Key, string Value)[] cells) =>
        new(2, cells.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase));

    private static SheetRow ValidRow() => Row(
        (ImportColumns.ProductCode, "LS-1001"),
        (ImportColumns.Name, "Silk Scarf"),
        (ImportColumns.Sku, "LS-1001-RED"),
        (ImportColumns.Price, "49.90"),
        ($"{ImportColumns.AxisPrefix}colour", "Red"));

    [Fact]
    public void A_complete_row_parses_into_a_variant()
    {
        var result = ImportRowParser.Parse(Schema(), ValidRow());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ProductCode.ShouldBe("LS-1001");
        result.Value.Price.ShouldBe(49.90m);
        result.Value.Axes["colour"].ShouldBe("Red");
        result.Value.Stock.ShouldBe(0);
    }

    [Fact]
    public void A_value_outside_the_dropdown_is_refused()
    {
        var row = Row(
            (ImportColumns.ProductCode, "LS-1001"),
            (ImportColumns.Sku, "X"),
            (ImportColumns.Price, "10"),
            ($"{ImportColumns.AxisPrefix}colour", "Turquoise"));

        var result = ImportRowParser.Parse(Schema(), row);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("import.attribute_not_allowed");
    }

    [Fact]
    public void A_missing_required_axis_is_refused()
    {
        var row = Row(
            (ImportColumns.ProductCode, "LS-1001"),
            (ImportColumns.Sku, "X"),
            (ImportColumns.Price, "10"));

        ImportRowParser.Parse(Schema(), row).Error.Code.ShouldBe("import.attribute_missing");
    }

    [Fact]
    public void A_compare_at_price_below_the_price_is_refused()
    {
        var row = Row(
            (ImportColumns.ProductCode, "LS-1001"),
            (ImportColumns.Sku, "X"),
            (ImportColumns.Price, "50"),
            (ImportColumns.CompareAtPrice, "40"),
            ($"{ImportColumns.AxisPrefix}colour", "Red"));

        ImportRowParser.Parse(Schema(), row).Error.Code.ShouldBe("import.compare_at_price_invalid");
    }

    /// <summary>
    /// Italy is a target market and Italian Excel writes 1.234,56 while Malaysia writes 1,234.56.
    /// Getting this wrong prices a dress at 123456.
    /// </summary>
    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("49", 49)]
    public void Prices_parse_in_every_market_notation(string text, decimal expected)
    {
        var row = Row(
            (ImportColumns.ProductCode, "LS-1001"),
            (ImportColumns.Sku, "X"),
            (ImportColumns.Price, text),
            ($"{ImportColumns.AxisPrefix}colour", "Red"));

        ImportRowParser.Parse(Schema(), row).Value.Price.ShouldBe(expected);
    }

    [Fact]
    public void A_group_whose_first_row_has_no_name_is_refused()
    {
        var parsed = ImportRowParser.Parse(Schema(), Row(
            (ImportColumns.ProductCode, "LS-1001"),
            (ImportColumns.Sku, "X"),
            (ImportColumns.Price, "10"),
            ($"{ImportColumns.AxisPrefix}colour", "Red"))).Value;

        ImportRowParser.ValidateGroup([parsed]).Error.Code.ShouldBe("import.name_missing");
    }

    [Fact]
    public void Two_rows_with_the_same_sku_are_refused()
    {
        var first = ImportRowParser.Parse(Schema(), ValidRow()).Value;
        var second = first with { RowNumber = 3 };

        ImportRowParser.ValidateGroup([first, second]).Error.Code.ShouldBe("import.sku_duplicate");
    }

    [Fact]
    public void Two_rows_with_the_same_options_are_refused()
    {
        var first = ImportRowParser.Parse(Schema(), ValidRow()).Value;
        var second = first with { RowNumber = 3, Sku = "LS-1001-RED-2" };

        ImportRowParser.ValidateGroup([first, second]).Error.Code.ShouldBe("import.variant_duplicate");
    }

    [Fact]
    public void A_valid_group_passes()
    {
        var first = ImportRowParser.Parse(Schema(), ValidRow()).Value;

        var second = ImportRowParser.Parse(Schema(), Row(
            (ImportColumns.ProductCode, "LS-1001"),
            (ImportColumns.Sku, "LS-1001-BLUE"),
            (ImportColumns.Price, "49.90"),
            ($"{ImportColumns.AxisPrefix}colour", "Blue"))).Value;

        ImportRowParser.ValidateGroup([first, second]).IsSuccess.ShouldBeTrue();
    }
}
