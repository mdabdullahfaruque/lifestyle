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

    public RemoteImageImportOptions RemoteImageImport { get; init; } = new();

    /// <summary>
    /// Importing images by URL (docs/08 §7.2). <b>Off by default, deliberately.</b>
    /// <para>
    /// Letting a seller name a URL means letting them choose what this server connects to, and this
    /// server shares a host with four other products and a cloud metadata endpoint. The image library
    /// solves the same seller problem with none of that exposure, so this stays off unless there is a
    /// specific reason to accept the risk.
    /// </para>
    /// </summary>
    public sealed class RemoteImageImportOptions
    {
        public bool Enabled { get; init; }

        /// <summary>Per image. Independent of the upload cap: this one is spent on our bandwidth.</summary>
        [Range(1, 20 * 1024 * 1024)]
        public long MaxBytes { get; init; } = 8 * 1024 * 1024;

        [Range(1, 60)]
        public int TimeoutSeconds { get; init; } = 10;

        /// <summary>
        /// Allows plain http. Off, because http means the image can be swapped in transit by anyone on
        /// the path, and it would land on a storefront as though the vendor had chosen it.
        /// </summary>
        public bool AllowInsecureHttp { get; init; }
    }
}
