using System.Text;
using Lifestyle.Modules.Catalog.Domain;

namespace Lifestyle.Modules.Catalog.Internal;

/// <summary>An image offered to the matcher: its library id and the name the seller gave it.</summary>
internal sealed record MatchCandidate(string MediaId, string FileName);

/// <summary>What the matcher decided about one image.</summary>
internal sealed record ImageMatch(
    string MediaId,
    string FileName,
    string? ProductCode,
    int Position,
    ImportMatchConfidence Confidence,
    string? MatchedBy);

/// <summary>
/// Assigns images to products by what the seller already called their files (docs/08 §4).
/// <para>
/// The whole point is that the seller does no organising work up front. Their files are the input,
/// this does the sorting, and the review grid only has to show the exceptions. A folder or a
/// filename that already carries a product code is matched outright; anything else is handed back
/// unmatched for a drag in the grid.
/// </para>
/// </summary>
internal static class ImageMatcher
{
    /// <summary>Qualifiers that mean "this is the main shot", whatever the number says.</summary>
    private static readonly string[] PrimaryQualifiers = ["main", "cover", "front", "primary", "hero"];

    public static IReadOnlyList<ImageMatch> Match(
        IReadOnlyList<MatchCandidate> candidates,
        IReadOnlyCollection<string> productCodes,
        IReadOnlyDictionary<string, string> skuToProductCode)
    {
        // Normalised key -> the code as the seller wrote it, so "LS-1001", "ls_1001" and "LS 1001"
        // all find the same product while the grid still shows their own spelling.
        var codeIndex = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var code in productCodes)
            codeIndex.TryAdd(Normalise(code), code);

