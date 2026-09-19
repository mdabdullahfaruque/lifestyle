using System.Text;
using Lifestyle.Modules.Catalog.Internal;
using Shouldly;
using Xunit;

namespace Lifestyle.UnitTests.Catalog;

/// <summary>
/// The reader turns a seller's file into a grid of strings. Its failures are the worst kind,
/// because a misparsed sheet imports the wrong data rather than refusing to import.
/// </summary>
public sealed class SheetReaderTests
{
    private static MemoryStream Csv(string text, bool bom = true)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new MemoryStream(bom ? [.. Encoding.UTF8.GetPreamble(), .. bytes] : bytes);
    }

    [Fact]
    public void Headers_and_rows_are_read()
    {
        using var file = Csv("product_code,name\nLS-1001,Scarf\nLS-1002,Shawl\n");

        var result = SheetReader.Read(file, "products.csv");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(2);
        result.Value.Rows[0].Get("name").ShouldBe("Scarf");
    }

    /// <summary>
    /// A description containing a line break is legal CSV — it is quoted. Splitting the file into
    /// lines before parsing quotes tears that row apart and misaligns every row after it, which a
    /// seller sees as "the import mangled my catalogue" with no clue why.
    /// </summary>
    [Fact]
    public void A_newline_inside_a_quoted_field_does_not_end_the_row()
    {
        using var file = Csv(
            "product_code,description,name\n"
            + "LS-1001,\"Soft silk.\nHand finished.\",Scarf\n"
            + "LS-1002,Plain,Shawl\n");

        var result = SheetReader.Read(file, "products.csv");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(2);
        result.Value.Rows[0].Get("description").ShouldBe("Soft silk.\nHand finished.");

        // The row after the multi-line one must still line up.
        result.Value.Rows[1].Get("name").ShouldBe("Shawl");
        result.Value.Rows[1].Get("product_code").ShouldBe("LS-1002");
    }

    [Fact]
    public void A_doubled_quote_is_a_literal_quote()
    {
        using var file = Csv("product_code,name\nLS-1001,\"The \"\"Big\"\" Scarf\"\n");

        SheetReader.Read(file, "products.csv").Value.Rows[0].Get("name")
            .ShouldBe("The \"Big\" Scarf");
    }

    [Fact]
    public void A_quoted_comma_is_not_a_column_break()
    {
        using var file = Csv("product_code,name,brand\nLS-1001,\"Scarf, silk\",Aisha\n");

        var row = SheetReader.Read(file, "products.csv").Value.Rows[0];

        row.Get("name").ShouldBe("Scarf, silk");
        row.Get("brand").ShouldBe("Aisha");
    }

    [Fact]
    public void Bangla_survives_a_utf8_file()
    {
        using var file = Csv("product_code,name\nLS-1001,শাড়ি\n");

        SheetReader.Read(file, "products.csv").Value.Rows[0].Get("name").ShouldBe("শাড়ি");
    }

    /// <summary>
    /// Silently dropping rows past the cap would import part of a catalogue and report success —
    /// the seller would have no way to know which products never arrived.
    /// </summary>
    [Fact]
    public void Too_many_rows_is_refused_rather_than_truncated()
    {
        var builder = new StringBuilder("product_code,name\n");
        for (var i = 0; i <= SheetReader.MaxRows + 1; i++)
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"LS-{i},Item {i}\n");

        using var file = Csv(builder.ToString());

        var result = SheetReader.Read(file, "products.csv");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.import_too_many_rows");
    }

    [Fact]
    public void A_duplicated_header_is_refused()
    {
        using var file = Csv("product_code,name,name\nLS-1001,a,b\n");

        SheetReader.Read(file, "products.csv").Error.Code.ShouldBe("catalog.import_duplicate_headers");
    }

    [Fact]
    public void A_blank_line_in_the_middle_is_formatting_not_data()
    {
        using var file = Csv("product_code,name\nLS-1001,Scarf\n\nLS-1002,Shawl\n");

        SheetReader.Read(file, "products.csv").Value.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public void A_last_row_without_a_trailing_newline_is_still_read()
    {
        using var file = Csv("product_code,name\nLS-1001,Scarf");

        SheetReader.Read(file, "products.csv").Value.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public void An_empty_file_is_refused()
    {
        using var file = Csv(string.Empty, bom: false);

        SheetReader.Read(file, "products.csv").Error.Code.ShouldBe("catalog.import_empty");
    }
}
