using System.ComponentModel.DataAnnotations;

namespace Lifestyle.Modules.Media.Internal;

public sealed class MediaModuleOptions
{
    public const string SectionName = "Media";

    /// <summary>Hard cap per file. Rejected before anything is written to disk.</summary>
    [Range(1, 50 * 1024 * 1024)]
    public long MaxUploadBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>
    /// Allow-list, not a block-list. An allow-list fails closed: a format we have not considered is
    /// rejected rather than accepted.
    /// </summary>
    public IReadOnlyList<string> AllowedContentTypes { get; init; } =
        ["image/jpeg", "image/png", "image/webp", "application/pdf"];

    /// <summary>Longest edge, in pixels, for each named derivative.</summary>
    public IReadOnlyDictionary<string, int> VariantSizes { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["thumb"] = 200,
        ["card"] = 600,
        ["large"] = 1600
    };

    /// <summary>How long an unattached upload survives before the sweeper deletes it.</summary>
    [Range(1, 168)]
    public int OrphanRetentionHours { get; init; } = 24;
}
