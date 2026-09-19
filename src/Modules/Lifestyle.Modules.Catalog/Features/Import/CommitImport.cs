using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Features.Products;
using Lifestyle.Modules.Catalog.Internal;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Media.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Catalog.Features.Import;

/// <summary>
/// Turns a reviewed import into draft products (docs/08 §5.3).
/// <para>
/// Everything lands as <see cref="ProductStatus.Draft"/>. The importer deliberately has no
/// completeness rules of its own: <c>SubmitForReview</c> already refuses a product with no
/// variants, no images or no description, and publishing still needs a moderator. An import that
/// could publish would be a way around moderation for precisely the bulk case moderation exists
/// for.
/// </para>
/// </summary>
internal static class CommitImport
{
    internal sealed class Handler(
        ICatalogDbContext db,
        ImportSchemaFactory schemas,
        IMediaModule media,
        VendorScope scope,
        ProductSlugFactory slugs,
        IClock clock,
        IOptions<CatalogModuleOptions> options)
        : IHandler<Handler.Command, Result<ImportJobResponse>>
    {
        internal sealed record Command(Guid JobId);

        public async Task<Result<ImportJobResponse>> Handle(Command command, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedImportJobAsync(command.JobId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var job = loaded.Value;
            var now = clock.UtcNow;

            // Moves the job to Committing before any writing, so a double-clicked commit cannot
            // run twice and create every product in the sheet a second time.
            var began = job.BeginCommit(now);
            if (began.IsFailure) return began.Error;

            var schema = await schemas.ForCategoryAsync(job.CategoryId, ct);
            if (schema.IsFailure)
            {
                job.Fail(schema.Error.Message, now);
                await db.SaveChangesAsync(ct);
                return schema.Error;
            }

            var definitions = await CreateProduct.Handler.LoadAttributesAsync(db, await AttributeSetIdAsync(job, ct), ct);

            var created = 0;
            var updated = 0;
            var attachments = new Dictionary<Guid, List<string>>();

            foreach (var group in job.Rows
                         .Where(r => r.IsImportable && r.ProductCode is not null)
                         .GroupBy(r => r.ProductCode!, StringComparer.OrdinalIgnoreCase))
            {
                var rows = group.OrderBy(r => r.RowNumber).ToList();

                // Re-parsed from the stored cells rather than carried over from upload: one parser,
                // so the grid's verdict and what is written here cannot drift apart.
                var parsed = new List<ParsedRow>(rows.Count);

                foreach (var row in rows)
                {
                    var result = ImportRowParser.Parse(schema.Value, new SheetRow(row.RowNumber, row.Values));

                    if (result.IsFailure)
                    {
                        row.Reject(result.Error.Code, result.Error.Message);
                        continue;
                    }

                    parsed.Add(result.Value);
                }

                if (parsed.Count == 0) continue;

                var outcome = await UpsertAsync(job, group.Key, parsed, rows, definitions, now, ct);
                if (outcome.IsFailure)
                {
                    foreach (var row in rows) row.Reject(outcome.Error.Code, outcome.Error.Message);
                    continue;
                }

                var (product, isNew) = outcome.Value;

                if (isNew) created++;
                else updated++;

                var images = job.Images
                    .Where(i => string.Equals(i.ProductCode, group.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(i => i.Position)
                    .ToList();

                foreach (var image in images)
                    product.AddImage(image.MediaId, null, now);

                if (images.Count > 0)
                    attachments[product.Id] = [.. images.Select(i => i.MediaId)];
            }

            job.Complete(created, updated, now);
            await db.SaveChangesAsync(ct);

            // After the catalogue write, so an image is never claimed for a product that failed to
            // save. Attach is idempotent, so a retry here costs nothing.
            foreach (var (productId, mediaIds) in attachments)
                await media.AttachAsync(mediaIds, MediaOwnerTypes.Product, productId, ct);

            var urls = await StartImport.Handler.ImageUrlsAsync(job, media, ct);
            return job.ToResponse(urls);
        }

        private async Task<Guid?> AttributeSetIdAsync(ImportJob job, CancellationToken ct) =>
            await db.Categories.AsNoTracking()
                .Where(c => c.Id == job.CategoryId)
                .Select(c => c.AttributeSetId)
                .FirstOrDefaultAsync(ct);

        private async Task<Result<(Product Product, bool IsNew)>> UpsertAsync(
            ImportJob job,
            string productCode,
            List<ParsedRow> parsed,
            IReadOnlyList<ImportJobRow> rows,
            IReadOnlyList<ProductAttribute> definitions,
            DateTimeOffset now,
            CancellationToken ct)
        {
            var first = parsed[0];
            var existingId = rows.Select(r => r.TargetProductId).FirstOrDefault(id => id is not null);

            var product = existingId is { } id
                ? await db.Products
                    .Include(p => p.Variants)
                    .Include(p => p.Images)
                    .Include(p => p.AttributeValues)
                    .FirstOrDefaultAsync(p => p.Id == id, ct)
                : null;

            var isNew = product is null;

            if (product is null)
            {
                var slug = await slugs.CreateAsync(job.VendorId, first.Name!, null, ct);
                if (slug.IsFailure) return slug.Error;

                product = Product.Create(
                    job.VendorId, job.CategoryId, first.Name!, slug.Value,
                    first.Description, first.ShortDescription, first.Brand,
                    options.Value.Currency, now);

                product.SetVendorProductCode(productCode, now);
                db.Products.Add(product);
            }
            else
            {
                product.UpdateDetails(
                    job.CategoryId, first.Name!, first.Description, first.ShortDescription, first.Brand, now);
            }

            var applied = CreateProduct.Handler.ApplyAttributes(product, definitions, first.Attributes);
            if (applied.IsFailure) return applied.Error;

            foreach (var row in parsed)
            {
                var axisCheck = CreateProduct.Handler.ValidateAxes(definitions, row.Axes);
                if (axisCheck.IsFailure) return axisCheck.Error;

                var existingVariant = product.Variants
                    .FirstOrDefault(v => string.Equals(v.Sku, row.Sku, StringComparison.OrdinalIgnoreCase));

                if (existingVariant is null)
                {
                    var added = product.AddVariant(
                        row.Sku, row.Axes, row.Price, row.CompareAtPrice, row.Stock, now);

                    if (added.IsFailure) return added.Error;
                }
                else
                {
                    // Variants absent from the sheet are deliberately left alone. Removing them
                    // would make a partial sheet — one the seller uploaded to change prices —
                    // silently delete the SKUs they did not mention.
                    var changed = product.UpdateVariant(
                        existingVariant.Id, row.Price, row.CompareAtPrice, row.Stock, isActive: true, now);

                    if (changed.IsFailure) return changed.Error;
                }
            }

            return (product, isNew);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/import/{jobId:guid}/commit", async (
                Guid jobId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(jobId), ct)).ToHttpResult())
            .WithName("CommitImport")
            .WithSummary("Create or update the draft products from a reviewed import.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .Produces<ImportJobResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}
