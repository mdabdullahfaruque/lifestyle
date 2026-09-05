using FluentValidation;
using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Features.Products;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Platform.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Paging;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Features.Admin;

/// <summary>
/// The product moderation queue. In the Catalog module, not an Admin module, because it reads
/// Catalog's tables (docs/04 §3.4).
/// </summary>
internal static class ListProductsForModeration
{
    public sealed record Request(string? Status, Guid? VendorId, int Page = 1, int PageSize = 20);

    internal sealed class Handler(ICatalogDbContext db)
        : IHandler<Request, Result<PagedResult<ProductListItemResponse>>>
    {
        public async Task<Result<PagedResult<ProductListItemResponse>>> Handle(Request request, CancellationToken ct)
        {
            var query = db.Products.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<ProductStatus>(request.Status, ignoreCase: true, out var status))
                    return Error.Validation("catalog.status_invalid", $"Unknown status '{request.Status}'.");

                query = query.Where(p => p.Status == status);
            }
            else
            {
                // Default to the thing a moderator actually opened this page for.
                query = query.Where(p => p.Status == ProductStatus.PendingReview);
            }

            if (request.VendorId is { } vendorId) query = query.Where(p => p.VendorId == vendorId);

            var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
            var total = await query.CountAsync(ct);

            var rows = await query
                .OrderBy(p => p.SubmittedAt ?? p.CreatedAt)
                .Skip(page.Skip)
                .Take(page.Size)
                .Select(p => new ProductListRow(
                    p.Id, p.VendorId, p.Name, p.Slug, p.Status, p.MinPrice, p.MaxPrice, p.Currency,
                    p.TotalStock,
                    p.Images.OrderBy(i => i.Position).Select(i => i.MediaId).FirstOrDefault(),
                    p.PublishedAt))
                .ToListAsync(ct);

            return PagedResult<ProductListItemResponse>.From([.. rows.Select(r => r.ToResponse())], page, total);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/products", async ([AsParameters] Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("ListProductsForModeration")
            .WithSummary("Products awaiting moderation, oldest submission first.")
            .RequirePermission(Permissions.Catalog.Moderate)
            .Produces<PagedResult<ProductListItemResponse>>();
}

internal static class GetProductForModeration
{
    internal sealed class Handler(ICatalogDbContext db) : IHandler<Guid, Result<ProductResponse>>
    {
        public async Task<Result<ProductResponse>> Handle(Guid productId, CancellationToken ct)
        {
            var product = await db.Products
                .Include(p => p.Variants)
                .Include(p => p.Images)
                .Include(p => p.AttributeValues)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productId, ct);

            return product is null ? Error.NotFound("catalog.product_not_found") : product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/products/{productId:guid}", async (Guid productId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(productId, ct)).ToHttpResult())
            .WithName("GetProductForModeration")
            .WithSummary("Any product, in any state, for review.")
            .RequirePermission(Permissions.Catalog.Moderate)
            .Produces<ProductResponse>();
}

internal static class ModerateProduct
{
    public sealed record Request(bool Approve, string? Note);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() =>
            RuleFor(r => r.Note)
                .NotEmpty()
                .When(r => !r.Approve)
                .WithMessage("A rejection must say why — the vendor sees this.")
                .MaximumLength(1000);
    }

    internal sealed class Handler(ICatalogDbContext db, IAuditLog audit, IClock clock)
        : IHandler<Handler.Command, Result<ProductResponse>>
    {
        internal sealed record Command(Guid ProductId, Request Request);

        public async Task<Result<ProductResponse>> Handle(Command command, CancellationToken ct)
        {
            var product = await db.Products
                .Include(p => p.Variants)
                .Include(p => p.Images)
                .Include(p => p.AttributeValues)
                .FirstOrDefaultAsync(p => p.Id == command.ProductId, ct);

            if (product is null) return Error.NotFound("catalog.product_not_found");

            var result = command.Request.Approve
                ? product.Approve(clock.UtcNow)
                : product.Reject(command.Request.Note!, clock.UtcNow);

            if (result.IsFailure) return result.Error;

            audit.Record(
                command.Request.Approve ? "product.approved" : "product.rejected",
                "product", product.Id,
                new { product.Name, product.VendorId, command.Request.Note });

            await db.SaveChangesAsync(ct);
            return product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/products/{productId:guid}/moderate", async (
                Guid productId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(productId, request), ct)).ToHttpResult())
            .WithName("ModerateProduct")
            .WithSummary("Approve a product for sale, or reject it with a reason.")
            .RequirePermission(Permissions.Catalog.Moderate)
            .Validate<Request>()
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}

internal static class TakeDownProduct
{
    public sealed record Request(string Reason);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
    }

    internal sealed class Handler(ICatalogDbContext db, IAuditLog audit, IClock clock)
        : IHandler<Handler.Command, Result<ProductResponse>>
    {
        internal sealed record Command(Guid ProductId, Request Request);

        public async Task<Result<ProductResponse>> Handle(Command command, CancellationToken ct)
        {
            var product = await db.Products
                .Include(p => p.Variants)
                .Include(p => p.Images)
                .Include(p => p.AttributeValues)
                .FirstOrDefaultAsync(p => p.Id == command.ProductId, ct);

            if (product is null) return Error.NotFound("catalog.product_not_found");

            var result = product.Unpublish(command.Request.Reason, clock.UtcNow);
            if (result.IsFailure) return result.Error;

            audit.Record("product.taken_down", "product", product.Id,
                new { product.Name, product.VendorId, command.Request.Reason });

            await db.SaveChangesAsync(ct);
            return product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/products/{productId:guid}/takedown", async (
                Guid productId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(productId, request), ct)).ToHttpResult())
            .WithName("TakeDownProduct")
            .WithSummary("Remove a live product from sale.")
            .RequirePermission(Permissions.Catalog.Moderate)
            .Validate<Request>()
            .Produces<ProductResponse>();
}
