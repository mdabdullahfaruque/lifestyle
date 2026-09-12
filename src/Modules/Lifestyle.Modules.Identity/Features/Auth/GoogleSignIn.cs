using FluentValidation;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Identity.Domain;
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
/// Sign in with Google, on the buyer and seller surfaces.
///
/// <para>
/// **The admin surface is refused outright.** A platform admin can approve vendors, moderate the
/// catalogue and take shops down; making that reachable through a second identity provider means a
/// compromised Google account is a compromised platform. Admin stays password + TOTP, which is one
/// well-understood path rather than two.
/// </para>
///
/// <para>
/// An account is found in this order: an existing Google link (the provider's immutable subject),
/// then a matching verified email, then a new account. Linking on email is safe *only* because the
/// validator refuses a token whose <c>email_verified</c> is false — Google asserting a verified
/// address proves the same control that an email confirmation loop proves. Without that check this
/// would be an account-takeover primitive: claim any address, get handed that user's account.
/// </para>
///
/// <para>
/// Signing in with Google grants no roles beyond Buyer. Whether the caller may use the seller
/// surface is still decided by <see cref="PermissionResolver"/> against their vendor staff
/// membership, exactly as it is for a password sign-in.
/// </para>
/// </summary>
internal static class GoogleSignIn
{
    public sealed record Request(string IdToken, string Surface, Guid? VendorId);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.IdToken).NotEmpty();
            RuleFor(r => r.Surface)
                .NotEmpty()
                .Must(s => s == Surfaces.Buyer || s == Surfaces.Seller)
                .WithMessage("Google sign-in is available on the buyer and seller surfaces only.");
        }
    }

    internal sealed class Handler(
        IIdentityDbContext db,
        IGoogleTokenValidator google,
        ITokenService tokens,
        PermissionResolver permissions,
        IClock clock,
        IOptions<IdentityModuleOptions> options)
        : IHandler<Handler.Command, Result<Login.LoginOutcome>>
    {
        private readonly IdentityModuleOptions _options = options.Value;

        internal sealed record Command(Request Request, string? UserAgent, string? IpAddress);

        public async Task<Result<Login.LoginOutcome>> Handle(Command command, CancellationToken ct)
        {
            var request = command.Request;
            var now = clock.UtcNow;

            // Belt and braces: the validator rejects it, and so does this, because a surface check
            // that exists only in a validator is one refactor away from being gone.
            if (request.Surface == Surfaces.Admin)
            {
                return Error.Forbidden("identity.surface_not_permitted",
                    "The admin console cannot be reached with Google sign-in.");
            }

            var identity = await google.ValidateAsync(request.IdToken, ct);
            if (identity.IsFailure) return identity.Error;

            var (subject, email, _, name) = identity.Value;

            var user = await db.Users
                .Include(u => u.RefreshTokens)
                .Include(u => u.ExternalLogins)
                .FirstOrDefaultAsync(
                    u => u.ExternalLogins.Any(l => l.Provider == ExternalProviders.Google && l.Subject == subject),
                    ct);

            if (user is null)
            {
                user = await db.Users
                    .Include(u => u.RefreshTokens)
                    .Include(u => u.ExternalLogins)
                    .FirstOrDefaultAsync(u => u.Email == email, ct);

                if (user is not null)
                {
                    // Existing password account, same verified address: link rather than refuse, or
                    // the owner is locked out of their own account by using the wrong button.
                    user.LinkExternalLogin(ExternalProviders.Google, subject, now);
                    user.MarkEmailVerifiedByProvider(now);
                }
                else
                {
                    user = User.RegisterExternal(
                        email, string.IsNullOrWhiteSpace(name) ? email : name,
                        ExternalProviders.Google, subject, emailVerified: true, now);

                    user.AssignRole(SystemRoles.BuyerId, null, now);
                    db.Users.Add(user);
                }
            }

            if (user.Status == UserStatus.Suspended)
                return Error.Forbidden("identity.account_suspended", "This account has been suspended.");

            // A lockout is about this account, not about how the caller proved who they are.
            if (user.IsLockedOut(now))
                return Error.Forbidden("identity.account_locked", "Too many failed attempts. Try again later.");

            var access = await permissions.ResolveAsync(user.Id, request.Surface, request.VendorId, ct);

            if (access.NeedsVendorChoice)
                return Error.Validation("identity.vendor_required", "This account staffs more than one vendor; specify vendorId.");

            if (!access.HasAccess)
                return Error.Forbidden("identity.surface_not_permitted", "This account cannot sign in on this surface.");

            // Two-factor still applies to anyone who has turned it on. Google having authenticated
            // the person does not replace a second factor they deliberately added.
            if (user.TwoFactorEnabled)
            {
                return Error.Validation("identity.totp_required",
                    "This account uses two-factor authentication. Sign in with your password and code.");
            }

            user.RecordSuccessfulLogin(now);

            var (refreshValue, refreshHash) = tokens.CreateRefreshToken();
            var lifetime = TimeSpan.FromDays(_options.RefreshTokenDays);
            user.IssueRefreshToken(refreshHash, Guid.CreateVersion7(), now, lifetime, command.UserAgent, command.IpAddress);

            await db.SaveChangesAsync(ct);

            var accessToken = tokens.CreateAccessToken(user, request.Surface, [.. access.Permissions], access.VendorId);

            return new Login.LoginOutcome(
                new AuthResponse(
                    accessToken,
                    _options.AccessTokenMinutes * 60,
                    request.Surface,
                    access.VendorId,
                    new UserProfileResponse(user.Id, user.Email, user.FullName, user.PhoneNumber,
                        user.EmailVerified, user.TwoFactorEnabled, [.. access.Permissions])),
                refreshValue,
                now.Add(lifetime));
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/google", async (
                Request request, Handler handler, HttpContext http, CancellationToken ct) =>
            {
                var result = await handler.Handle(
                    new Handler.Command(request, http.Request.Headers.UserAgent.ToString(),
                        http.Connection.RemoteIpAddress?.ToString()),
                    ct);

                if (result.IsFailure) return ResultExtensions.Problem(result.Error);

                AuthCookies.SetRefreshToken(http.Response, result.Value.RefreshToken,
                    result.Value.RefreshExpiresAt, http.Request.IsHttps);

                return Microsoft.AspNetCore.Http.Results.Ok(result.Value.Response);
            })
            .WithName("GoogleSignIn")
            .WithSummary("Sign in with a Google ID token. Buyer and seller surfaces only.")
            .AllowAnonymous()
            .Validate<Request>()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
