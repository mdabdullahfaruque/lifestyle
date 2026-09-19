using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Internal;

namespace Lifestyle.Modules.Catalog.Features.Import;

/// <summary>The review grid's data: what the sheet said, what matched, and what needs a decision.</summary>
public sealed record ImportJobResponse(
    Guid Id,
    string Status,
    string SourceFileName,
    Guid CategoryId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    string? FailureReason,
    int TotalRows,
    int ImportableRows,
    int ErrorRows,
    int RowsNeedingConfirmation,
    int CreatedCount,
    int UpdatedCount,
    IReadOnlyList<ImportProductResponse> Products,
    IReadOnlyList<ImportImageResponse> LooseImages);

/// <summary>One product as the import understands it: its rows, its images, and its verdict.</summary>
public sealed record ImportProductResponse(
    string ProductCode,
    string? Name,
    bool IsNew,
    Guid? ExistingProductId,
    bool AffectsLiveProduct,
    bool LiveUpdateConfirmed,
    IReadOnlyList<ImportRowResponse> Rows,
    IReadOnlyList<ImportImageResponse> Images);

public sealed record ImportRowResponse(
    Guid Id,
    int RowNumber,
    string? Sku,
    string Outcome,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsSkipped,
    bool IsImportable);

public sealed record ImportImageResponse(
    Guid Id,
    string MediaId,
    string FileName,
    string? Url,
    /// <summary>Which product it is currently assigned to; null while it is unplaced.</summary>
    string? ProductCode,
    int Position,
    string Confidence,
    string? MatchedBy);

internal static class ImportMapping
{
    public static ImportJobResponse ToResponse(
        this ImportJob job,
        IReadOnlyDictionary<string, string> imageUrls)
    {
        var rows = job.Rows.OrderBy(r => r.RowNumber).ToList();
        var images = job.Images.ToList();

        var products = rows
            .Where(r => r.ProductCode is not null)
            .GroupBy(r => r.ProductCode!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var groupImages = images
                    .Where(i => string.Equals(i.ProductCode, group.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(i => i.Position)
                    .Select(i => i.ToResponse(imageUrls))
                    .ToList();

                return new ImportProductResponse(
                    group.Key,
                    first.Values.TryGetValue(ImportColumns.Name, out var name) ? name : null,
                    IsNew: first.TargetProductId is null,
                    first.TargetProductId,
                    first.AffectsLiveProduct,
                    first.LiveUpdateConfirmed,
                    [.. group.Select(ToResponse)],
                    groupImages);
            })
            .ToList();

        var loose = images
            .Where(i => i.ProductCode is null)
            .Select(i => i.ToResponse(imageUrls))
            .ToList();

        return new ImportJobResponse(
            job.Id,
            job.Status.ToString(),
            job.SourceFileName,
            job.CategoryId,
            job.ExpiresAt,
            job.CreatedAt,
            job.FailureReason,
            rows.Count,
            rows.Count(r => r.IsImportable),
            rows.Count(r => r.Outcome == ImportRowOutcome.Error),
            rows.Count(r => r.AffectsLiveProduct && !r.LiveUpdateConfirmed),
            job.CreatedCount,
            job.UpdatedCount,
            products,
            loose);
    }

    private static ImportRowResponse ToResponse(ImportJobRow row) => new(
        row.Id,
        row.RowNumber,
        row.Sku,
        row.Outcome.ToString(),
        row.ErrorCode,
        row.ErrorMessage,
        row.IsSkipped,
        row.IsImportable);

    private static ImportImageResponse ToResponse(
        this ImportJobImage image, IReadOnlyDictionary<string, string> urls) => new(
        image.Id,
        image.MediaId,
        image.FileName,
        urls.TryGetValue(image.MediaId, out var url) ? url : null,
        image.ProductCode,
        image.Position,
        image.Confidence.ToString(),
        image.MatchedBy);
}
