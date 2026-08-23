using FluentValidation;
using Lifestyle.Modules.Catalog.Features.Products;
using Lifestyle.Modules.Catalog.Internal;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Features.Inventory;

internal static class AddVariant
{
    public sealed record Request(
        string Sku,
        IReadOnlyDictionary<string, string> Options,
        decimal Price,
        decimal? CompareAtPrice,
        int StockQuantity);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Sku).NotEmpty().MaximumLength(80);
            RuleFor(r => r.Price).GreaterThan(0);
            RuleFor(r => r.StockQuantity).GreaterThanOrEqualTo(0);
            RuleFor(r => r.Options).NotNull();
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

            var category = await db.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == product.CategoryId, ct);

            var attributes = await CreateProduct.Handler.LoadAttributesAsync(db, category?.AttributeSetId, ct);

            var axisCheck = CreateProduct.Handler.ValidateAxes(attributes, command.Request.Options);
            if (axisCheck.IsFailure) return axisCheck.Error;

            var added = product.AddVariant(
                command.Request.Sku, command.Request.Options, command.Request.Price,
                command.Request.CompareAtPrice, command.Request.StockQuantity, clock.UtcNow);

            if (added.IsFailure) return added.Error;

            await db.SaveChangesAsync(ct);
            return product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/{productId:guid}/variants", async (
                Guid productId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(productId, request), ct)).ToHttpResult())
            .WithName("AddVariant")
            .WithSummary("Add a variant to a product.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .Validate<Request>()
            .Produces<ProductResponse>();
}

internal static class UpdateVariant
{
    public sealed record Request(decimal Price, decimal? CompareAtPrice, int StockQuantity, bool IsActive);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Price).GreaterThan(0);
            RuleFor(r => r.StockQuantity).GreaterThanOrEqualTo(0);
        }
    }

    internal sealed class Handler(ICatalogDbContext db, VendorScope scope, IClock clock)
        : IHandler<Handler.Command, Result<ProductResponse>>
    {
        internal sealed record Command(Guid ProductId, Guid VariantId, Request Request);

        public async Task<Result<ProductResponse>> Handle(Command command, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedProductAsync(command.ProductId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var product = loaded.Value;
            var updated = product.UpdateVariant(command.VariantId, command.Request.Price,
                command.Request.CompareAtPrice, command.Request.StockQuantity, command.Request.IsActive, clock.UtcNow);

            if (updated.IsFailure) return updated.Error;

            await db.SaveChangesAsync(ct);
            return product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/{productId:guid}/variants/{variantId:guid}", async (
                Guid productId, Guid variantId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(productId, variantId, request), ct)).ToHttpResult())
            .WithName("UpdateVariant")
            .WithSummary("Update a variant's price, stock and availability.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .Validate<Request>()
            .Produces<ProductResponse>();
}

internal static class RemoveVariant
{
    internal sealed class Handler(ICatalogDbContext db, VendorScope scope, IClock clock)
        : IHandler<Handler.Command, Result<ProductResponse>>
    {
        internal sealed record Command(Guid ProductId, Guid VariantId);

        public async Task<Result<ProductResponse>> Handle(Command command, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedProductAsync(command.ProductId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var product = loaded.Value;
            var removed = product.RemoveVariant(command.VariantId, clock.UtcNow);
            if (removed.IsFailure) return removed.Error;

            await db.SaveChangesAsync(ct);
            return product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete("/{productId:guid}/variants/{variantId:guid}", async (
                Guid productId, Guid variantId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(productId, variantId), ct)).ToHttpResult())
            .WithName("RemoveVariant")
            .WithSummary("Remove a variant. A product must keep at least one.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .Produces<ProductResponse>();
}

/// <summary>
/// Bulk stock adjustment — the one-tap update the v1 stock-accuracy risk (Plan §6.2, R13) depends
/// on. Takes deltas rather than absolutes so two concurrent adjustments both apply.
/// </summary>
internal static class AdjustStock
{
    public sealed record Request(IReadOnlyList<Adjustment> Adjustments);

    public sealed record Adjustment(Guid VariantId, int Delta);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Adjustments)
                .NotEmpty()
                .Must(a => a.Count <= 200).WithMessage("At most 200 adjustments per request.");

            RuleForEach(r => r.Adjustments).ChildRules(a =>
            {
                a.RuleFor(x => x.VariantId).NotEmpty();
                a.RuleFor(x => x.Delta).NotEqual(0).WithMessage("A delta of zero changes nothing.");
            });
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

            // All-or-nothing: a partial stock update is worse than a rejected one, because the
            // vendor believes the whole batch applied.
            foreach (var adjustment in command.Request.Adjustments)
            {
                var result = product.AdjustStock(adjustment.VariantId, adjustment.Delta, clock.UtcNow);
                if (result.IsFailure) return result.Error;
            }

            await db.SaveChangesAsync(ct);
            return product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/{productId:guid}/stock", async (
                Guid productId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(productId, request), ct)).ToHttpResult())
            .WithName("AdjustStock")
            .WithSummary("Apply stock deltas to one or more variants of a product.")
            .RequirePermission(Permissions.Inventory.WriteOwn)
            .Validate<Request>()
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
