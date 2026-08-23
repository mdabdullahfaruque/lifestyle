using FluentValidation;
using Lifestyle.Modules.Identity.Internal;
using Lifestyle.Modules.Identity.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Identity.Features.Auth;

/// <summary>
/// TOTP enrolment: begin returns a secret and provisioning URI; confirm proves the user actually
/// scanned it. The secret is only persisted on confirm, so an abandoned enrolment cannot lock
/// anyone out.
/// </summary>
internal static class EnrolTwoFactor
{
    public sealed record BeginResponse(string Secret, string ProvisioningUri);

    public sealed record ConfirmRequest(string Secret, string Code);

    public sealed class ConfirmValidator : AbstractValidator<ConfirmRequest>
    {
        public ConfirmValidator()
        {
            RuleFor(r => r.Secret).NotEmpty();
            RuleFor(r => r.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
        }
    }

    internal sealed class BeginHandler(
        IIdentityDbContext db,
        ITotpService totp,
        ICurrentUser currentUser,
        IOptions<IdentityModuleOptions> options)
        : IHandler<Unit, Result<BeginResponse>>
    {
        public async Task<Result<BeginResponse>> Handle(Unit _, CancellationToken ct)
        {
            if (currentUser.UserId is not { } userId)
                return Error.Unauthorized("identity.not_authenticated");

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return Error.NotFound("identity.user_not_found");

            var secret = totp.GenerateSecret();
            return new BeginResponse(secret, totp.BuildProvisioningUri(secret, user.Email, options.Value.JwtIssuer));
        }
    }

    internal sealed class ConfirmHandler(
        IIdentityDbContext db,
        ITotpService totp,
        ICurrentUser currentUser,
        IClock clock)
        : IHandler<ConfirmRequest, Result>
    {
        public async Task<Result> Handle(ConfirmRequest request, CancellationToken ct)
        {
            if (currentUser.UserId is not { } userId)
                return Error.Unauthorized("identity.not_authenticated");

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return Error.NotFound("identity.user_not_found");

            if (!totp.Verify(request.Secret, request.Code))
                return Error.Validation("identity.totp_invalid", "That code did not match. Check your authenticator app.");

            user.EnableTwoFactor(request.Secret, clock.UtcNow);
            await db.SaveChangesAsync(ct);

            return Result.Success();
        }
    }

    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapPost("/2fa/begin", async (BeginHandler handler, CancellationToken ct) =>
                (await handler.Handle(Unit.Value, ct)).ToHttpResult())
            .WithName("BeginTwoFactorEnrolment")
            .WithSummary("Generate a TOTP secret and provisioning URI.")
            .RequireAuthorization()
            .Produces<BeginResponse>();

        group.MapPost("/2fa/confirm", async (ConfirmRequest request, ConfirmHandler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("ConfirmTwoFactorEnrolment")
            .WithSummary("Prove the authenticator was set up, and enable two-factor sign-in.")
            .RequireAuthorization()
            .Validate<ConfirmRequest>()
            .Produces(StatusCodes.Status204NoContent);
    }
}
