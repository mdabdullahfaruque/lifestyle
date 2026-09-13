using FluentValidation;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Platform.Contracts;
using Lifestyle.Modules.Vendors.Domain;
using Lifestyle.Modules.Vendors.Features.Vendors;
using Lifestyle.Modules.Vendors.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Identifiers;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Vendors.Features.Admin;

/// <summary>
/// An administrator onboards a shop directly, on behalf of a seller who is not going to fill in
/// the application form themselves — the shop is signed up over the phone or in person.
/// <para>
/// This is a shortcut past the review queue, not past the rules: it ends in exactly the state a
/// reviewed application ends in (Approved, owner staff row, seller role granted), and it refuses
/// to touch a shop that did go through the queue. The KYC documents an applicant must upload are
/// not required here, because the administrator is the one vouching — that trade-off is the whole
/// point of the endpoint, and it is audited under its own action name so the two paths are
/// distinguishable afterwards.
/// </para>
/// </summary>
internal static class CreateVendorAsAdmin
{
    public sealed record Request(
        string LegalName,
        string DisplayName,
        string? DesiredSlug,
        string ContactEmail,
        string ContactPhone,
        string? RegistrationNumber,
        string OwnerEmail,
        string? OwnerFullName,
        string? OwnerPhone,
        bool Approve = true);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.LegalName).NotEmpty().MaximumLength(300);
            RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(200);
            RuleFor(r => r.ContactEmail).NotEmpty().EmailAddress().MaximumLength(320);
            RuleFor(r => r.ContactPhone).NotEmpty().MaximumLength(32);
            RuleFor(r => r.RegistrationNumber).MaximumLength(60);

            RuleFor(r => r.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(320);
            RuleFor(r => r.OwnerFullName).MaximumLength(200);
            RuleFor(r => r.OwnerPhone).MaximumLength(32);

            RuleFor(r => r.DesiredSlug)
                .Must(s => Slug.IsValid(s))
                .When(r => !string.IsNullOrWhiteSpace(r.DesiredSlug))
                .WithMessage("A shop address may only contain lowercase letters, numbers and hyphens.");
        }
    }

    internal sealed class Handler(
        IVendorsDbContext db,
        IIdentityModule identity,
        IAuditLog audit,
        ICurrentUser currentUser,
        IClock clock)
        : IHandler<Request, Result<AdminCreatedVendorResponse>>
    {
        public async Task<Result<AdminCreatedVendorResponse>> Handle(Request request, CancellationToken ct)
        {
            if (currentUser.UserId is not { } adminId)
                return Error.Unauthorized("identity.not_authenticated");

            var now = clock.UtcNow;
            var ownerEmail = request.OwnerEmail.Trim().ToLowerInvariant();

            var existing = await identity.FindByEmailAsync(ownerEmail, ct);

            if (existing is { IsActive: false })
            {
                return Error.Conflict("vendors.owner_inactive",
                    "That account is deactivated. Reactivate it before giving it a shop.");
            }

            if (existing is not null && await db.VendorStaff
                    .AnyAsync(s => s.UserId == existing.Id && s.Role == VendorStaffRole.Owner, ct))
            {
                return Error.Conflict("vendors.already_owner",
                    "That account already owns a shop. One owner, one shop.");
            }

            var slug = await ApplyAsVendor.ReserveSlugAsync(db, request.DesiredSlug ?? request.DisplayName, ct);
            if (slug.IsFailure) return slug.Error;

            // Create the account only once nothing else can fail: an orphaned login for a shop
            // that was never created would be invisible and impossible to explain.
            var created = existing is null
                ? await identity.CreateForVendorOwnerAsync(
                    ownerEmail,
                    string.IsNullOrWhiteSpace(request.OwnerFullName) ? request.DisplayName : request.OwnerFullName!,
                    request.OwnerPhone,
                    ct)
                : null;

            var ownerId = existing?.Id ?? created!.UserId;

            var vendor = Vendor.Apply(
                request.LegalName, request.DisplayName, slug.Value,
                request.ContactEmail, request.ContactPhone, request.RegistrationNumber,
                ownerId, now);

            if (request.Approve)
            {
                var approved = vendor.ApproveOnCreation(adminId, now);
                if (approved.IsFailure) return approved.Error;
            }

            db.Vendors.Add(vendor);

            audit.Record(
                request.Approve ? "vendor.created_by_admin" : "vendor.drafted_by_admin",
                "vendor", vendor.Id,
                new { vendor.DisplayName, vendor.Slug, OwnerEmail = ownerEmail, OwnerCreated = created is not null });

            await db.SaveChangesAsync(ct);

            // Same ordering as the review queue: the seller role is granted only once the vendor
            // row is committed, so a failure here leaves a shop without a seller rather than a
            // seller without a shop.
            if (request.Approve)
                await identity.GrantVendorRoleAsync(ownerId, vendor.Id, VendorRoleKind.Owner, ct);

            return new AdminCreatedVendorResponse(
                vendor.ToResponse(), ownerId, ownerEmail, created is not null, created?.TemporaryPassword);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToCreated(v => $"/v1/admin/vendors/{v.Vendor.Id}"))
            .WithName("CreateVendorAsAdmin")
            .WithSummary("Create a shop on a seller's behalf, approved immediately.")
            .RequirePermission(Permissions.Vendors.Approve)
            .Validate<Request>()
            .Produces<AdminCreatedVendorResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);
}

/// <summary>
/// The shop, plus what the administrator has to pass on to its owner.
/// <para>
/// <paramref name="TemporaryPassword"/> is present exactly once — in this response, when the
/// account was created here. It is not stored in readable form and cannot be retrieved again; if
/// it is lost the owner resets their password like anyone else.
/// </para>
/// </summary>
public sealed record AdminCreatedVendorResponse(
    VendorResponse Vendor,
    Guid OwnerUserId,
    string OwnerEmail,
    bool OwnerAccountCreated,
    string? TemporaryPassword);
