using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Internal;
using Shouldly;
using Xunit;

namespace Lifestyle.UnitTests.Catalog;

/// <summary>
/// The matcher is what makes bulk import worth using: it decides how much organising the seller
/// still has to do by hand (docs/08 §4).
/// </summary>
public sealed class ImageMatcherTests
{
    private static readonly string[] Codes = ["LS-1001", "LS-1002"];

    private static IReadOnlyList<ImageMatch> Match(params string[] fileNames) =>
        ImageMatcher.Match(
            [.. fileNames.Select((f, i) => new MatchCandidate($"media{i}", f))],
            Codes,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void A_folder_named_after_the_product_claims_everything_in_it()
    {
        var matches = Match("LS-1001/anything.jpg", "LS-1001/DSC_9987.jpg");

        matches.ShouldAllBe(m => m.ProductCode == "LS-1001");
        matches.ShouldAllBe(m => m.Confidence == ImportMatchConfidence.Matched);
        matches.ShouldAllBe(m => m.MatchedBy == "folder");
    }

    [Theory]
    [InlineData("LS-1001.jpg")]
    [InlineData("ls_1001.jpg")]
    [InlineData("LS 1001.png")]
    [InlineData("ls1001.webp")]
    public void Separators_and_case_do_not_matter(string fileName)
    {
        var match = Match(fileName).Single();

        match.ProductCode.ShouldBe("LS-1001");
        match.Confidence.ShouldBe(ImportMatchConfidence.Matched);
    }

    [Fact]
    public void A_primary_qualifier_wins_position_zero_whatever_the_numbers_say()
    {
        var matches = Match("LS-1001_2.jpg", "LS-1001_3.jpg", "LS-1001_main.jpg");

        var primary = matches.Single(m => m.FileName == "LS-1001_main.jpg");
        primary.Position.ShouldBe(0);

        // Positions are dense and unique, whatever the seller numbered them.
        matches.Select(m => m.Position).OrderBy(p => p).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void An_unrecognised_name_is_left_for_the_grid_rather_than_guessed_at()
    {
        var match = Match("IMG_20260612_142233.jpg").Single();

        match.ProductCode.ShouldBeNull();
        match.Confidence.ShouldBe(ImportMatchConfidence.Unmatched);
    }

    [Fact]
    public void A_code_buried_in_a_longer_name_is_a_guess_not_a_certainty()
    {
        var match = Match("photo-of-LS-1002-in-red.jpg").Single();

        match.ProductCode.ShouldBe("LS-1002");
        match.Confidence.ShouldBe(ImportMatchConfidence.Guessed);
    }

    /// <summary>
    /// SKUs are only unique within a product, so the same SKU may sit on two of a vendor's
    /// products. Matching one would be a coin flip (docs/08 §9 D2).
    /// </summary>
    [Fact]
    public void A_sku_that_belongs_to_two_products_is_not_matched()
    {
        var ambiguous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHARED-1"] = "LS-1001"
        };

        // Same SKU key resolving to a second product makes it ambiguous.
        ambiguous["shared_1"] = "LS-1002";

        var matches = ImageMatcher.Match(
            [new MatchCandidate("m1", "SHARED-1.jpg")], Codes, ambiguous);

        matches.Single().ProductCode.ShouldBeNull();
    }

    [Fact]
    public void A_unique_sku_matches_its_product()
    {
        var skus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LS1001-RED-M"] = "LS-1001"
        };

        var match = ImageMatcher.Match(
            [new MatchCandidate("m1", "LS1001-RED-M.jpg")], Codes, skus).Single();

        match.ProductCode.ShouldBe("LS-1001");
        match.MatchedBy.ShouldBe("sku");
    }

    [Fact]
    public void Positions_restart_per_product()
    {
        var matches = Match("LS-1001_1.jpg", "LS-1001_2.jpg", "LS-1002_1.jpg");

        matches.Where(m => m.ProductCode == "LS-1001").Select(m => m.Position).OrderBy(p => p).ShouldBe([0, 1]);
        matches.Single(m => m.ProductCode == "LS-1002").Position.ShouldBe(0);
    }
}

/// <summary>
/// Capture-time clustering (docs/08 §4.3) is what makes "just drop the whole folder in" usable
/// instead of a wall of four hundred identically-named tiles.
/// </summary>
public sealed class ImageMatcherClusteringTests
{
    private static readonly DateTimeOffset Shoot = new(2026, 6, 12, 14, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<ImageMatch> Match(params (string Name, DateTimeOffset? Taken)[] files) =>
        ImageMatcher.Match(
            [.. files.Select((f, i) => new MatchCandidate($"media{i}", f.Name, f.Taken))],
            ["LS-1001"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Photos_taken_seconds_apart_are_one_group()
    {
        var matches = Match(
            ("IMG_001.jpg", Shoot),
            ("IMG_002.jpg", Shoot.AddSeconds(4)),
            ("IMG_003.jpg", Shoot.AddSeconds(9)));

        matches.Select(m => m.ClusterKey).Distinct().Count().ShouldBe(1);
        matches.ShouldAllBe(m => m.ClusterKey == 0);
    }

    [Fact]
    public void A_long_gap_starts_the_next_product()
    {
        var matches = Match(
            ("IMG_001.jpg", Shoot),
            ("IMG_002.jpg", Shoot.AddSeconds(5)),
            // Long enough to be the seller fetching the next item off the shelf.
            ("IMG_003.jpg", Shoot.AddMinutes(4)),
            ("IMG_004.jpg", Shoot.AddMinutes(4).AddSeconds(6)));

        var keys = matches.OrderBy(m => m.FileName, StringComparer.Ordinal).Select(m => m.ClusterKey).ToList();

        keys.ShouldBe([0, 0, 1, 1]);
    }

    /// <summary>
    /// Clustering only groups; it never invents a product. Nothing here may arrive assigned.
    /// </summary>
    [Fact]
    public void Clustering_never_assigns_a_product()
    {
        var matches = Match(("IMG_001.jpg", Shoot), ("IMG_002.jpg", Shoot.AddSeconds(3)));

        matches.ShouldAllBe(m => m.ProductCode == null);
        matches.ShouldAllBe(m => m.Confidence == ImportMatchConfidence.Unmatched);
    }

    [Fact]
    public void An_image_the_matcher_placed_is_not_clustered()
    {
        var matches = Match(("LS-1001_main.jpg", Shoot), ("IMG_002.jpg", Shoot.AddSeconds(3)));

        matches.Single(m => m.ProductCode == "LS-1001").ClusterKey.ShouldBeNull();
        matches.Single(m => m.ProductCode is null).ClusterKey.ShouldNotBeNull();
    }

    /// <summary>A screenshot or a photo through a social app has no EXIF; it must still be listed.</summary>
    [Fact]
    public void A_photo_with_no_capture_time_is_left_ungrouped_not_dropped()
    {
        var matches = Match(("IMG_001.jpg", Shoot), ("screenshot.png", null));

        matches.Count.ShouldBe(2);
        matches.Single(m => m.FileName == "screenshot.png").ClusterKey.ShouldBeNull();
    }
}
