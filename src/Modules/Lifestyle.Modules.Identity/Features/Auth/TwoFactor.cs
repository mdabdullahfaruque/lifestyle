using System.Text;
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
using Microsoft.Extensions.Caching.Distributed;
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

    /// <summary>Secret is accepted for wire-compatibility but IGNORED — the server-held one wins.</summary>
    public sealed record ConfirmRequest(string? Secret, string Code);

    public sealed class ConfirmValidator : AbstractValidator<ConfirmRequest>
    {
        public ConfirmValidator()
        {
            RuleFor(r => r.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
        }
    }

    internal sealed class BeginHandler(
        IIdentityDbContext db,
        ITotpService totp,
        ICurrentUser currentUser,
        IDistributedCache cache,
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

            // The secret the CONFIRM step will accept is this one, held server-side against the
            // user for ten minutes — never the one the client sends back. Otherwise a hijacked
            // session could enrol an attacker-chosen secret and own the second factor.
            await cache.SetAsync(EnrolmentKey(userId), Encoding.UTF8.GetBytes(secret),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, ct);

            return new BeginResponse(secret, totp.BuildProvisioningUri(secret, user.Email, options.Value.JwtIssuer));
        }
    }

    internal static string EnrolmentKey(Guid userId) => $"2fa-enrol:{userId}";

    internal sealed class ConfirmHandler(
        IIdentityDbContext db,
        ITotpService totp,
        ICurrentUser currentUser,
        IDistributedCache cache,
        IClock clock)
        : IHandler<ConfirmRequest, Result>
    {
        public async Task<Result> Handle(ConfirmRequest request, CancellationToken ct)
        {
            if (currentUser.UserId is not { } userId)
                return Error.Unauthorized("identity.not_authenticated");

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return Error.NotFound("identity.user_not_found");

            // Only the server-held secret from the begin step counts; request.Secret is ignored.
            var cached = await cache.GetAsync(EnrolmentKey(userId), ct);
            if (cached is null)
                return Error.Validation("identity.totp_enrolment_expired",
                    "The enrolment expired. Start again from the QR step.");

            var secret = Encoding.UTF8.GetString(cached);

            if (!totp.Verify(secret, request.Code))
                return Error.Validation("identity.totp_invalid", "That code did not match. Check your authenticator app.");

            user.EnableTwoFactor(secret, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await cache.RemoveAsync(EnrolmentKey(userId), ct);

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
