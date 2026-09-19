namespace Lifestyle.Modules.Vendors.Internal;

/// <summary>
/// Bound from <c>Vendors:*</c>. Only addresses so far — a module cannot read
/// <c>PlatformOptions</c>, which lives in <c>Api</c> (docs/04 §2.1, rule 4).
/// </summary>
public sealed class VendorsModuleOptions
{
    public const string SectionName = "Vendors";

    /// <summary>
    /// Where an owner goes to manage their shop. Blank omits the link rather than emitting a dead
    /// one — an email whose only button 404s is worse than an email with no button.
    /// </summary>
    public string? SellerConsoleUrl { get; init; }

    /// <summary>
    /// The public address of a shop, with <c>{slug}</c> substituted.
    ///
    /// <para>
    /// A template rather than a root domain because the right answer changes: today the form that
    /// actually resolves is <c>https://example.com/shop/{slug}</c>, and
    /// <c>https://{slug}.example.com</c> only works once the wildcard DNS, certificate and nginx
    /// site of docs/07 §10 are in place. Switching between them is then configuration, not a
    /// deployment of new code.
    /// </para>
    /// </summary>
    public string? ShopUrlTemplate { get; init; }

    internal string? ShopUrlFor(string slug) =>
        string.IsNullOrWhiteSpace(ShopUrlTemplate)
            ? null
            : ShopUrlTemplate.Replace("{slug}", slug, StringComparison.Ordinal);
}