        var skuIndex = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguousSkus = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (sku, code) in skuToProductCode)
        {
            var key = Normalise(sku);

            // A SKU is only unique within a product (Product.AddVariant), so the same SKU can sit
            // on two products of one vendor. Matching such a file would be a coin flip, so it goes
            // to the grid instead — docs/08 §9 D2.
            if (!skuIndex.TryAdd(key, code) && skuIndex[key] != code)
                ambiguousSkus.Add(key);
        }

        // One lookup for rules 2 and 3 together. A product code and a SKU can both be a leading run
        // of the same filename — "LS1001-RED-M" starts with the code "LS-1001" and *is* the SKU —
        // and the longer of the two is the more specific answer, so they compete in one index
        // rather than one rule shadowing the other.
        var index = new Dictionary<string, (string Code, string MatchedBy)>(StringComparer.Ordinal);

        foreach (var (key, code) in codeIndex)
            index[key] = (code, "filename");

        foreach (var (key, code) in skuIndex)
            if (!ambiguousSkus.Contains(key) && !index.ContainsKey(key))
                index[key] = (code, "sku");

        var results = new List<ImageMatch>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var (folder, stem) = SplitPath(candidate.FileName);

            // Rule 1: the folder is the product. Strongest signal there is — the seller put the
            // file there deliberately.
            if (folder is not null && codeIndex.TryGetValue(Normalise(folder), out var byFolder))
            {
                results.Add(new ImageMatch(
                    candidate.MediaId, candidate.FileName, byFolder,
                    OrderOf(stem), ImportMatchConfidence.Matched, "folder"));
                continue;
            }

            // Rules 2 and 3: the longest leading run of the filename that is a known code or SKU.
            var resolved = ResolveLeading(stem, index);

            if (resolved is { } hit)
            {
                results.Add(new ImageMatch(
                    candidate.MediaId, candidate.FileName, hit.Code,
                    OrderOf(hit.Remainder), ImportMatchConfidence.Matched, hit.MatchedBy));
                continue;
            }

            // Rule 4: a known code appears somewhere inside a longer name, e.g.
            // "photo-of-LS-1001-red". Real enough to pre-fill, not certain enough to trust.
            var normalisedStem = Normalise(stem);
            var contained = codeIndex
                .Where(kv => normalisedStem.Contains(kv.Key, StringComparison.Ordinal))
                .OrderByDescending(kv => kv.Key.Length)
                .Select(kv => kv.Value)
                .FirstOrDefault();

            results.Add(contained is not null
                ? new ImageMatch(candidate.MediaId, candidate.FileName, contained,
                    OrderOf(stem), ImportMatchConfidence.Guessed, "contains")
                : new ImageMatch(candidate.MediaId, candidate.FileName, null,
                    0, ImportMatchConfidence.Unmatched, null));
        }

        return Reindex(results);
    }

    /// <summary>
    /// Collapses each product's sort keys into dense 0..n-1 positions.
    /// <para>
    /// Sorting on (is-primary, number, filename) and then renumbering avoids the off-by-one
    /// argument about whether <c>_1</c> means position 0 or 1 — it means "first", whatever the
    /// seller intended by the digit.
    /// </para>
    /// </summary>
    private static List<ImageMatch> Reindex(List<ImageMatch> matches)
    {
        var output = new List<ImageMatch>(matches.Count);

        output.AddRange(matches.Where(m => m.ProductCode is null));

        foreach (var group in matches.Where(m => m.ProductCode is not null)
                     .GroupBy(m => m.ProductCode!, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderBy(m => m.Position)
                .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
                output.Add(ordered[i] with { Position = i });
        }

        return output;
    }

    /// <summary>Lowercased, with every separator removed, so only the characters that matter remain.</summary>
    private static string Normalise(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));

        return builder.ToString();
    }

    private static (string? Folder, string Stem) SplitPath(string fileName)
    {
        var normalised = fileName.Replace('\\', '/');
        var lastSlash = normalised.LastIndexOf('/');

        var name = lastSlash >= 0 ? normalised[(lastSlash + 1)..] : normalised;

        string? folder = null;
        if (lastSlash > 0)
        {
            var directory = normalised[..lastSlash];
            var previousSlash = directory.LastIndexOf('/');
            folder = previousSlash >= 0 ? directory[(previousSlash + 1)..] : directory;
        }

        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;

        return (folder, stem);
    }

    /// <summary>
    /// Finds the longest run of leading filename segments that is a known code or SKU, and returns
    /// what was left over.
    /// <para>
    /// Longest-first matters because codes contain separators themselves: cutting "LS-1001_main" at
    /// the first dash would look up "LS", find nothing, and send a perfectly well named file to the
    /// grid. Trying "ls1001main", then "ls1001", then "ls" finds the real answer and leaves "main"
    /// as the qualifier.
    /// </para>
    /// </summary>
    private static (string Code, string MatchedBy, string Remainder)? ResolveLeading(
        string stem, Dictionary<string, (string Code, string MatchedBy)> index)
    {
        var segments = stem.Split(['_', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        for (var take = segments.Length; take >= 1; take--)
        {
            var key = Normalise(string.Concat(segments.Take(take)));
            if (key.Length == 0) continue;

            if (index.TryGetValue(key, out var hit))
                return (hit.Code, hit.MatchedBy, string.Join('-', segments.Skip(take)));
        }

        return null;
    }

    /// <summary>
    /// A sort key, not a final position: 0 for a primary qualifier, otherwise the trailing number,
    /// otherwise last. <see cref="Reindex"/> turns these into real positions.
    /// </summary>
    private static int OrderOf(string stem)
    {
        var lowered = stem.ToLowerInvariant();

        // Nothing after the code — "LS-1001.jpg" is the one image the seller bothered to name
        // plainly, so it leads.
        if (lowered.Length == 0) return 0;

        if (Array.Exists(PrimaryQualifiers, q => lowered.EndsWith(q, StringComparison.Ordinal)
                                                 || lowered.StartsWith(q, StringComparison.Ordinal)))
            return 0;

        var digits = new StringBuilder();
        for (var i = lowered.Length - 1; i >= 0 && char.IsDigit(lowered[i]); i--)
            digits.Insert(0, lowered[i]);

        return digits.Length > 0 && int.TryParse(digits.ToString(), out var number)
            ? number
            : int.MaxValue;
    }
}
