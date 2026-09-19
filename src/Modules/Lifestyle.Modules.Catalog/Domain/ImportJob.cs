using Lifestyle.SharedKernel.Domain;
using Lifestyle.SharedKernel.Results;

namespace Lifestyle.Modules.Catalog.Domain;

/// <summary>
/// One bulk-import attempt by one vendor (docs/08 §5).
/// <para>
/// The job is a <b>staging area</b>: a sheet is parsed into rows and images are matched to them,
/// and none of it touches the catalogue until the seller commits. That is what makes the review
/// grid possible and what stops a mis-keyed spreadsheet from silently rewriting a live shop.
/// </para>
/// </summary>
internal sealed class ImportJob : AggregateRoot
{
    private readonly List<ImportJobRow> _rows = [];
    private readonly List<ImportJobImage> _images = [];

    private ImportJob() { }

    public Guid VendorId { get; private set; }
    public ImportJobStatus Status { get; private set; } = ImportJobStatus.Analysing;

    public string SourceFileName { get; private set; } = null!;
    public Guid CategoryId { get; private set; }

    /// <summary>
    /// A job left in review is dropped after this. Its staged images fall back to orphan status and
    /// the existing media sweeper collects them, so abandonment costs nothing permanent.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public string? FailureReason { get; private set; }
    public DateTimeOffset? CommittedAt { get; private set; }

    public int CreatedCount { get; private set; }
    public int UpdatedCount { get; private set; }

    public IReadOnlyCollection<ImportJobRow> Rows => _rows.AsReadOnly();
    public IReadOnlyCollection<ImportJobImage> Images => _images.AsReadOnly();

    public static ImportJob Start(
        Guid vendorId, Guid categoryId, string sourceFileName, DateTimeOffset now, TimeSpan lifetime) => new()
        {
            VendorId = vendorId,
            CategoryId = categoryId,
            SourceFileName = sourceFileName,
            Status = ImportJobStatus.Analysing,
            ExpiresAt = now + lifetime,
            CreatedAt = now
        };

    public void AddRow(ImportJobRow row) => _rows.Add(row);

    public void AddImage(ImportJobImage image) => _images.Add(image);

    /// <summary>Analysis finished: the grid can be shown.</summary>
    public Result Ready(DateTimeOffset now)
    {
        if (Status != ImportJobStatus.Analysing)
            return Error.Conflict("catalog.import_not_analysing", $"A job in {Status} state cannot become ready.");

        Status = ImportJobStatus.NeedsReview;
        UpdatedAt = now;
        return Result.Success();
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        Status = ImportJobStatus.Failed;
        FailureReason = reason;
        UpdatedAt = now;
    }

    /// <summary>
    /// Locks the job for commit. Separate from <see cref="Complete"/> so a second commit of the
    /// same job cannot run concurrently and create every product twice.
    /// </summary>
    public Result BeginCommit(DateTimeOffset now)
    {
        if (Status != ImportJobStatus.NeedsReview)
            return Error.Conflict("catalog.import_not_reviewable",
                $"Only a job awaiting review can be committed; this one is {Status}.");

        if (now >= ExpiresAt)
            return Error.Conflict("catalog.import_expired",
                "This import has expired. Upload the sheet again.");

        if (!_rows.Any(r => r.IsImportable))
            return Error.Validation("catalog.import_nothing_to_do",
                "Every row in this import has an error. Fix the sheet and upload it again.");

        Status = ImportJobStatus.Committing;
        UpdatedAt = now;
        return Result.Success();
    }

    public void Complete(int created, int updated, DateTimeOffset now)
    {
        Status = ImportJobStatus.Completed;
        CreatedCount = created;
        UpdatedCount = updated;
        CommittedAt = now;
        UpdatedAt = now;
    }

