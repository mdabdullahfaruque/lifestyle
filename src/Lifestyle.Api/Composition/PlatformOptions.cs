using System.ComponentModel.DataAnnotations;

namespace Lifestyle.Api.Composition;

/// <summary>
/// Which country this deployment serves, and the hosts it answers on. One codebase, one deployment
/// per country (Plan §1) — so country is configuration, and the environment name stays one of the
/// three standard ones. (PropertyMart used a "ProductionMY" environment name for this; it broke
/// <c>IsProduction()</c> and spread config across near-duplicate files.)
/// </summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>ISO 3166-1 alpha-2: <c>MY</c>, <c>BD</c> or <c>IT</c>.</summary>
    [Required, RegularExpression("^[A-Z]{2}$")]
    public required string Country { get; init; }

    [Required, RegularExpression("^[A-Z]{3}$")]
    public required string Currency { get; init; }

    /// <summary>Apex domain, e.g. <c>lifestyle.com</c>. Storefront subdomains hang off this.</summary>
    [Required]
    public required string RootDomain { get; init; }

    /// <summary>IANA id used to render dates for this market, e.g. <c>Asia/Kuala_Lumpur</c>.</summary>
    [Required]
    public required string TimeZone { get; init; }

    public string MarketplaceHost => $"www.{RootDomain}";
    public string SellerHost => $"seller.{RootDomain}";
    public string AdminHost => $"admin.{RootDomain}";
    public string ApiHost => $"api.{RootDomain}";

    /// <summary>Origins allowed to call the API with credentials.</summary>
    public IReadOnlyList<string> CorsOrigins { get; init; } = [];
}
