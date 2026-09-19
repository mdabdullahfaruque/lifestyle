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

namespace Lifestyle.Modules.Catalog.Features.Import;

/// <summary>Reads back a staged import — the data behind the review grid.</summary>
internal static class GetImportJob
{
    internal sealed class Handler(VendorScope scope, IMediaModule media)
        : IHandler<Handler.Command, Result<ImportJobResponse>>
    {
        internal sealed record Command(Guid JobId);

        public async Task<Result<ImportJobResponse>> Handle(Command command, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedImportJobAsync(command.JobId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var urls = await StartImport.Handler.ImageUrlsAsync(loaded.Value, media, ct);
            return loaded.Value.ToResponse(urls);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/import/{jobId:guid}", async (Guid jobId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(jobId), ct)).ToHttpResult())
            .WithName("GetImportJob")
            .WithSummary("The staged import: products, rows, matched images and what still needs a decision.")
            .RequirePermission(Permissions.Catalog.ReadOwn)
            .Produces<ImportJobResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>
/// The seller's corrections from the review grid: images dragged onto the right product, rows
/// skipped, and explicit consent for anything that would pull a live product off the storefront.
/// </summary>
internal static class ReviseImportJob
{
    public sealed record Request(
        IReadOnlyList<ImageAssignment>? Images,
        IReadOnlyList<RowDecision>? Rows,
        IReadOnlyList<Guid>? ConfirmLiveUpdates);

    public sealed record ImageAssignment(Guid ImageId, string? ProductCode, int Position);

    public sealed record RowDecision(Guid RowId, bool Skip);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleForEach(r => r.Images).ChildRules(i =>
            {
                i.RuleFor(x => x.ImageId).NotEmpty();
                i.RuleFor(x => x.ProductCode).MaximumLength(64);
                i.RuleFor(x => x.Position).GreaterThanOrEqualTo(0);
            });

            RuleForEach(r => r.Rows).ChildRules(r => r.RuleFor(x => x.RowId).NotEmpty());
        }
    }

    internal sealed class Handler(ICatalogDbContext db, VendorScope scope, IMediaModule media, IClock clock)
        : IHandler<Handler.Command, Result<ImportJobResponse>>
    {
        internal sealed record Command(Guid JobId, Request Request);

        public async Task<Result<ImportJobResponse>> Handle(Command command, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedImportJobAsync(command.JobId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var job = loaded.Value;
            var now = clock.UtcNow;
            var request = command.Request;

            foreach (var assignment in request.Images ?? [])
            {
                var applied = job.AssignImage(assignment.ImageId, assignment.ProductCode, assignment.Position, now);
                if (applied.IsFailure) return applied.Error;
            }

            var skip = (request.Rows ?? []).ToDictionary(r => r.RowId, r => r.Skip);
            var revised = job.Revise(skip, request.ConfirmLiveUpdates ?? [], now);
            if (revised.IsFailure) return revised.Error;

            await db.SaveChangesAsync(ct);

            var urls = await StartImport.Handler.ImageUrlsAsync(job, media, ct);
            return job.ToResponse(urls);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPatch("/import/{jobId:guid}", async (
                Guid jobId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(jobId, request), ct)).ToHttpResult())
            .WithName("ReviseImportJob")
            .WithSummary("Apply the review grid's corrections. Still writes nothing to the catalogue.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .Validate<Request>()
            .Produces<ImportJobResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>Abandons a staged import. Its images fall back to the library, unharmed.</summary>
internal static class CancelImportJob
{
    internal sealed class Handler(ICatalogDbContext db, VendorScope scope, IClock clock)
        : IHandler<Handler.Command, Result>
    {
        internal sealed record Command(Guid JobId);

        public async Task<Result> Handle(Command command, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedImportJobAsync(command.JobId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var cancelled = loaded.Value.Cancel(clock.UtcNow);
            if (cancelled.IsFailure) return cancelled.Error;

            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete("/import/{jobId:guid}", async (Guid jobId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(jobId), ct)).ToHttpResult())
            .WithName("CancelImportJob")
            .WithSummary("Cancel a staged import.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