    public Result Cancel(DateTimeOffset now)
    {
        if (Status is ImportJobStatus.Completed or ImportJobStatus.Committing)
            return Error.Conflict("catalog.import_not_cancellable",
                "A committed import cannot be cancelled.");

        Status = ImportJobStatus.Cancelled;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Applies the seller's corrections from the review grid: which rows to skip, and confirmation
    /// for rows that would send a live product back to moderation (docs/08 §9 D4).
    /// </summary>
    public Result Revise(IReadOnlyDictionary<Guid, bool> skip, IReadOnlyCollection<Guid> confirmLive, DateTimeOffset now)
    {
        if (Status != ImportJobStatus.NeedsReview)
            return Error.Conflict("catalog.import_not_reviewable",
                $"A job in {Status} state cannot be edited.");

        foreach (var (rowId, isSkipped) in skip)
        {
            var row = _rows.FirstOrDefault(r => r.Id == rowId);
            if (row is null) return Error.NotFound("catalog.import_row_not_found");

            row.SetSkipped(isSkipped);
        }

        foreach (var rowId in confirmLive)
        {
            var row = _rows.FirstOrDefault(r => r.Id == rowId);
            if (row is null) return Error.NotFound("catalog.import_row_not_found");

            row.ConfirmLiveUpdate();
        }

        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Reassigns a staged image to a different product code, or detaches it entirely.</summary>
    public Result AssignImage(Guid imageId, string? productCode, int position, DateTimeOffset now)
    {
        if (Status != ImportJobStatus.NeedsReview)
            return Error.Conflict("catalog.import_not_reviewable",
                $"A job in {Status} state cannot be edited.");

        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image is null) return Error.NotFound("catalog.import_image_not_found");

        image.AssignTo(productCode, position);
        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>One sheet row, parsed and judged, waiting to become a variant.</summary>
internal sealed class ImportJobRow : Entity
{
    private ImportJobRow() { }

    public Guid ImportJobId { get; private set; }

    /// <summary>The row number in the seller's own file, so an error message points at what they see.</summary>
    public int RowNumber { get; private set; }

    public string? ProductCode { get; private set; }
    public string? Sku { get; private set; }

    /// <summary>The parsed cells, kept so the errors-only sheet can be written back out.</summary>
    public Dictionary<string, string> Values { get; private set; } = [];

    public ImportRowOutcome Outcome { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>Set when this row's product code already exists for the vendor.</summary>
    public Guid? TargetProductId { get; private set; }

    /// <summary>
    /// True when the matched product is live or awaiting moderation, so importing it would take it
    /// off the storefront. Such a row is held back until the seller confirms (docs/08 §9 D4).
    /// </summary>
    public bool AffectsLiveProduct { get; private set; }

    public bool LiveUpdateConfirmed { get; private set; }
    public bool IsSkipped { get; private set; }

    /// <summary>A row that will actually be written when the job is committed.</summary>
    public bool IsImportable =>
        !IsSkipped
        && Outcome == ImportRowOutcome.Ok
        && (!AffectsLiveProduct || LiveUpdateConfirmed);

    public static ImportJobRow Create(
        Guid jobId, int rowNumber, string? productCode, string? sku, Dictionary<string, string> values) => new()
        {
            ImportJobId = jobId,
            RowNumber = rowNumber,
            ProductCode = productCode,
            Sku = sku,
            Values = values
        };

    public void Accept() => Outcome = ImportRowOutcome.Ok;

    public void Reject(string code, string message)
    {
        Outcome = ImportRowOutcome.Error;
        ErrorCode = code;
        ErrorMessage = message;
    }

    public void MatchTo(Guid productId, bool affectsLive)
    {
        TargetProductId = productId;
        AffectsLiveProduct = affectsLive;
    }

    public void ConfirmLiveUpdate() => LiveUpdateConfirmed = true;

    public void SetSkipped(bool skipped) => IsSkipped = skipped;
}

/// <summary>
/// A staged image and the product code the matcher assigned it to, with how confident that was.
/// </summary>
internal sealed class ImportJobImage : Entity
{
    private ImportJobImage() { }

    public Guid ImportJobId { get; private set; }

    /// <summary>The media id in the vendor's library.</summary>
    public string MediaId { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    /// <summary>Null when nothing matched — the grid shows it as loose for the seller to place.</summary>
    public string? ProductCode { get; private set; }

    public int Position { get; private set; }
    public ImportMatchConfidence Confidence { get; private set; }

    /// <summary>Which matcher rule claimed it, for the "why is this here" tooltip in the grid.</summary>
    public string? MatchedBy { get; private set; }

    /// <summary>
    /// Groups unplaced photos taken in one burst, so the grid can offer a whole shoot to drag
    /// rather than eight separate tiles. Null once the image belongs to a product.
    /// </summary>
    public int? ClusterKey { get; private set; }

    public static ImportJobImage Create(
        Guid jobId, string mediaId, string fileName, string? productCode,
        int position, ImportMatchConfidence confidence, string? matchedBy, int? clusterKey) => new()
        {
            ImportJobId = jobId,
            MediaId = mediaId,
            FileName = fileName,
            ProductCode = productCode,
            Position = position,
            Confidence = confidence,
            MatchedBy = matchedBy,
            ClusterKey = clusterKey
        };

    /// <summary>A seller's drag in the grid. Their choice is always certain, by definition.</summary>
    public void AssignTo(string? productCode, int position)
    {
        ProductCode = string.IsNullOrWhiteSpace(productCode) ? null : productCode.Trim();
        Position = position;
        Confidence = ProductCode is null ? ImportMatchConfidence.Unmatched : ImportMatchConfidence.Matched;
        MatchedBy = "seller";

        // Once it has a home the burst it came from is no longer interesting; keeping the key would
        // leave it grouped with photos it is no longer beside.
        if (ProductCode is not null) ClusterKey = null;
    }
}

internal enum ImportJobStatus
{
    Analysing = 1,
    NeedsReview = 2,
    Committing = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6
}

internal enum ImportRowOutcome
{
    Ok = 1,
    Error = 2
}

internal enum ImportMatchConfidence
{
    /// <summary>A folder or filename matched a product code or SKU outright.</summary>
    Matched = 1,

    /// <summary>Inferred — shown pre-filled but visibly a guess.</summary>
    Guessed = 2,

    Unmatched = 3
}
