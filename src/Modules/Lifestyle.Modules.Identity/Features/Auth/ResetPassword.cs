using FluentValidation;
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
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Identity.Features.Auth;

/// <summary>
/// Redeems the link from <see cref="ForgotPassword"/> and sets the new password.
///
/// <para>
/// The token is consumed before the password is written, and one error covers "wrong", "already
/// used" and "expired" — the three are the same event to an honest user and three different
/// signals to someone guessing.
/// </para>
///
/// <para>
/// This deliberately does **not** sign the caller in. Issuing a session here would mean deciding a
/// surface and stepping around the TOTP gate on an account that may have two-factor enabled, which
/// is precisely the gate a stolen reset link would want stepped around. They sign in afterwards,
/// through the front door, with the factors their account actually requires.
/// </para>
/// </summary>
internal static class ResetPassword
{
    public sealed record Request(string Token, string NewPassword);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator(IOptions<IdentityModuleOptions> options)
        {
            var minimum = options.Value.MinimumPasswordLength;

            RuleFor(r => r.Token).NotEmpty().MaximumLength(256);
            RuleFor(r => r.NewPassword)
                .NotEmpty()
                .MinimumLength(minimum)
                .WithMessage($"Password must be at least {minimum} characters.")
                .MaximumLength(128);
        }
    }

    internal sealed class Handler(
        IIdentityDbContext db,
        IPasswordService passwords,
        ITokenService tokens,
        IDistributedCache cache,
        IClock clock)
        : IHandler<Request, Result>
    {
        public async Task<Result> Handle(Request request, CancellationToken ct)
        {
            var key = ForgotPassword.CacheKey(tokens.HashSingleUseToken(request.Token));
            var userId = await cache.GetStringAsync(key, ct);

            if (!Guid.TryParse(userId, out var id))
                return Invalid();

            // Consumed first: if the write below fails, the link is spent rather than replayable.
            await cache.RemoveAsync(key, ct);

            var user = await db.Users
                .Include(u => u.RefreshTokens)
                .FirstOrDefaultAsync(u => u.Id == id, ct);

            if (user is null) return Invalid();

            // A suspended account is not resettable. Saying so would confirm the address exists,
            // so it answers exactly as a bad token does.
            if (user.Status == UserStatus.Suspended) return Invalid();

            user.ResetPassword(passwords.Hash(request.NewPassword), clock.UtcNow);
            await db.SaveChangesAsync(ct);

            return Result.Success();
        }

        private static Error Invalid() =>
            Error.Validation("identity.reset_token_invalid",
                "That link is no longer valid. Request a new one and use the most recent email.");
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/reset-password", async (Request request, Handler handler, HttpContext http, CancellationToken ct) =>
            {
                var result = await handler.Handle(request, ct);
                if (result.IsFailure) return ResultExtensions.Problem(result.Error);

                // The reset revoked every refresh token; clear the cookie so the browser is not
                // left holding one that will only fail on its next use.
                AuthCookies.Clear(http.Response, http.Request.IsHttps);
                return Microsoft.AspNetCore.Http.Results.NoContent();
            })
            .WithName("ResetPassword")
            .WithSummary("Set a new password using a reset link, ending every existing session.")
            .AllowAnonymous()
            .Validate<Request>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
