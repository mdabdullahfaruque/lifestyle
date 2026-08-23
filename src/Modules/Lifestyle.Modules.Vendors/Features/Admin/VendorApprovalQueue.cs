using FluentValidation;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Vendors.Domain;
using Lifestyle.Modules.Vendors.Features.Vendors;
using Lifestyle.Modules.Vendors.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Paging;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Vendors.Features.Admin;

/// <summary>
/// The super-admin approval queue (Plan Phase 1). Lives in the Vendors module because it reads
/// vendor tables — there is no separate Admin module (docs/04 §3.4).
/// </summary>
internal static class ListVendorsForAdmin
{
    public sealed record Request(string? Status, string? Search, int Page = 1, int PageSize = 20);

    internal sealed class Handler(IVendorsDbContext db)
        : IHandler<Request, Result<PagedResult<VendorListItemResponse>>>
    {
        public async Task<Result<PagedResult<VendorListItemResponse>>> Handle(Request request, CancellationToken ct)
        {
            var query = db.Vendors.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<VendorStatus>(request.Status, ignoreCase: true, out var status))
                    return Error.Validation("vendors.status_invalid", $"Unknown status '{request.Status}'.");

                query = query.Where(v => v.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                // EF.Functions.Like, not Npgsql's ILike: a module must not take a compile-time
                // dependency on the database provider. Slug is citext so it matches case-insensitively
                // already; the two name columns are lowered explicitly. This is an admin-only search
                // over a small table, so the unindexed scan is acceptable.
                var term = $"%{request.Search.Trim().ToLowerInvariant()}%";

                // CA1304/CA1311: ToLower() here is never executed in .NET — it is part of an
                // expression tree and EF translates it to SQL lower(). ToLowerInvariant() has no
                // translation, so the culture-aware overloads the analyser wants are not options.
#pragma warning disable CA1304, CA1311
                query = query.Where(v =>
                    EF.Functions.Like(v.DisplayName.ToLower(), term) ||
                    EF.Functions.Like(v.LegalName.ToLower(), term) ||
                    EF.Functions.Like(v.Slug, term));
#pragma warning restore CA1304, CA1311
            }

            var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
            var total = await query.CountAsync(ct);

            var items = await query
                // Oldest submission first: a queue that reorders itself is a queue people distrust.
                .OrderBy(v => v.SubmittedAt ?? v.CreatedAt)
                .Skip(page.Skip)
                .Take(page.Size)
                .Select(v => new VendorListItemResponse(
                    v.Id, v.DisplayName, v.LegalName, v.Slug, v.Status.ToString(), v.SubmittedAt, v.CreatedAt))
                .ToListAsync(ct);

            return PagedResult<VendorListItemResponse>.From(items, page, total);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/", async ([AsParameters] Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("ListVendorsForAdmin")
            .WithSummary("The vendor approval queue, filterable by status.")
            .RequirePermission(Permissions.Vendors.Read)
            .Produces<PagedResult<VendorListItemResponse>>();
}

internal static class GetVendorForAdmin
{
    internal sealed class Handler(IVendorsDbContext db) : IHandler<Guid, Result<VendorResponse>>
    {
        public async Task<Result<VendorResponse>> Handle(Guid vendorId, CancellationToken ct)
        {
            var vendor = await db.Vendors
                .Include(v => v.Documents)
                .Include(v => v.Staff)
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == vendorId, ct);

            return vendor is null ? Error.NotFound("vendors.not_found") : vendor.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{vendorId:guid}", async (Guid vendorId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(vendorId, ct)).ToHttpResult())
            .WithName("GetVendorForAdmin")
            .WithSummary("Full vendor record including KYC documents, for review.")
            .RequirePermission(Permissions.Vendors.Read)
            .Produces<VendorResponse>();
}

internal static class ReviewVendorApplication
{
    public sealed record Request(bool Approve, string? Reason);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() =>
            RuleFor(r => r.Reason)
                .NotEmpty()
                .When(r => !r.Approve)
                .WithMessage("A rejection must say why — the applicant sees this.")
                .MaximumLength(1000);
    }

    internal sealed class Handler(
        IVendorsDbContext db,
        IIdentityModule identity,
        ICurrentUser currentUser,
        IClock clock)
        : IHandler<Handler.Command, Result<VendorResponse>>
    {
        internal sealed record Command(Guid VendorId, Request Request);

        public async Task<Result<VendorResponse>> Handle(Command command, CancellationToken ct)
        {
            if (currentUser.UserId is not { } adminId)
                return Error.Unauthorized("identity.not_authenticated");

            var vendor = await db.Vendors
                .Include(v => v.Documents)
                .Include(v => v.Staff)
                .FirstOrDefaultAsync(v => v.Id == command.VendorId, ct);

            if (vendor is null) return Error.NotFound("vendors.not_found");

            var result = command.Request.Approve
                ? vendor.Approve(adminId, clock.UtcNow)
                : vendor.Reject(adminId, command.Request.Reason!, clock.UtcNow);

            if (result.IsFailure) return result.Error;

            await db.SaveChangesAsync(ct);

            // Grant the seller-surface role only once the vendor is real. Doing this through the
            // Identity contract keeps role storage inside the module that owns it.
            if (command.Request.Approve)
                await identity.GrantVendorRoleAsync(vendor.OwnerUserId, vendor.Id, VendorRoleKind.Owner, ct);

            return vendor.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/{vendorId:guid}/review", async (
                Guid vendorId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(vendorId, request), ct)).ToHttpResult())
            .WithName("ReviewVendorApplication")
            .WithSummary("Approve or reject a pending vendor application.")
            .RequirePermission(Permissions.Vendors.Approve)
            .Validate<Request>()
            .Produces<VendorResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}

internal static class SetVendorSuspension
{
    public sealed record Request(bool Suspend, string? Reason);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() =>
            RuleFor(r => r.Reason).NotEmpty().When(r => r.Suspend).MaximumLength(1000);
    }

    internal sealed class Handler(IVendorsDbContext db, ICurrentUser currentUser, IClock clock)
        : IHandler<Handler.Command, Result<VendorResponse>>
    {
        internal sealed record Command(Guid VendorId, Request Request);

        public async Task<Result<VendorResponse>> Handle(Command command, CancellationToken ct)
        {
            if (currentUser.UserId is not { } adminId)
                return Error.Unauthorized("identity.not_authenticated");

            var vendor = await db.Vendors
                .Include(v => v.Documents)
                .Include(v => v.Staff)
                .FirstOrDefaultAsync(v => v.Id == command.VendorId, ct);

            if (vendor is null) return Error.NotFound("vendors.not_found");

            var result = command.Request.Suspend
                ? vendor.Suspend(adminId, command.Request.Reason!, clock.UtcNow)
                : vendor.Reinstate(clock.UtcNow);

            if (result.IsFailure) return result.Error;

            await db.SaveChangesAsync(ct);
            return vendor.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/{vendorId:guid}/suspension", async (
                Guid vendorId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(vendorId, request), ct)).ToHttpResult())
            .WithName("SetVendorSuspension")
            .WithSummary("Suspend or reinstate an approved vendor. Suspension unpublishes its catalogue.")
            .RequirePermission(Permissions.Vendors.Suspend)
            .Validate<Request>()
            .Produces<VendorResponse>();
}
