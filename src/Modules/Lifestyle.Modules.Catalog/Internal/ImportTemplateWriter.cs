using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Lifestyle.Modules.Catalog.Internal;

/// <summary>
/// Turns an <see cref="ImportSchema"/> into a sheet the seller fills in (docs/08 §3).
/// <para>
/// XLSX is the primary format because it carries <b>data-validation dropdowns</b>: a Select
/// attribute becomes a picker rather than free text, which removes the largest class of import
/// error before the file is ever uploaded. CSV is offered for sellers whose tooling cannot manage
/// XLSX, and loses only the dropdowns.
/// </para>
/// </summary>
internal static class ImportTemplateWriter
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string CsvContentType = "text/csv";

    /// <summary>Rows the dropdowns are applied to. Beyond this the seller is past bulk import.</summary>
    private const int ValidatedRows = 2000;

    public static byte[] ToXlsx(ImportSchema schema)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Products");

        // Allowed values live on their own sheet and the dropdowns point at ranges here. An inline
        // Excel list is capped at 255 characters, which a colour list will exceed.
        var lists = workbook.Worksheets.Add("Lists");
        lists.Hide();

        var listColumn = 1;

        for (var i = 0; i < schema.Columns.Count; i++)
        {
            var column = schema.Columns[i];
            var columnNumber = i + 1;
            var header = sheet.Cell(1, columnNumber);

            header.Value = column.Name;
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = column.Required ? XLColor.LightSalmon : XLColor.LightGray;

            header.CreateComment().AddText(column.Required ? $"Required. {column.Help}" : column.Help);

            sheet.Column(columnNumber).Width = 22;

            // Text, so a code like 0012 keeps its leading zero and a long barcode is not rendered
            // in scientific notation. Both are real ways a sheet arrives corrupted.
            if (column.Name is ImportColumns.ProductCode or ImportColumns.Sku or ImportColumns.BarCode)
                sheet.Column(columnNumber).Style.NumberFormat.Format = "@";

            if (!column.HasDropdown) continue;

            for (var v = 0; v < column.AllowedValues.Count; v++)
                lists.Cell(v + 1, listColumn).Value = column.AllowedValues[v];

            var source = lists.Range(1, listColumn, column.AllowedValues.Count, listColumn);
            var target = sheet.Range(2, columnNumber, ValidatedRows, columnNumber);
            target.CreateDataValidation().List(source, inCellDropdown: true);

            listColumn++;
        }

        sheet.SheetView.FreezeRows(1);
        WriteInstructions(workbook, schema);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// CSV with a UTF-8 <b>BOM</b>. Without it Excel on Windows opens the file in the system ANSI
    /// code page and silently destroys every Bangla product name — the seller sees mojibake and
    /// blames the platform (docs/08 §3.2).
    /// </summary>
    public static byte[] ToCsv(ImportSchema schema)
    {
        var line = string.Join(',', schema.Columns.Select(c => Escape(c.Name)));
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. bytes];
    }

    public static string FileName(ImportSchema schema, string extension) =>
        string.Create(CultureInfo.InvariantCulture, $"lifestyle-import-{schema.CategorySlug}.{extension}");

    private static void WriteInstructions(XLWorkbook workbook, ImportSchema schema)
    {
        var sheet = workbook.Worksheets.Add("Instructions");

        sheet.Cell(1, 1).Value = $"Bulk import template — {schema.CategoryName}";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        var notes = new[]
        {
            "Fill in the Products sheet. Row 1 is the header — do not rename or reorder it.",
            "One row per sellable variant. A product with three sizes is three rows.",
            $"Repeat the same {ImportColumns.ProductCode} on every row of one product.",
            "Product columns (name, description, brand) are read from the first row of each product.",
            "Everything imports as a draft. Nothing goes on the storefront until you submit it and it is approved.",
            "Re-uploading a corrected sheet updates the same products — it does not create duplicates.",
            "Orange headers are required. Hover any header for a description of the column."
        };

        for (var i = 0; i < notes.Length; i++)
            sheet.Cell(i + 3, 1).Value = notes[i];

        var row = notes.Length + 5;
        sheet.Cell(row, 1).Value = "Column";
        sheet.Cell(row, 2).Value = "Required";
        sheet.Cell(row, 3).Value = "Notes";
        sheet.Range(row, 1, row, 3).Style.Font.Bold = true;

        foreach (var column in schema.Columns)
        {
            row++;
            sheet.Cell(row, 1).Value = column.Name;
            sheet.Cell(row, 2).Value = column.Required ? "yes" : "";
            sheet.Cell(row, 3).Value = column.HasDropdown
                ? $"{column.Help} One of: {string.Join(", ", column.AllowedValues)}"
                : column.Help;
        }

        sheet.Column(1).Width = 28;
        sheet.Column(2).Width = 10;
        sheet.Column(3).Width = 90;
    }

    private static string Escape(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
}
