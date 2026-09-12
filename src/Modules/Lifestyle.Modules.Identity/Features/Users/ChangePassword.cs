using FluentValidation;
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

namespace Lifestyle.Modules.Identity.Features.Users;

internal static class ChangePassword
{
    public sealed record Request(string CurrentPassword, string NewPassword);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator(IOptions<IdentityModuleOptions> options)
        {
            var minimum = options.Value.MinimumPasswordLength;

            RuleFor(r => r.CurrentPassword).NotEmpty();
            RuleFor(r => r.NewPassword)
                .NotEmpty()
                .MinimumLength(minimum)
                .WithMessage($"Password must be at least {minimum} characters.")
                .MaximumLength(128)
                .NotEqual(r => r.CurrentPassword)
                .WithMessage("The new password must be different from the current one.");
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

            // This account signed up through an external provider and has never had a password, so
            // there is no current one to verify. Setting a first password from here is not built —
            // recorded in docs/07 — and silently accepting any "current password" would be worse.
            if (!user.HasPassword)
            {
                return Error.Validation("identity.password_not_set",
                    "This account signs in with Google and has no password to change.");
            }

            if (passwords.Verify(user.PasswordHash!, request.CurrentPassword) == PasswordVerificationResult.Failed)
                return Error.Validation("identity.current_password_invalid", "Your current password is incorrect.");

            // SetPasswordHash also revokes every active refresh token — changing a password is how
            // a user reacts to a suspected compromise, so every other session must die.
            user.SetPasswordHash(passwords.Hash(request.NewPassword), clock.UtcNow);
            await db.SaveChangesAsync(ct);

            return Result.Success();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/change-password", async (Request request, Handler handler, HttpContext http, CancellationToken ct) =>
            {
                var result = await handler.Handle(request, ct);
                if (result.IsFailure) return ResultExtensions.Problem(result.Error);

                // Every session was just revoked, including this one.
                Features.Auth.AuthCookies.Clear(http.Response, http.Request.IsHttps);
                return Microsoft.AspNetCore.Http.Results.NoContent();
            })
            .WithName("ChangePassword")
            .WithSummary("Change the password and sign out every session.")
            .RequireAuthorization()
            .Validate<Request>()
            .Produces(StatusCodes.Status204NoContent);
}
