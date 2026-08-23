using Lifestyle.SharedKernel.Identifiers;
using Shouldly;
using Xunit;

namespace Lifestyle.UnitTests.SharedKernel;

public sealed class SlugTests
{
    [Theory]
    [InlineData("Red Silk Scarf", "red-silk-scarf")]
    [InlineData("  Leading and trailing  ", "leading-and-trailing")]
    [InlineData("Multiple---hyphens", "multiple-hyphens")]
    [InlineData("Symbols!@#$%^&*()Here", "symbols-here")]
    [InlineData("UPPERCASE", "uppercase")]
    [InlineData("with 123 numbers", "with-123-numbers")]
    public void Produces_url_safe_slugs(string input, string expected)
    {
        Slug.From(input).ShouldBe(expected);
    }

    /// <summary>
    /// Italy is a target market, so accented Latin characters must degrade to their ASCII base
    /// rather than being stripped into an unusable slug.
    /// </summary>
    [Theory]
    [InlineData("Café Latté", "cafe-latte")]
    [InlineData("Naïve Façade", "naive-facade")]
    [InlineData("Boutique Élégance", "boutique-elegance")]
    public void Strips_diacritics_to_the_ascii_base(string input, string expected)
    {
        Slug.From(input).ShouldBe(expected);
    }

    /// <summary>
    /// Characters with no Unicode decomposition (ß, Ł, and every non-Latin script) cannot degrade
    /// to ASCII, so they become separators. A shop named only in such a script therefore produces
    /// an empty slug — which <see cref="Slug.IsValid"/> rejects, and the caller must then ask for
    /// a Latin shop address. This is a known limitation, not an accident.
    /// </summary>
    [Fact]
    public void Non_decomposable_characters_become_separators()
    {
        Slug.From("Große Straße").ShouldBe("gro-e-stra-e");
        Slug.IsValid(Slug.From("ঢাকা")).ShouldBeFalse();
    }

    [Fact]
    public void Truncates_very_long_input_without_a_trailing_hyphen()
    {
        var slug = Slug.From(new string('a', 50) + " " + new string('b', 50));

        slug.Length.ShouldBeLessThanOrEqualTo(80);
        slug.ShouldNotEndWith("-");
    }

    [Fact]
    public void Appends_a_numeric_suffix_for_collisions()
    {
        Slug.WithSuffix("my-shop", 1).ShouldBe("my-shop");
        Slug.WithSuffix("my-shop", 2).ShouldBe("my-shop-2");
        Slug.WithSuffix("my-shop", 17).ShouldBe("my-shop-17");
    }

    [Theory]
    [InlineData("valid-slug", true)]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("-leading", false)]
    [InlineData("trailing-", false)]
    [InlineData("double--hyphen", false)]
    [InlineData("Upper", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    public void Validates_slug_shape(string candidate, bool expected)
    {
        Slug.IsValid(candidate).ShouldBe(expected);
    }

    /// <summary>
    /// A vendor taking <c>admin</c> or <c>api</c> would shadow a platform surface, and could make a
    /// phishing page look official (FRD §5.3).
    /// </summary>
    [Theory]
    [InlineData("admin")]
    [InlineData("api")]
    [InlineData("www")]
    [InlineData("checkout")]
    [InlineData("ADMIN")]
    public void Reserves_platform_hostnames(string candidate)
    {
        Slug.IsReserved(candidate).ShouldBeTrue();
    }

    [Fact]
    public void Allows_ordinary_shop_names()
    {
        Slug.IsReserved("aisha-boutique").ShouldBeFalse();
    }
}
