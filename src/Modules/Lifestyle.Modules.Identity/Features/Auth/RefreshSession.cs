using Lifestyle.Modules.Identity.Internal;
using Lifestyle.Modules.Identity.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Identity.Features.Auth;

/// <summary>
/// Rotating refresh (FRD §4.2). Every use issues a successor and revokes the presented token.
/// Presenting a token that was already rotated means it leaked, so the entire family is revoked —
/// the legitimate user is logged out too, which is the correct trade for a stolen session.
/// </summary>
internal static class RefreshSession
{
    public sealed record Request(string Surface, Guid? VendorId);

    internal sealed class Handler(
        IIdentityDbContext db,
        ITokenService tokens,
        PermissionResolver permissions,
        IClock clock,
        IOptions<IdentityModuleOptions> options,
        ILogger<Handler> logger)
        : IHandler<Handler.Command, Result<Login.LoginOutcome>>
    {
        private readonly IdentityModuleOptions _options = options.Value;

        internal sealed record Command(string RefreshToken, string Surface, Guid? VendorId, string? UserAgent, string? IpAddress);

        public async Task<Result<Login.LoginOutcome>> Handle(Command command, CancellationToken ct)
        {
            var now = clock.UtcNow;
            var hash = tokens.HashRefreshToken(command.RefreshToken);

            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (stored is null)
                return Error.Unauthorized("identity.refresh_invalid", "The session is no longer valid.");

            var user = await db.Users
                .Include(u => u.RefreshTokens)
                .FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);

            if (user is null)
                return Error.Unauthorized("identity.refresh_invalid", "The session is no longer valid.");

            if (stored.IsReplayed(now))
            {
                logger.LogWarning(
                    "Refresh token replay detected for user {UserId}, family {FamilyId}. Revoking the family.",
                    user.Id, stored.FamilyId);

                user.RevokeTokenFamily(stored.FamilyId, now, "replay_detected");
                await db.SaveChangesAsync(ct);

                return Error.Unauthorized("identity.refresh_replayed",
                    "This session was terminated for security reasons. Please sign in again.");
            }

            var access = await permissions.ResolveAsync(user.Id, command.Surface, command.VendorId, ct);
            if (!access.HasAccess)
                return Error.Forbidden("identity.surface_not_permitted", "This account cannot use this surface.");

            var (value, successorHash) = tokens.CreateRefreshToken();
            var lifetime = TimeSpan.FromDays(_options.RefreshTokenDays);

            // The successor stays in the same family, so a later replay of any ancestor still
            // brings the whole chain down.
            var successor = user.IssueRefreshToken(successorHash, stored.FamilyId, now, lifetime,
                command.UserAgent, command.IpAddress);
            stored.MarkRotated(successor.Id, now);

            await db.SaveChangesAsync(ct);

            var accessToken = tokens.CreateAccessToken(user, command.Surface, [.. access.Permissions], access.VendorId);

            return new Login.LoginOutcome(
                new AuthResponse(
                    accessToken,
                    _options.AccessTokenMinutes * 60,
                    command.Surface,
                    access.VendorId,
                    new UserProfileResponse(user.Id, user.Email, user.FullName, user.PhoneNumber,
                        user.EmailVerified, user.TwoFactorEnabled, [.. access.Permissions])),
                value,
                now.Add(lifetime));
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/refresh", async (Request request, Handler handler, HttpContext http, CancellationToken ct) =>
            {
                var cookie = AuthCookies.Read(http.Request);
                if (cookie is null)
                    return ResultExtensions.Problem(
                        Error.Unauthorized("identity.refresh_missing", "No session cookie was presented."));

                var result = await handler.Handle(
                    new Handler.Command(cookie, request.Surface, request.VendorId,
                        http.Request.Headers.UserAgent.ToString(),
                        http.Connection.RemoteIpAddress?.ToString()),
                    ct);

                if (result.IsFailure)
                {
                    AuthCookies.Clear(http.Response, http.Request.IsHttps);
                    return ResultExtensions.Problem(result.Error);
                }

                AuthCookies.SetRefreshToken(http.Response, result.Value.RefreshToken,
                    result.Value.RefreshExpiresAt, http.Request.IsHttps);

                return Microsoft.AspNetCore.Http.Results.Ok(result.Value.Response);
            })
            .WithName("RefreshSession")
            .WithSummary("Exchange the refresh cookie for a new access token, rotating the refresh token.")
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
}
