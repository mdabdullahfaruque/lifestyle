using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lifestyle.SharedKernel.Identifiers;

/// <summary>
/// URL slug generation. Shared because storefront slugs, product slugs and category slugs must all
/// produce identical output — three near-copies is how they drift.
/// </summary>
public static partial class Slug
{
    private const int MaxLength = 80;

    /// <summary>
    /// Reserved on the storefront subdomain — a vendor may not take a host we need for a platform
    /// surface, or one that would let them impersonate the platform (FRD §5.3).
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "seller", "shop", "shops", "store", "stores", "mail", "smtp", "imap",
        "ftp", "cdn", "static", "assets", "media", "img", "images", "blog", "help", "support",
        "status", "docs", "dev", "staging", "test", "app", "apps", "auth", "login", "account",
        "accounts", "billing", "pay", "payment", "payments", "checkout", "cart", "order", "orders",
        "search", "about", "legal", "privacy", "terms", "security", "abuse", "postmaster", "webmaster",
        "ns", "ns1", "ns2", "mx", "vpn", "git", "ci", "grafana", "metrics", "internal", "lifestyle"
    };

    /// <summary>
    /// Lowercases, strips diacritics, collapses everything that is not [a-z0-9] into single
    /// hyphens, and trims to <see cref="MaxLength"/>.
    /// </summary>
    public static string From(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var normalised = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalised.Length);

        foreach (var ch in normalised)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        var slug = NonSlugCharacters().Replace(builder.ToString().Normalize(NormalizationForm.FormC), "-");
        slug = CollapseHyphens().Replace(slug, "-").Trim('-');

        return slug.Length > MaxLength ? slug[..MaxLength].TrimEnd('-') : slug;
    }

    /// <summary>Appends <c>-2</c>, <c>-3</c>, … to make a slug unique within its scope.</summary>
    public static string WithSuffix(string slug, int attempt) =>
        attempt <= 1 ? slug : $"{slug[..Math.Min(slug.Length, MaxLength - 5)].TrimEnd('-')}-{attempt}";

    public static bool IsValid(string? slug) =>
        !string.IsNullOrWhiteSpace(slug)
        && slug.Length is >= 3 and <= MaxLength
        && ValidSlug().IsMatch(slug);

    public static bool IsReserved(string slug) => Reserved.Contains(slug);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacters();

    [GeneratedRegex("-{2,}")]
    private static partial Regex CollapseHyphens();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex ValidSlug();
}
