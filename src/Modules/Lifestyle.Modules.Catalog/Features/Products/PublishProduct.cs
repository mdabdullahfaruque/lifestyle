using Lifestyle.Modules.Catalog.Internal;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lifestyle.Modules.Catalog.Features.Products;

/// <summary>
/// Vendor submits a draft for moderation (FRD §7.5). It does not go live here — a moderator
/// approves it. That is the whole point of the review step.
/// </summary>
internal static class SubmitProductForReview
{
    internal sealed class Handler(ICatalogDbContext db, VendorScope scope, IClock clock)
        : IHandler<Guid, Result<ProductResponse>>
    {
        public async Task<Result<ProductResponse>> Handle(Guid productId, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedProductAsync(productId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var product = loaded.Value;
            var submitted = product.SubmitForReview(clock.UtcNow);
            if (submitted.IsFailure) return submitted.Error;

            await db.SaveChangesAsync(ct);
            return product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/{productId:guid}/submit", async (Guid productId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(productId, ct)).ToHttpResult())
            .WithName("SubmitProductForReview")
            .WithSummary("Submit a draft product for moderation.")
            .RequirePermission(Permissions.Catalog.PublishOwn)
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
}

/// <summary>Vendor takes their own live product down — no moderator needed to stop selling.</summary>
internal static class UnpublishOwnProduct
{
    internal sealed class Handler(ICatalogDbContext db, VendorScope scope, IClock clock)
        : IHandler<Guid, Result<ProductResponse>>
    {
        public async Task<Result<ProductResponse>> Handle(Guid productId, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedProductAsync(productId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var product = loaded.Value;
            var result = product.Unpublish("vendor_request", clock.UtcNow);
            if (result.IsFailure) return result.Error;

            await db.SaveChangesAsync(ct);
            return product.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/{productId:guid}/unpublish", async (Guid productId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(productId, ct)).ToHttpResult())
            .WithName("UnpublishOwnProduct")
            .WithSummary("Take one of your own live products off sale.")
            .RequirePermission(Permissions.Catalog.PublishOwn)
            .Produces<ProductResponse>();
}
