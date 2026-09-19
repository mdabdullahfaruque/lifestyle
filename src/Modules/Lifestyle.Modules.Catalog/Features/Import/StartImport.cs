using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Internal;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Media.Contracts;
using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Features.Import;

/// <summary>
/// Accepts a filled-in sheet, parses it, matches the vendor's library images to it, and stages the
/// result for review (docs/08 §5).
/// <para>
/// Nothing is written to the catalogue here. The job is a staging area and the seller sees the
/// grid before anything becomes a product — which is what makes a mis-keyed spreadsheet a
/// correctable mistake rather than a rewritten shop.
/// </para>
/// </summary>
internal static class StartImport
{
    /// <summary>How long a staged job survives unreviewed. Docs/08 §5.1.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    private const long MaxSheetBytes = 10 * 1024 * 1024;

    /// <summary>
    /// How many <c>image_urls</c> one import may download. Fetching happens inside this request, so
    /// without a cap a sheet of a thousand links would hold the request open for an hour.
    /// </summary>
    private const int MaxFetchedUrls = 50;

    internal sealed class Handler(
        ICatalogDbContext db,
        ImportSchemaFactory schemas,
        IMediaModule media,
        IVendorsModule vendors,
        VendorScope scope,
        IClock clock)
        : IHandler<Handler.Command, Result<ImportJobResponse>>
    {
        internal sealed record Command(Guid CategoryId, IFormFile Sheet);

        public async Task<Result<ImportJobResponse>> Handle(Command command, CancellationToken ct)
        {
            var vendorId = scope.RequireVendorId();
            if (vendorId.IsFailure) return vendorId.Error;

            if (!await vendors.CanSellAsync(vendorId.Value, ct))
                return Error.Forbidden("catalog.vendor_not_approved",
                    "Your shop must be approved before you can add products.");

            var sheet = command.Sheet;

            if (sheet.Length == 0)
                return Error.Validation("catalog.import_empty", "That file is empty.");

            if (sheet.Length > MaxSheetBytes)
                return Error.Validation("catalog.import_too_large",
                    $"The sheet must be {MaxSheetBytes / (1024 * 1024)} MB or smaller.");

            var schema = await schemas.ForCategoryAsync(command.CategoryId, ct);
            if (schema.IsFailure) return schema.Error;

            SheetContent content;
            await using (var stream = sheet.OpenReadStream())
            {
                var read = SheetReader.Read(stream, sheet.FileName);
                if (read.IsFailure) return read.Error;

                content = read.Value;
            }

            if (content.Rows.Count == 0)
                return Error.Validation("catalog.import_no_rows", "That sheet has headers but no rows.");

            var job = ImportJob.Start(
                vendorId.Value, command.CategoryId, SafeName(sheet.FileName), clock.UtcNow, Lifetime);

            BuildRows(job, schema.Value, content);
            await MatchExistingProductsAsync(job, db, vendorId.Value, ct);
            await MatchImagesAsync(job, media, vendorId.Value, ct);
            await FetchImageUrlsAsync(job, media, vendorId.Value, ct);

            var ready = job.Ready(clock.UtcNow);
            if (ready.IsFailure) return ready.Error;

            db.ImportJobs.Add(job);
            await db.SaveChangesAsync(ct);

            var urls = await ImageUrlsAsync(job, media, ct);
            return job.ToResponse(urls);
        }

        /// <summary>
        /// Parses every row, then re-checks each product as a whole. A row can be perfectly valid on
        /// its own and still be wrong in company — two rows with the same options, for instance —
        /// so group failures mark every row of that product, not just the second one.
        /// </summary>
        private static void BuildRows(ImportJob job, ImportSchema schema, SheetContent content)
        {
            var parsed = new Dictionary<Guid, ParsedRow>();

            foreach (var sheetRow in content.Rows)
            {
                var values = sheetRow.Cells.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
                var result = ImportRowParser.Parse(schema, sheetRow);

                if (result.IsFailure)
                {
                    var rejected = ImportJobRow.Create(
                        job.Id, sheetRow.RowNumber,
                        sheetRow.Get(ImportColumns.ProductCode), sheetRow.Get(ImportColumns.Sku), values);

                    rejected.Reject(result.Error.Code, result.Error.Message);
                    job.AddRow(rejected);
                    continue;
                }

                var row = ImportJobRow.Create(
                    job.Id, sheetRow.RowNumber, result.Value.ProductCode, result.Value.Sku, values);

                row.Accept();
                job.AddRow(row);
                parsed[row.Id] = result.Value;
            }

            foreach (var group in job.Rows
                         .Where(r => r.Outcome == ImportRowOutcome.Ok && r.ProductCode is not null)
                         .GroupBy(r => r.ProductCode!, StringComparer.OrdinalIgnoreCase))
            {
                var rows = group.OrderBy(r => r.RowNumber).ToList();
                var check = ImportRowParser.ValidateGroup([.. rows.Select(r => parsed[r.Id])]);

                if (check.IsFailure)
                    foreach (var row in rows)
                        row.Reject(check.Error.Code, check.Error.Message);
            }
        }

        /// <summary>
        /// Links rows to products the vendor already has under the same code, and flags the ones
        /// that would take a live product off the storefront (docs/08 §9 D4).
        /// </summary>
        private static async Task MatchExistingProductsAsync(
            ImportJob job, ICatalogDbContext db, Guid vendorId, CancellationToken ct)
        {
            var codes = job.Rows
                .Where(r => r.ProductCode is not null)
                .Select(r => r.ProductCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (codes.Count == 0) return;

            var existing = await db.Products.AsNoTracking()
                .Where(p => p.VendorId == vendorId
                            && p.VendorProductCode != null
                            && codes.Contains(p.VendorProductCode))
                .Select(p => new { p.Id, p.VendorProductCode, p.Status })
                .ToListAsync(ct);

            if (existing.Count == 0) return;

            var byCode = existing.ToDictionary(
                p => p.VendorProductCode!, p => p, StringComparer.OrdinalIgnoreCase);

            foreach (var row in job.Rows.Where(r => r.ProductCode is not null))
            {
                if (!byCode.TryGetValue(row.ProductCode!, out var product)) continue;

                // A published or pending product goes back to moderation when it is edited
                // (Product.UpdateDetails), so a careless bulk update would empty a shop's
                // storefront. The seller has to say yes to that explicitly.
                var affectsLive = product.Status is ProductStatus.Published or ProductStatus.PendingReview;

                row.MatchTo(product.Id, affectsLive);
            }
        }

        private static async Task MatchImagesAsync(
            ImportJob job, IMediaModule media, Guid vendorId, CancellationToken ct)
        {
            var library = await media.ListForVendorAsync(vendorId, unusedOnly: true, ct);
            if (library.Count == 0) return;

            var codes = job.Rows
                .Where(r => r.ProductCode is not null)
                .Select(r => r.ProductCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Every pair, including repeats: a SKU that appears on two products must reach the
            // matcher as two pairs so it can refuse to guess between them (docs/08 §9 D2).
            var skus = job.Rows
                .Where(r => r.Sku is not null && r.ProductCode is not null)
                .Select(r => (Sku: r.Sku!, ProductCode: r.ProductCode!))
                .ToList();

            var matches = ImageMatcher.Match(
                [.. library.Select(a => new MatchCandidate(a.Id, a.FileName, a.CapturedAt))],
                codes, skus);

            foreach (var match in matches)
                job.AddImage(ImportJobImage.Create(
                    job.Id, match.MediaId, match.FileName, match.ProductCode,
                    match.Position, match.Confidence, match.MatchedBy, match.ClusterKey));
        }

        /// <summary>
        /// Downloads anything named in the <c>image_urls</c> column and attaches it to that row's
        /// product (docs/08 §7.2).
        /// <para>
        /// Off unless <c>Media:RemoteImageImport:Enabled</c> is set, in which case every URL
        /// returns a failure that is reported against the row rather than failing the import. A
        /// download that does not work must never cost the seller the rest of a good sheet — and
        /// the image library is the path that does not depend on someone else's server being up.
        /// </para>
        /// </summary>
        private static async Task FetchImageUrlsAsync(
            ImportJob job, IMediaModule media, Guid vendorId, CancellationToken ct)
        {
            var byProduct = job.Rows
                .Where(r => r.ProductCode is not null
                            && r.Values.TryGetValue(ImportColumns.ImageUrls, out var raw)
                            && !string.IsNullOrWhiteSpace(raw))
                .GroupBy(r => r.ProductCode!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (byProduct.Count == 0) return;

            var fetched = new List<string>();

            foreach (var group in byProduct)
            {
                // Product-level column, so only the first row of each product is read — the same
                // rule as name and description.
                var first = group.OrderBy(r => r.RowNumber).First();
                var urls = first.Values[ImportColumns.ImageUrls]
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var position = job.Images.Count(i =>
                    string.Equals(i.ProductCode, group.Key, StringComparison.OrdinalIgnoreCase));

                foreach (var url in urls)
                {
                    ct.ThrowIfCancellationRequested();

                    if (fetched.Count >= MaxFetchedUrls)
                    {
                        first.Reject("import.too_many_image_urls",
                            $"An import can download at most {MaxFetchedUrls} images by URL. "
                            + "Upload the rest to your image library instead.");
                        break;
                    }

                    var result = await media.ImportFromUrlAsync(url, ct);

                    if (result.IsFailure)
                    {
                        // The feature being switched off is a platform decision, not a mistake the
                        // seller made. Failing their rows for filling in a column the template
                        // offers them would be indefensible — the column is simply ignored, and
                        // they place the images from their library instead.
                        if (result.Error.Code == "media.url_import_disabled") return;

                        first.Reject(result.Error.Code, $"{url}: {result.Error.Message}");
                        break;
                    }

                    job.AddImage(ImportJobImage.Create(
                        job.Id, result.Value.Id, result.Value.FileName, group.Key,
                        position++, ImportMatchConfidence.Matched, "url", null));

                    fetched.Add(result.Value.Id);
                }
            }

            // Claimed for the library so a cancelled import leaves them reusable rather than
            // sweeping them and making the seller download everything again.
            if (fetched.Count > 0)
                await media.AttachAsync(fetched, MediaOwnerTypes.VendorLibrary, vendorId, ct);
        }

        internal static async Task<IReadOnlyDictionary<string, string>> ImageUrlsAsync(
            ImportJob job, IMediaModule media, CancellationToken ct)
        {
            var ids = job.Images.Select(i => i.MediaId).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);

            var assets = await media.GetManyAsync(ids, ct);

            return assets.ToDictionary(
                a => a.Id,
                a => a.Derivatives.TryGetValue(MediaVariants.Thumbnail, out var thumb) ? thumb : a.Url,
                StringComparer.Ordinal);
        }

        private static string SafeName(string fileName)
        {
            var name = Path.GetFileName(fileName ?? string.Empty);
            return string.IsNullOrWhiteSpace(name) ? "import" : name.Length > 255 ? name[..255] : name;
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/import", async (
                Guid categoryId, IFormFile sheet, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(categoryId, sheet), ct)).ToHttpResult())
            .WithName("StartImport")
            .WithSummary("Upload a filled-in import sheet. Stages the result for review; writes nothing yet.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .DisableAntiforgery()
            .Produces<ImportJobResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
