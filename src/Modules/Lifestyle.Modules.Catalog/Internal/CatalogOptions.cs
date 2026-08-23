using System.ComponentModel.DataAnnotations;

namespace Lifestyle.Modules.Catalog.Internal;

public sealed class CatalogModuleOptions
{
    public const string SectionName = "Catalog";

    /// <summary>
    /// The deployment's trading currency. One deployment per country (Plan §1), so this is a single
    /// value rather than a per-product choice.
    /// </summary>
    [Required, RegularExpression("^[A-Z]{3}$")]
    public required string Currency { get; init; }

    [Range(1, 100)]
    public int DefaultPageSize { get; init; } = 24;

    [Range(1, 200)]
    public int MaxPageSize { get; init; } = 100;
}
