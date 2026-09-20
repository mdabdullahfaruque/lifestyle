using FluentValidation;
using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Features.Products;
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
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Catalog.Features.Import;

/// <summary>
/// Turns groups of library photos into draft products — the images-first lane of docs/08 §1.1.
/// <para>
/// This is the path for a seller who photographs stock before writing anything down: upload the
/// shoot, drag the photos into piles, name each pile, done. No spreadsheet is involved at any
/// point. The sheet importer exists for sellers who already have a supplier catalogue, and
/// neither path is a prerequisite for the other.
/// </para>
/// <para>
/// Everything lands as <see cref="ProductStatus.Draft"/>, exactly like the sheet importer: the
/// completeness rules live on the aggregate and publishing still needs a moderator.
/// </para>
/// </summary>
internal static class CreateProductsFromImages
{
    public sealed record Request(Guid CategoryId, IReadOnlyList<ProductGroup> Products);

    /// <summary>One pile of photos the seller grouped, plus what they typed on it.</summary>
    public sealed record ProductGroup(
        string Name,
        decimal Price,
        string? Sku,
        int StockQuantity,
        string? Description,
        IReadOnlyList<string> MediaIds);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.CategoryId).NotEmpty();

            RuleFor(r => r.Products)
                .NotEmpty().WithMessage("Group at least one set of photos into a product.")
                .Must(p => p.Count <= 100).WithMessage("Create at most 100 products at a time.");

            RuleForEach(r => r.Products).ChildRules(p =>
            {
                p.RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
                p.RuleFor(x => x.Price).GreaterThan(0);
                p.RuleFor(x => x.Sku).MaximumLength(80);
                p.RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
                p.RuleFor(x => x.Description).MaximumLength(20000);

                p.RuleFor(x => x.MediaIds)
                    .NotEmpty().WithMessage("Every product needs at least one photo.")
                    .Must(m => m.Count <= 20).WithMessage("A product may have at most 20 images.");
            });
        }
    }

    public sealed record CreatedProduct(Guid Id, string Name, string Slug, int ImageCount);

    public sealed record Response(int Created, IReadOnlyList<CreatedProduct> Products);

    internal sealed class Handler(
        ICatalogDbContext db,
        IVendorsModule vendors,
        IMediaModule media,
        VendorScope scope,
        ProductSlugFactory slugs,
        IClock clock,
        IOptions<CatalogModuleOptions> options)
        : IHandler<Request, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Request request, CancellationToken ct)
        {
            var vendorId = scope.RequireVendorId();
            if (vendorId.IsFailure) return vendorId.Error;

            if (!await vendors.CanSellAsync(vendorId.Value, ct))
                return Error.Forbidden("catalog.vendor_not_approved",
                    "Your shop must be approved before you can add products.");

            var category = await db.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct);

            if (category is null) return Error.NotFound("catalog.category_not_found");

            var canHold = category.EnsureCanHoldProducts();
            if (canHold.IsFailure) return canHold.Error;

            // A photo may only sit on one product. Letting the same image back two products would
            // look like a successful import and leave the seller wondering why two listings share
            // a picture.
            var duplicate = request.Products
                .SelectMany(p => p.MediaIds)
                .GroupBy(id => id, StringComparer.Ordinal)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate is not null)
                return Error.Validation("catalog.image_reused",
                    "The same photo is on more than one product. Move it to just one.");

            var definitions = await CreateProduct.Handler.LoadAttributesAsync(db, category.AttributeSetId, ct);

            // A variant axis cannot be answered from a photograph. Refusing up front beats creating
            // drafts the seller then discovers they cannot submit.
            var requiredAxis = definitions.FirstOrDefault(a => a.IsVariantAxis && a.IsRequired);

            if (requiredAxis is not null)
                return Error.Validation("catalog.axis_required",
                    $"Products in {category.Name} need a {requiredAxis.Name} on every variant, which "
                    + "photos alone cannot supply. Use the spreadsheet import for this category.");

            var now = clock.UtcNow;
            var created = new List<CreatedProduct>(request.Products.Count);
            var attachments = new Dictionary<Guid, IReadOnlyList<string>>();

            foreach (var group in request.Products)
            {
                var slug = await slugs.CreateAsync(vendorId.Value, group.Name, null, ct);
                if (slug.IsFailure) return slug.Error;

                var product = Product.Create(
                    vendorId.Value, request.CategoryId, group.Name, slug.Value,
                    group.Description, null, null, options.Value.Currency, now);

                // One variant, because one pile of photos is one thing to sell. A seller who needs
                // sizes or colours adds them in the product editor afterwards.
                var sku = string.IsNullOrWhiteSpace(group.Sku)
                    ? slug.Value.ToUpperInvariant()
                    : group.Sku.Trim();

                var added = product.AddVariant(
                    sku, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    group.Price, null, group.StockQuantity, now);

                if (added.IsFailure) return added.Error;

                foreach (var mediaId in group.MediaIds)
                    product.AddImage(mediaId, null, now);

                db.Products.Add(product);
                attachments[product.Id] = group.MediaIds;
                created.Add(new CreatedProduct(product.Id, product.Name, product.Slug, group.MediaIds.Count));
            }

            await db.SaveChangesAsync(ct);

            // After the catalogue write, so a photo is never claimed for a product that failed to
            // save. Attach is idempotent, so a retry costs nothing.
            foreach (var (productId, mediaIds) in attachments)
                await media.AttachAsync(mediaIds, MediaOwnerTypes.Product, productId, ct);

            return new Response(created.Count, created);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/from-images", async (Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("CreateProductsFromImages")
            .WithSummary("Create draft products from grouped library photos. No spreadsheet involved.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .Validate<Request>()
            .Produces<Response>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
