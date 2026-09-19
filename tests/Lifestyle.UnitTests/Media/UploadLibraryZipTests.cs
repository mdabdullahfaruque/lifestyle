using Lifestyle.Modules.Media.Features.Library;
using Shouldly;
using Xunit;

namespace Lifestyle.UnitTests.Media;

/// <summary>
/// ZIP entry names are attacker-controlled input and are also what the import matcher reads, so
/// this one function is both the zip-slip guard and the reason folder-per-product works
/// (docs/08 §4.1, §7.1).
/// </summary>
public sealed class UploadLibraryZipTests
{
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config")]
    [InlineData("/etc/shadow")]
    [InlineData("C:/Windows/notepad.exe")]
    [InlineData("folder/../../escape.jpg")]
    public void A_path_that_tries_to_escape_is_refused_outright(string path)
    {
        UploadLibraryZip.SafeDisplayName(path).ShouldBeNull();
    }

    [Theory]
    [InlineData("__MACOSX/LS-1001/._main.jpg")]
    [InlineData(".DS_Store")]
    [InlineData("LS-1001/.hidden.jpg")]
    public void Archive_junk_is_skipped(string path)
    {
        UploadLibraryZip.SafeDisplayName(path).ShouldBeNull();
    }

    /// <summary>
    /// The folder is the product code by convention, and folding it in is what lets the matcher
    /// place the file without anything having to store a directory from an archive.
    /// </summary>
    [Fact]
    public void A_folder_is_folded_into_the_name()
    {
        UploadLibraryZip.SafeDisplayName("LS-1001/main.jpg").ShouldBe("LS-1001_main.jpg");
    }

    [Fact]
    public void Only_the_immediate_parent_is_kept()
    {
        UploadLibraryZip.SafeDisplayName("shoot-june/LS-1001/2.jpg").ShouldBe("LS-1001_2.jpg");
    }

    [Fact]
    public void A_file_at_the_archive_root_keeps_its_own_name()
    {
        UploadLibraryZip.SafeDisplayName("LS-1001.jpg").ShouldBe("LS-1001.jpg");
    }

    [Fact]
    public void Backslash_separators_are_understood()
    {
        UploadLibraryZip.SafeDisplayName("LS-1001\\main.jpg").ShouldBe("LS-1001_main.jpg");
    }

    [Fact]
    public void A_very_long_name_is_truncated_rather_than_refused()
    {
        var name = UploadLibraryZip.SafeDisplayName(new string('a', 400) + ".jpg");

        name.ShouldNotBeNull();
        name.Length.ShouldBe(255);
    }

    /// <summary>
    /// The end-to-end point of the flattening: what comes out must be something the matcher
    /// resolves back to the folder's product code.
    /// </summary>
    [Fact]
    public void The_flattened_name_still_matches_its_product_code()
    {
        var flattened = UploadLibraryZip.SafeDisplayName("LS-1001/DSC_9987.jpg");
        flattened.ShouldNotBeNull();

        var match = Lifestyle.Modules.Catalog.Internal.ImageMatcher.Match(
            [new Lifestyle.Modules.Catalog.Internal.MatchCandidate("m1", flattened)],
            ["LS-1001"],
            []).Single();

        match.ProductCode.ShouldBe("LS-1001");
    }
}
