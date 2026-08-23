using FluentValidation;
using Lifestyle.Modules.Vendors.Domain;
using Lifestyle.Modules.Vendors.Internal;
using Lifestyle.Modules.Vendors.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Vendors.Features.Vendors;

// ══════════════════════════════════════════════════════════════════════════════════════════════
// Onboarding — authenticated as an ordinary user, before any seller role exists.
// See ApplicantScope for why these cannot live on the seller surface.
// ══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>The applicant's view of their own in-progress application.</summary>
internal static class GetMyApplication
{
    internal sealed class Handler(ApplicantScope scope) : IHandler<Unit, Result<VendorResponse>>
    {
        public async Task<Result<VendorResponse>> Handle(Unit _, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedApplicationAsync(ct);
            return loaded.IsFailure ? loaded.Error : loaded.Value.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/mine", async (Handler handler, CancellationToken ct) =>
                (await handler.Handle(Unit.Value, ct)).ToHttpResult())
            .WithName("GetMyApplication")
            .WithSummary("The caller's own vendor application, in any state.")
            .RequireAuthorization()
            .Produces<VendorResponse>();
}

internal static class UploadVendorDocument
{
    public sealed record Request(string Kind, string MediaId, string FileName);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Kind)
                .NotEmpty()
                .Must(k => Enum.TryParse<VendorDocumentKind>(k, ignoreCase: true, out _))
                .WithMessage("Kind must be one of: BusinessRegistration, OwnerIdentity, BankStatement, TaxCertificate, Other.");

            RuleFor(r => r.MediaId).NotEmpty().MaximumLength(64);
            RuleFor(r => r.FileName).NotEmpty().MaximumLength(255);
        }
    }

    internal sealed class Handler(IVendorsDbContext db, ApplicantScope scope, IClock clock)
        : IHandler<Request, Result<VendorResponse>>
    {
        public async Task<Result<VendorResponse>> Handle(Request request, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedApplicationAsync(ct);
            if (loaded.IsFailure) return loaded.Error;

            var vendor = loaded.Value;
            var kind = Enum.Parse<VendorDocumentKind>(request.Kind, ignoreCase: true);
            vendor.AddDocument(kind, request.MediaId, request.FileName, clock.UtcNow);

            await db.SaveChangesAsync(ct);
            return vendor.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/documents", async (Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("UploadVendorDocument")
            .WithSummary("Attach a KYC document that has already been uploaded to Media.")
            .RequireAuthorization()
            .Validate<Request>()
            .Produces<VendorResponse>();
}

internal static class SubmitVendorForReview
{
    internal sealed class Handler(IVendorsDbContext db, ApplicantScope scope, IClock clock)
        : IHandler<Unit, Result<VendorResponse>>
    {
        public async Task<Result<VendorResponse>> Handle(Unit _, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedApplicationAsync(ct);
            if (loaded.IsFailure) return loaded.Error;

            var vendor = loaded.Value;
            var result = vendor.SubmitForReview(clock.UtcNow);
            if (result.IsFailure) return result.Error;

            await db.SaveChangesAsync(ct);
            return vendor.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/submit", async (Handler handler, CancellationToken ct) =>
                (await handler.Handle(Unit.Value, ct)).ToHttpResult())
            .WithName("SubmitVendorForReview")
            .WithSummary("Submit the application to the platform approval queue.")
            .RequireAuthorization()
            .Produces<VendorResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
}

// ══════════════════════════════════════════════════════════════════════════════════════════════
// Post-approval — the seller surface, scoped to the vendor in the token.
// ══════════════════════════════════════════════════════════════════════════════════════════════

internal static class GetVendorProfile
{
    internal sealed class Handler(IVendorsDbContext db, ICurrentUser currentUser)
        : IHandler<Unit, Result<VendorResponse>>
    {
        public async Task<Result<VendorResponse>> Handle(Unit _, CancellationToken ct)
        {
            if (currentUser.VendorId is not { } vendorId)
                return Error.Forbidden("vendors.no_vendor_context", "This token is not scoped to a vendor.");

            var vendor = await db.Vendors
                .Include(v => v.Documents)
                .Include(v => v.Staff)
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == vendorId, ct);

            return vendor is null ? Error.NotFound("vendors.not_found") : vendor.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/profile", async (Handler handler, CancellationToken ct) =>
                (await handler.Handle(Unit.Value, ct)).ToHttpResult())
            .WithName("GetVendorProfile")
            .WithSummary("The caller's vendor profile, including KYC documents and staff.")
            .Produces<VendorResponse>();
}

internal static class UpdateStorefront
{
    public sealed record Request(
        string DisplayName,
        string? About,
        string? LogoMediaId,
        string? BannerMediaId,
        string? AccentColour,
        string? WhatsAppNumber);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(200);
            RuleFor(r => r.About).MaximumLength(4000);
            RuleFor(r => r.LogoMediaId).MaximumLength(64);
            RuleFor(r => r.BannerMediaId).MaximumLength(64);

            RuleFor(r => r.AccentColour)
                .Matches("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")
                .When(r => !string.IsNullOrWhiteSpace(r.AccentColour))
                .WithMessage("Use a hex colour such as #C2185B.");

            RuleFor(r => r.WhatsAppNumber)
                .Matches(@"^\+[1-9][0-9]{6,17}$")
                .When(r => !string.IsNullOrWhiteSpace(r.WhatsAppNumber))
                .WithMessage("Use full international format, for example +60123456789.");
        }
    }

    internal sealed class Handler(IVendorsDbContext db, ICurrentUser currentUser, IClock clock)
        : IHandler<Request, Result<VendorResponse>>
    {
        public async Task<Result<VendorResponse>> Handle(Request request, CancellationToken ct)
        {
            if (currentUser.VendorId is not { } vendorId)
                return Error.Forbidden("vendors.no_vendor_context", "This token is not scoped to a vendor.");

            var vendor = await db.Vendors
                .Include(v => v.Documents)
                .Include(v => v.Staff)
                .FirstOrDefaultAsync(v => v.Id == vendorId, ct);

            if (vendor is null) return Error.NotFound("vendors.not_found");

            vendor.UpdateStorefront(request.DisplayName, request.About, request.LogoMediaId,
                request.BannerMediaId, request.AccentColour, request.WhatsAppNumber, clock.UtcNow);

            await db.SaveChangesAsync(ct);
            return vendor.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/storefront", async (Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("UpdateStorefront")
            .WithSummary("Update the shop's public branding and WhatsApp ordering number.")
            .Validate<Request>()
            .Produces<VendorResponse>();
}
