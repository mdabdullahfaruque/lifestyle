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
/// Buyer self-registration. Seller and admin accounts are never self-served — they are created by
/// vendor onboarding or by an existing admin.
/// </summary>
internal static class Register
{
    public sealed record Request(string Email, string Password, string FullName, string? PhoneNumber);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator(IOptions<IdentityModuleOptions> options)
        {
            var minimum = options.Value.MinimumPasswordLength;

            RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(320);
            RuleFor(r => r.FullName).NotEmpty().MaximumLength(200);
            RuleFor(r => r.PhoneNumber).MaximumLength(32);

            // Length over composition rules: NIST guidance, and composition rules mostly produce
            // "Password1!". The compromised-password check (FRD §4.2) is a Phase 1 follow-up.
            RuleFor(r => r.Password)
                .NotEmpty()
                .MinimumLength(minimum)
                .WithMessage($"Password must be at least {minimum} characters.")
                .MaximumLength(128);
        }
    }

    internal sealed class Handler(
        IIdentityDbContext db,
        IPasswordService passwords,
        IClock clock)
        : IHandler<Request, Result<UserProfileResponse>>
    {
        public async Task<Result<UserProfileResponse>> Handle(Request request, CancellationToken ct)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            if (await db.Users.AnyAsync(u => u.Email == email, ct))
            {
                // Deliberately explicit. Enumeration is already possible through the login flow and
                // through password reset; a vague message here only confuses honest users.
                return Error.Conflict("identity.email_taken", "An account with this email already exists.");
            }

            var user = User.Register(email, passwords.Hash(request.Password), request.FullName, request.PhoneNumber, clock.UtcNow);
            user.AssignRole(SystemRoles.BuyerId, null, clock.UtcNow);

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            return new UserProfileResponse(
                user.Id, user.Email, user.FullName, user.PhoneNumber,
                user.EmailVerified, user.TwoFactorEnabled, []);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/register", async (Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToCreated(u => $"/v1/me"))
            .WithName("Register")
            .WithSummary("Create a buyer account.")
            .AllowAnonymous()
            .Validate<Request>()
            .Produces<UserProfileResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);
}
