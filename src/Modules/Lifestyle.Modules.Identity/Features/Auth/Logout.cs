using Lifestyle.Modules.Identity.Internal;
using Lifestyle.Modules.Identity.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Identity.Features.Auth;

/// <summary>
/// Revokes the presented refresh token's family and clears the cookie. Idempotent and anonymous by
/// design: a client whose access token has already expired must still be able to log out.
/// </summary>
internal static class Logout
{
    internal sealed class Handler(IIdentityDbContext db, ITokenService tokens, IClock clock)
        : IHandler<string, Result>
    {
        public async Task<Result> Handle(string refreshToken, CancellationToken ct)
        {
            var hash = tokens.HashRefreshToken(refreshToken);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (stored is null) return Result.Success();

            var user = await db.Users
                .Include(u => u.RefreshTokens)
                .FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);

            user?.RevokeTokenFamily(stored.FamilyId, clock.UtcNow, "logout");
            await db.SaveChangesAsync(ct);

            return Result.Success();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/logout", async (Handler handler, HttpContext http, CancellationToken ct) =>
            {
                var cookie = AuthCookies.Read(http.Request);
                if (cookie is not null) await handler.Handle(cookie, ct);

                AuthCookies.Clear(http.Response, http.Request.IsHttps);
                return Microsoft.AspNetCore.Http.Results.NoContent();
            })
            .WithName("Logout")
            .WithSummary("Revoke the current session.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent);
}
