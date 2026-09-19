using System.Text;
using ClosedXML.Excel;
using Lifestyle.SharedKernel.Results;

namespace Lifestyle.Modules.Catalog.Internal;

/// <summary>One row of the seller's file: the row number they see, and the cells by header.</summary>
internal sealed record SheetRow(int RowNumber, IReadOnlyDictionary<string, string> Cells)
{
    public string? Get(string column) =>
        Cells.TryGetValue(column, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

internal sealed record SheetContent(
    IReadOnlyList<string> Headers,
    IReadOnlyList<SheetRow> Rows,
    string EncodingUsed);

/// <summary>
/// Reads an uploaded XLSX or CSV into headers and rows (docs/08 §3).
/// <para>
/// Nothing here knows what a product is. It turns a file into a grid of strings and reports how it
/// decoded it; deciding what the columns mean is <see cref="ImportRowParser"/>'s job.
/// </para>
/// </summary>
internal static class SheetReader
{
    /// <summary>
    /// A ceiling on rows, not a business rule. It stops one upload from pinning memory on a server
    /// shared with four other products (docs/07 §1).
    /// </summary>
    public const int MaxRows = 5000;

    public static Result<SheetContent> Read(Stream file, string fileName)
    {
        var isCsv = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        return isCsv ? ReadCsv(file) : ReadXlsx(file);
    }

    private static Result<SheetContent> ReadXlsx(Stream file)
    {
        XLWorkbook? workbook = null;
        try
        {
            try
            {
                workbook = new XLWorkbook(file);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidDataException)
            {
                return Error.Validation("catalog.import_unreadable",
                    "That file could not be opened as a spreadsheet. Export it as .xlsx or .csv and try again.");
            }

            // The template names it "Products"; a seller's own file may not, so fall back to the
            // first sheet rather than refusing a perfectly good upload over a tab name.
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == "Products")
                        ?? workbook.Worksheets.FirstOrDefault();

            if (sheet is null)
                return Error.Validation("catalog.import_empty", "That workbook has no sheets.");

            var used = sheet.RangeUsed();
            if (used is null)
                return Error.Validation("catalog.import_empty", "That sheet is empty.");

            var firstRow = used.FirstRow().RowNumber();
            var lastRow = used.LastRow().RowNumber();
            var firstColumn = used.FirstColumn().ColumnNumber();
            var lastColumn = used.LastColumn().ColumnNumber();

            var headers = new List<string>();
            for (var c = firstColumn; c <= lastColumn; c++)
                headers.Add(sheet.Cell(firstRow, c).GetString().Trim());

            var headerCheck = ValidateHeaders(headers);
            if (headerCheck.IsFailure) return headerCheck.Error;

            if (lastRow - firstRow > MaxRows) return TooManyRows();

            var rows = new List<SheetRow>();

            for (var r = firstRow + 1; r <= lastRow; r++)
            {
                var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var hasValue = false;

                for (var c = firstColumn; c <= lastColumn; c++)
                {
                    var header = headers[c - firstColumn];
                    if (string.IsNullOrEmpty(header)) continue;

                    var text = sheet.Cell(r, c).GetString().Trim();
                    cells[header] = text;
                    if (text.Length > 0) hasValue = true;
                }

                // A blank line in the middle of a sheet is formatting, not data.
                if (hasValue) rows.Add(new SheetRow(r, cells));
            }

            return new SheetContent(headers, rows, "xlsx");
        }
        finally
        {
            workbook?.Dispose();
        }
    }

    private static Result<SheetContent> ReadCsv(Stream file)
    {
        using var buffer = new MemoryStream();
        file.CopyTo(buffer);
        var bytes = buffer.ToArray();

        if (bytes.Length == 0)
            return Error.Validation("catalog.import_empty", "That file is empty.");

        var (text, encodingUsed) = Decode(bytes);

        // Parsed as records, not as lines. A description containing a newline is legal CSV — it is
        // quoted — and splitting on '\n' first would tear that one product's row into pieces and
        // shred every row after it.
        var records = ParseCsvRecords(text);

        if (records.Count == 0)
            return Error.Validation("catalog.import_empty", "That file has no rows.");

        var headers = records[0].Fields.Select(h => h.Trim()).ToList();

        var headerCheck = ValidateHeaders(headers);
        if (headerCheck.IsFailure) return headerCheck.Error;

        if (records.Count - 1 > MaxRows)
            return TooManyRows();

        var rows = new List<SheetRow>();

        for (var i = 1; i < records.Count; i++)
        {
            var record = records[i];
            if (record.Fields.All(string.IsNullOrWhiteSpace)) continue;

            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var c = 0; c < headers.Count; c++)
            {
                if (string.IsNullOrEmpty(headers[c])) continue;
                cells[headers[c]] = c < record.Fields.Count ? record.Fields[c].Trim() : string.Empty;
            }

            rows.Add(new SheetRow(record.LineNumber, cells));
        }

        return new SheetContent(headers, rows, encodingUsed);
    }

