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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Identity.Features.Auth;

/// <summary>
/// Email + password login for one surface. The surface is part of the request, not inferred from
/// the host, so the same endpoint serves all three and the audience separation stays explicit.
/// </summary>
internal static class Login
{
    public sealed record Request(string Email, string Password, string Surface, string? TotpCode, Guid? VendorId);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Email).NotEmpty().EmailAddress();
            RuleFor(r => r.Password).NotEmpty();
            RuleFor(r => r.Surface)
                .NotEmpty()
                .Must(Surfaces.All.Contains)
                .WithMessage("Surface must be one of: buyer, seller, admin.");
        }
    }

    internal sealed class Handler(
        IIdentityDbContext db,
        IPasswordService passwords,
        ITokenService tokens,
        ITotpService totp,
        PermissionResolver permissions,
        IClock clock,
        IOptions<IdentityModuleOptions> options)
        : IHandler<Handler.Command, Result<LoginOutcome>>
    {
        private readonly IdentityModuleOptions _options = options.Value;

        internal sealed record Command(Request Request, string? UserAgent, string? IpAddress);

        public async Task<Result<LoginOutcome>> Handle(Command command, CancellationToken ct)
        {
            var request = command.Request;
            var now = clock.UtcNow;
            var email = request.Email.Trim().ToLowerInvariant();

            var user = await db.Users
                .Include(u => u.RefreshTokens)
                .FirstOrDefaultAsync(u => u.Email == email, ct);

            // Same error for "no such user" and "wrong password": the difference is exactly what an
            // enumeration attack is looking for.
            if (user is null)
                return Error.Unauthorized("identity.invalid_credentials", "Email or password is incorrect.");

            if (user.IsLockedOut(now))
                return Error.Forbidden("identity.account_locked", "Too many failed attempts. Try again later.");

            if (user.Status == UserStatus.Suspended)
                return Error.Forbidden("identity.account_suspended", "This account has been suspended.");

            var verification = passwords.Verify(user.PasswordHash, request.Password);
            if (verification == PasswordVerificationResult.Failed)
            {
                user.RecordFailedLogin(now);
                await db.SaveChangesAsync(ct);
                return Error.Unauthorized("identity.invalid_credentials", "Email or password is incorrect.");
            }

            // The hasher's parameters have moved on since this hash was written — upgrade it now,
            // while we legitimately hold the plaintext.
            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
                user.SetPasswordHash(passwords.Hash(request.Password), now);

            var access = await permissions.ResolveAsync(user.Id, request.Surface, request.VendorId, ct);

            if (access.NeedsVendorChoice)
                return Error.Validation("identity.vendor_required", "This account staffs more than one vendor; specify vendorId.");

            if (!access.HasAccess)
                return Error.Forbidden("identity.surface_not_permitted", "This account cannot sign in on this surface.");

            // 2FA is mandatory on admin, and on seller for anyone who has enabled it (FRD §4.2).
            if (user.TwoFactorEnabled)
            {
                if (string.IsNullOrWhiteSpace(request.TotpCode))
                    return Error.Validation("identity.totp_required", "A two-factor code is required.");

                if (!totp.Verify(user.TwoFactorSecret!, request.TotpCode))
                {
                    user.RecordFailedLogin(now);
                    await db.SaveChangesAsync(ct);
                    return Error.Unauthorized("identity.totp_invalid", "The two-factor code is incorrect.");
                }
            }
            else if (request.Surface == Surfaces.Admin)
            {
                return Error.Forbidden("identity.totp_enrolment_required",
                    "Two-factor authentication must be enabled before signing in to the admin surface.");
            }

            user.RecordSuccessfulLogin(now);

            var (refreshValue, refreshHash) = tokens.CreateRefreshToken();
            var lifetime = TimeSpan.FromDays(_options.RefreshTokenDays);
            user.IssueRefreshToken(refreshHash, Guid.CreateVersion7(), now, lifetime, command.UserAgent, command.IpAddress);

            await db.SaveChangesAsync(ct);

            var accessToken = tokens.CreateAccessToken(user, request.Surface, [.. access.Permissions], access.VendorId);

            return new LoginOutcome(
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

    internal sealed record LoginOutcome(AuthResponse Response, string RefreshToken, DateTimeOffset RefreshExpiresAt);

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/login", async (
                Request request,
                Handler handler,
                HttpContext http,
                CancellationToken ct) =>
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
            .WithName("Login")
            .WithSummary("Sign in and receive an access token; the refresh token is set as an HttpOnly cookie.")
            .AllowAnonymous()
            .Validate<Request>()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
