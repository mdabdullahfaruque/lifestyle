using FluentValidation;
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

namespace Lifestyle.Modules.Catalog.Features.Products;

internal static class UpdateProduct
{
    public sealed record Request(
        Guid CategoryId,
        string Name,
        string? Description,
        string? ShortDescription,
        string? Brand,
        IReadOnlyDictionary<string, string>? Attributes);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.CategoryId).NotEmpty();
            RuleFor(r => r.Name).NotEmpty().MaximumLength(300);
            RuleFor(r => r.Description).MaximumLength(20000);
            RuleFor(r => r.ShortDescription).MaximumLength(500);
            RuleFor(r => r.Brand).MaximumLength(150);
        }
    }

    internal sealed class Handler(ICatalogDbContext db, VendorScope scope, IClock clock)
        : IHandler<Handler.Command, Result<ProductResponse>>
    {
        internal sealed record Command(Guid ProductId, Request Request);

        public async Task<Result<ProductResponse>> Handle(Command command, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedProductAsync(command.ProductId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var product = loaded.Value;
            var request = command.Request;

            var category = await db.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct);

            if (category is null) return Error.NotFound("catalog.category_not_found");

            var canHold = category.EnsureCanHoldProducts();
            if (canHold.IsFailure) return canHold.Error;

            var attributes = await CreateProduct.Handler.LoadAttributesAsync(db, category.AttributeSetId, ct);
            var applied = CreateProduct.Handler.ApplyAttributes(product, attributes, request.Attributes);
            if (applied.IsFailure) return applied.Error;

            // UpdateDetails pushes a published product back to PendingReview — see Product.cs.
            product.UpdateDetails(request.CategoryId, request.Name, request.Description,
                request.ShortDescription, request.Brand, clock.UtcNow);

            await db.SaveChangesAsync(ct);
            return product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/{productId:guid}", async (
                Guid productId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(productId, request), ct)).ToHttpResult())
            .WithName("UpdateProduct")
            .WithSummary("Update a product. Editing a live product returns it to review.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .Validate<Request>()
            .Produces<ProductResponse>();
}

internal static class SetProductImages
{
    public sealed record Request(IReadOnlyList<ImageInput> Images);

    public sealed record ImageInput(string MediaId, string? AltText);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Images)
                .NotNull()
                .Must(i => i.Count <= 20).WithMessage("A product may have at most 20 images.");

            RuleForEach(r => r.Images).ChildRules(i =>
            {
                i.RuleFor(x => x.MediaId).NotEmpty().MaximumLength(64);
                i.RuleFor(x => x.AltText).MaximumLength(300);
            });
        }
    }

    internal sealed class Handler(ICatalogDbContext db, IMediaModule media, VendorScope scope, IClock clock)
        : IHandler<Handler.Command, Result<ProductResponse>>
    {
        internal sealed record Command(Guid ProductId, Request Request);

        public async Task<Result<ProductResponse>> Handle(Command command, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedProductAsync(command.ProductId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var product = loaded.Value;
            var now = clock.UtcNow;
            var desired = command.Request.Images;
            var desiredIds = desired.Select(i => i.MediaId).ToList();

            foreach (var existing in product.Images.Select(i => i.MediaId).Except(desiredIds, StringComparer.Ordinal).ToList())
                product.RemoveImage(existing, now);

            foreach (var image in desired)
                product.AddImage(image.MediaId, image.AltText, now);

            // The request order is the display order — no separate reorder call to forget.
            product.ReorderImages(desiredIds, now);

            await db.SaveChangesAsync(ct);

            if (desiredIds.Count > 0)
                await media.AttachAsync(desiredIds, "product", product.Id, ct);

            return product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/{productId:guid}/images", async (
                Guid productId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(productId, request), ct)).ToHttpResult())
            .WithName("SetProductImages")
            .WithSummary("Replace the product's image set. Request order is display order.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .Validate<Request>()
            .Produces<ProductResponse>();
}

internal static class DeleteProduct
{
    internal sealed class Handler(ICatalogDbContext db, VendorScope scope, ICurrentUser currentUser, IClock clock)
        : IHandler<Guid, Result>
    {
        public async Task<Result> Handle(Guid productId, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedProductAsync(productId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var product = loaded.Value;

            // Soft delete: a product may already appear in someone's order history or WhatsApp
            // conversation, and those references must still resolve.
            product.DeletedAt = clock.UtcNow;
            product.DeletedBy = currentUser.UserId;

            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete("/{productId:guid}", async (Guid productId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(productId, ct)).ToHttpResult())
            .WithName("DeleteProduct")
            .WithSummary("Soft-delete a product.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .Produces(StatusCodes.Status204NoContent);
}
