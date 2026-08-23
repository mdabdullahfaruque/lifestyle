using FluentValidation;
using Lifestyle.Modules.Vendors.Domain;
using Lifestyle.Modules.Vendors.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Identifiers;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Vendors.Features.Vendors;

/// <summary>
/// Any signed-in user can start a vendor application. It lands in Draft; nothing is visible and
/// nothing can be sold until an admin approves it.
/// </summary>
internal static class ApplyAsVendor
{
    public sealed record Request(
        string LegalName,
        string DisplayName,
        string? DesiredSlug,
        string ContactEmail,
        string ContactPhone,
        string? RegistrationNumber);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.LegalName).NotEmpty().MaximumLength(300);
            RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(200);
            RuleFor(r => r.ContactEmail).NotEmpty().EmailAddress().MaximumLength(320);
            RuleFor(r => r.ContactPhone).NotEmpty().MaximumLength(32);
            RuleFor(r => r.RegistrationNumber).MaximumLength(60);

            RuleFor(r => r.DesiredSlug)
                .Must(s => Slug.IsValid(s))
                .When(r => !string.IsNullOrWhiteSpace(r.DesiredSlug))
                .WithMessage("A shop address may only contain lowercase letters, numbers and hyphens.");
        }
    }

    internal sealed class Handler(IVendorsDbContext db, ICurrentUser currentUser, IClock clock)
        : IHandler<Request, Result<VendorResponse>>
    {
        public async Task<Result<VendorResponse>> Handle(Request request, CancellationToken ct)
        {
            if (currentUser.UserId is not { } userId)
                return Error.Unauthorized("identity.not_authenticated");

            // One application at a time. Someone who already owns a vendor and wants a second one
            // is a real case, but it needs a deliberate flow rather than an accidental duplicate.
            var existing = await db.VendorStaff
                .AnyAsync(s => s.UserId == userId && s.Role == VendorStaffRole.Owner, ct);

            if (existing)
                return Error.Conflict("vendors.already_owner", "You already own a vendor account.");

            var slugResult = await ReserveSlugAsync(db, request.DesiredSlug ?? request.DisplayName, ct);
            if (slugResult.IsFailure) return slugResult.Error;

            var vendor = Vendor.Apply(
                request.LegalName, request.DisplayName, slugResult.Value,
                request.ContactEmail, request.ContactPhone, request.RegistrationNumber,
                userId, clock.UtcNow);

            db.Vendors.Add(vendor);
            await db.SaveChangesAsync(ct);

            return vendor.ToResponse();
        }
    }

    /// <summary>
    /// Turns a desired name into a free slug, appending -2, -3 … on collision. Shared with the
    /// admin create flow, so both produce identical results.
    /// </summary>
    internal static async Task<Result<string>> ReserveSlugAsync(IVendorsDbContext db, string desired, CancellationToken ct)
    {
        var baseSlug = Slug.From(desired);

        if (!Slug.IsValid(baseSlug))
            return Error.Validation("vendors.slug_invalid", "That shop name cannot be turned into a web address. Choose another.");

        if (Slug.IsReserved(baseSlug))
            return Error.Conflict("vendors.slug_reserved", $"'{baseSlug}' is reserved. Choose a different shop address.");

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            var candidate = Slug.WithSuffix(baseSlug, attempt);
            if (Slug.IsReserved(candidate)) continue;

            if (!await db.Vendors.IgnoreQueryFilters().AnyAsync(v => v.Slug == candidate, ct))
                return candidate;
        }

        return Error.Conflict("vendors.slug_unavailable", "That shop address is taken. Choose a different one.");
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToCreated(v => $"/v1/vendor/profile"))
            .WithName("ApplyAsVendor")
            .WithSummary("Start a vendor application.")
            .RequireAuthorization()
            .Validate<Request>()
            .Produces<VendorResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);
}
