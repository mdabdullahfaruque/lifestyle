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

namespace Lifestyle.Modules.Identity.Features.Users;

/// <summary>
/// Gives an account that has only ever signed in with Google its first password.
///
/// <para>
/// <c>ChangePassword</c> cannot serve this: it verifies a current password, and there is none to
/// verify. Until now that left such an owner with exactly one way into their account forever — and
/// a shop owner whose only key is a Google account they might lose access to, on a console whose
/// Google button depends on an origin staying registered, is one configuration slip from being
/// locked out of their own business.
/// </para>
///
/// <para>
/// No current password is asked for because none exists; the caller's own access token is the
/// proof. Every session still ends afterwards, exactly as a change does — if this call was not
/// made by the owner, the session that made it dies with all the others.
/// </para>
/// </summary>
internal static class SetFirstPassword
{
    public sealed record Request(string NewPassword);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator(IOptions<IdentityModuleOptions> options)
        {
            var minimum = options.Value.MinimumPasswordLength;

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
        ICurrentUser currentUser,
        IClock clock)
        : IHandler<Request, Result>
    {
        public async Task<Result> Handle(Request request, CancellationToken ct)
        {
            if (currentUser.UserId is not { } userId)
                return Error.Unauthorized("identity.not_authenticated");

            var user = await db.Users
                .Include(u => u.RefreshTokens)
                .FirstOrDefaultAsync(u => u.Id == userId, ct);

            if (user is null) return Error.NotFound("identity.user_not_found");

            // Not an error worth hiding: the caller is authenticated as this account, so it already
            // knows everything this answer reveals.
            if (user.HasPassword)
            {
                return Error.Validation("identity.password_already_set",
                    "This account already has a password. Change it instead.");
            }

            user.SetPasswordHash(passwords.Hash(request.NewPassword), clock.UtcNow);
            await db.SaveChangesAsync(ct);

            return Result.Success();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/set-password", async (Request request, Handler handler, HttpContext http, CancellationToken ct) =>
            {
                var result = await handler.Handle(request, ct);
                if (result.IsFailure) return ResultExtensions.Problem(result.Error);

                Features.Auth.AuthCookies.Clear(http.Response, http.Request.IsHttps);
                return Microsoft.AspNetCore.Http.Results.NoContent();
            })
            .WithName("SetFirstPassword")
            .WithSummary("Set a password on an account that signs in with Google only.")
            .RequireAuthorization()
            .Validate<Request>()
            .Produces(StatusCodes.Status204NoContent);
}