    private static Error TooManyRows() =>
        Error.Validation("catalog.import_too_many_rows",
            $"That sheet has more than {MaxRows} rows. Split it and import the parts separately.");

    /// <summary>
    /// Decodes CSV bytes, preferring UTF-8 and falling back rather than throwing.
    /// <para>
    /// Excel on Windows saves CSV in the system ANSI code page, which mangles Bangla. Strict UTF-8
    /// decoding throws on such a file, so the throw is the detection: if it fails, the bytes are
    /// not UTF-8 and Latin-1 at least round-trips the ASCII columns so the seller gets a readable
    /// error instead of a 500 (docs/08 §3.2).
    /// </para>
    /// </summary>
    private static (string Text, string Encoding) Decode(byte[] bytes)
    {
        var preamble = Encoding.UTF8.GetPreamble();

        if (bytes.Length >= preamble.Length && bytes.Take(preamble.Length).SequenceEqual(preamble))
            return (Encoding.UTF8.GetString(bytes, preamble.Length, bytes.Length - preamble.Length), "utf-8-bom");

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return (strict.GetString(bytes), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1.GetString(bytes), "latin-1");
        }
    }

    private static Result ValidateHeaders(List<string> headers)
    {
        if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
            return Error.Validation("catalog.import_no_headers",
                "The first row must be the column headers from the template.");

        var duplicates = headers
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        return duplicates.Count > 0
            ? Error.Validation("catalog.import_duplicate_headers",
                $"These columns appear more than once: {string.Join(", ", duplicates)}.")
            : Result.Success();
    }

    /// <summary>One CSV record, and the line the seller's editor shows it starting on.</summary>
    private sealed record CsvRecord(int LineNumber, List<string> Fields);

    /// <summary>
    /// Parses the whole file into records, honouring quoted fields, doubled quotes and newlines
    /// <b>inside</b> a quoted value.
    /// <para>
    /// Scanned once as a character stream rather than split into lines first, because a newline is
    /// only a record separator when it falls outside quotes. A product description containing a
    /// line break is perfectly legal CSV, and splitting on '\n' up front would tear that row apart
    /// and misalign every column in every row after it — the kind of corruption a seller would see
    /// as "the import mangled my catalogue" with no clue why.
    /// </para>
    /// <para>
    /// Written rather than taken from a package: the grammar is small and fully covered by tests,
    /// and a CSV dependency here would be another pinned version to audit.
    /// </para>
    /// </summary>
    private static List<CsvRecord> ParseCsvRecords(string text)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordStart = 1;

        void EndField() { fields.Add(current.ToString()); current.Clear(); }

        void EndRecord()
        {
            EndField();
            records.Add(new CsvRecord(recordStart, [.. fields]));
            fields.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    // "" inside a quoted field is a literal quote.
                    if (i + 1 < text.Length && text[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;

                    continue;
                }

                // A newline inside quotes is content, but still advances the line count so the
                // row numbers reported back to the seller match what their editor shows.
                if (ch == '\n') line++;

                current.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;

                case ',':
                    EndField();
                    break;

                case '\r':
                    // Swallowed; the '\n' that follows ends the record.
                    break;

                case '\n':
                    EndRecord();
                    line++;
                    recordStart = line;
                    break;

                default:
                    current.Append(ch);
                    break;
            }
        }

        // A final record with no trailing newline.
        if (current.Length > 0 || fields.Count > 0) EndRecord();

        return records;
    }
}
