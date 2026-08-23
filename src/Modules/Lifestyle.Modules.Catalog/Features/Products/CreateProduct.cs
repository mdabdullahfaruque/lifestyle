using FluentValidation;
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
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Catalog.Features.Products;

/// <summary>
/// Creates a Draft product with its variants in one call. A product without variants is not
/// sellable, so making the caller create the shell and then add variants would just produce a
/// population of half-made products.
/// </summary>
internal static class CreateProduct
{
    public sealed record Request(
        Guid CategoryId,
        string Name,
        string? Description,
        string? ShortDescription,
        string? Brand,
        IReadOnlyList<VariantInput> Variants,
        IReadOnlyList<string>? ImageMediaIds,
        IReadOnlyDictionary<string, string>? Attributes);

    public sealed record VariantInput(
        string Sku,
        IReadOnlyDictionary<string, string> Options,
        decimal Price,
        decimal? CompareAtPrice,
        int StockQuantity);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.CategoryId).NotEmpty();
            RuleFor(r => r.Name).NotEmpty().MaximumLength(300);
            RuleFor(r => r.Description).MaximumLength(20000);
            RuleFor(r => r.ShortDescription).MaximumLength(500);
            RuleFor(r => r.Brand).MaximumLength(150);

            RuleFor(r => r.Variants)
                .NotEmpty().WithMessage("A product needs at least one variant.")
                .Must(v => v.Count <= 200).WithMessage("A product may have at most 200 variants.");

            RuleForEach(r => r.Variants).ChildRules(v =>
            {
                v.RuleFor(x => x.Sku).NotEmpty().MaximumLength(80);
                v.RuleFor(x => x.Price).GreaterThan(0);
                v.RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
            });

            RuleFor(r => r.ImageMediaIds)
                .Must(ids => ids is null || ids.Count <= 20)
                .WithMessage("A product may have at most 20 images.");
        }
    }

    internal sealed class Handler(
        ICatalogDbContext db,
        IVendorsModule vendors,
        IMediaModule media,
        VendorScope scope,
        ProductSlugFactory slugs,
        IClock clock,
        IOptions<CatalogModuleOptions> options)
        : IHandler<Request, Result<ProductResponse>>
    {
        public async Task<Result<ProductResponse>> Handle(Request request, CancellationToken ct)
        {
            var vendorId = scope.RequireVendorId();
            if (vendorId.IsFailure) return vendorId.Error;

            // Asked through the contract, not by joining the vendors schema (docs/04 §4.3).
            if (!await vendors.CanSellAsync(vendorId.Value, ct))
                return Error.Forbidden("catalog.vendor_not_approved",
                    "Your shop must be approved before you can add products.");

            var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct);
            if (category is null) return Error.NotFound("catalog.category_not_found");

            var canHold = category.EnsureCanHoldProducts();
            if (canHold.IsFailure) return canHold.Error;

            var slug = await slugs.CreateAsync(vendorId.Value, request.Name, null, ct);
            if (slug.IsFailure) return slug.Error;

            var product = Product.Create(
                vendorId.Value, request.CategoryId, request.Name, slug.Value,
                request.Description, request.ShortDescription, request.Brand,
                options.Value.Currency, clock.UtcNow);

            var attributes = await LoadAttributesAsync(db, category.AttributeSetId, ct);

            var applied = ApplyAttributes(product, attributes, request.Attributes);
            if (applied.IsFailure) return applied.Error;

            foreach (var input in request.Variants)
            {
                var axisCheck = ValidateAxes(attributes, input.Options);
                if (axisCheck.IsFailure) return axisCheck.Error;

                var added = product.AddVariant(input.Sku, input.Options, input.Price,
                    input.CompareAtPrice, input.StockQuantity, clock.UtcNow);

                if (added.IsFailure) return added.Error;
            }

            foreach (var mediaId in request.ImageMediaIds ?? [])
                product.AddImage(mediaId, null, clock.UtcNow);

            db.Products.Add(product);
            await db.SaveChangesAsync(ct);

            // Claim the uploads so the orphan sweeper leaves them alone.
            if (request.ImageMediaIds is { Count: > 0 })
                await media.AttachAsync(request.ImageMediaIds, "product", product.Id, ct);

            return product.ToResponse();
        }

        internal static async Task<IReadOnlyList<ProductAttribute>> LoadAttributesAsync(
            ICatalogDbContext db, Guid? attributeSetId, CancellationToken ct) =>
            attributeSetId is null
                ? []
                : await db.ProductAttributes.AsNoTracking()
                    .Where(a => a.AttributeSetId == attributeSetId)
                    .ToListAsync(ct);

        /// <summary>Validates and stores the product-level (non-variant) attribute values.</summary>
        internal static Result ApplyAttributes(
            Product product,
            IReadOnlyList<ProductAttribute> definitions,
            IReadOnlyDictionary<string, string>? supplied)
        {
            var values = supplied ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var attribute in definitions.Where(a => !a.IsVariantAxis))
            {
                values.TryGetValue(attribute.Code, out var value);

                var check = attribute.ValidateValue(value);
                if (check.IsFailure) return check;

                product.SetAttributeValue(attribute.Id, attribute.Code, value);
            }

            return Result.Success();
        }

        /// <summary>
        /// Every variant must supply exactly the category's variant axes — no more, no fewer.
        /// A missing axis makes two variants indistinguishable to a buyer; an extra one is a typo.
        /// </summary>
        internal static Result ValidateAxes(
            IReadOnlyList<ProductAttribute> definitions,
            IReadOnlyDictionary<string, string> options)
        {
            var axes = definitions.Where(a => a.IsVariantAxis).ToList();
            if (axes.Count == 0) return Result.Success();

            foreach (var axis in axes)
            {
                if (!options.TryGetValue(axis.Code, out var value) || string.IsNullOrWhiteSpace(value))
                    return Error.Validation($"catalog.axis_missing.{axis.Code}",
                        $"Every variant must specify {axis.Name}.");

                var check = axis.ValidateValue(value);
                if (check.IsFailure) return check;
            }

            var known = axes.Select(a => a.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = options.Keys.FirstOrDefault(k => !known.Contains(k));

            return unknown is not null
                ? Error.Validation("catalog.axis_unknown", $"'{unknown}' is not an option for this category.")
                : Result.Success();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToCreated(p => $"/v1/vendor/products/{p.Id}"))
            .WithName("CreateProduct")
            .WithSummary("Create a draft product with its variants.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .Validate<Request>()
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
