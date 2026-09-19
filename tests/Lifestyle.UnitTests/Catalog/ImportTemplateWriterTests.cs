using System.Text;
using ClosedXML.Excel;
using Lifestyle.Modules.Catalog.Internal;
using Shouldly;
using Xunit;

namespace Lifestyle.UnitTests.Catalog;

/// <summary>
/// The import template is generated, not shipped as a fixture, so nothing but a test opening the
/// produced file proves the dropdowns and headers actually survived (docs/08 §3).
/// </summary>
public sealed class ImportTemplateWriterTests
{
    private static ImportSchema Schema() => new(
        Guid.CreateVersion7(),
        "Women's Dresses",
        "womens-dresses",
        [
            new ImportColumn(ImportColumns.ProductCode, ImportColumnKind.Product, true, "Your code.", []),
            new ImportColumn(ImportColumns.Name, ImportColumnKind.Product, true, "Title.", []),
            new ImportColumn(ImportColumns.Sku, ImportColumnKind.Variant, true, "One per variant.", []),
            new ImportColumn(ImportColumns.Price, ImportColumnKind.Variant, true, "Above zero.", []),
            new ImportColumn($"{ImportColumns.AxisPrefix}colour", ImportColumnKind.Axis, true, "Colour.",
                ["Red", "Green", "Blue"]),
            new ImportColumn($"{ImportColumns.AttributePrefix}material", ImportColumnKind.Attribute, false,
                "Material.", ["Cotton", "Silk"])
        ]);

    [Fact]
    public void Xlsx_header_row_is_the_column_contract_in_order()
    {
        var bytes = ImportTemplateWriter.ToXlsx(Schema());

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Products");

        var headers = Enumerable.Range(1, 6).Select(c => sheet.Cell(1, c).GetString()).ToList();

        headers.ShouldBe([
            ImportColumns.ProductCode, ImportColumns.Name, ImportColumns.Sku,
            ImportColumns.Price, "axis:colour", "attr:material"
        ]);
    }

    [Fact]
    public void Select_columns_get_a_dropdown_and_plain_columns_do_not()
    {
        var bytes = ImportTemplateWriter.ToXlsx(Schema());

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Products");

        // Column 5 is axis:colour, which has allowed values; column 2 is a free-text name.
        sheet.Cell(2, 5).HasDataValidation.ShouldBeTrue("a Select attribute must be a picker, not free text");
        sheet.Cell(2, 2).HasDataValidation.ShouldBeFalse();
    }

    [Fact]
    public void Codes_are_text_formatted_so_a_leading_zero_survives()
    {
        var bytes = ImportTemplateWriter.ToXlsx(Schema());

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Products");

        sheet.Column(1).Style.NumberFormat.Format.ShouldBe("@");
    }

    [Fact]
    public void Instructions_sheet_lists_every_column()
    {
        var bytes = ImportTemplateWriter.ToXlsx(Schema());

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        workbook.Worksheets.Any(w => w.Name == "Instructions").ShouldBeTrue();

        var text = workbook.Worksheet("Instructions").RangeUsed()!.Cells()
            .Select(c => c.GetString())
            .ToList();

        text.ShouldContain("axis:colour");
        text.ShouldContain(ImportColumns.ProductCode);
    }

    /// <summary>
    /// Excel on Windows reads a BOM-less CSV in the system ANSI code page, which destroys Bangla.
    /// The BOM is the whole reason the CSV branch exists in a usable form (docs/08 §3.2).
    /// </summary>
    [Fact]
    public void Csv_starts_with_a_utf8_bom()
    {
        var bytes = ImportTemplateWriter.ToCsv(Schema());

        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());

        var text = Encoding.UTF8.GetString(bytes);
        text.ShouldContain(ImportColumns.ProductCode);
        text.ShouldContain("axis:colour");
    }
}
