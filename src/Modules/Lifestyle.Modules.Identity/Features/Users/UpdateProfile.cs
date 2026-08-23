using FluentValidation;
using Lifestyle.Modules.Identity.Features.Auth;
using Lifestyle.Modules.Identity.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Identity.Features.Users;

internal static class UpdateProfile
{
    public sealed record Request(string FullName, string? PhoneNumber);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.FullName).NotEmpty().MaximumLength(200);
            RuleFor(r => r.PhoneNumber)
                .MaximumLength(32)
                .Matches(@"^\+?[0-9\s\-()]{6,32}$")
                .When(r => !string.IsNullOrWhiteSpace(r.PhoneNumber))
                .WithMessage("Enter a valid phone number, including country code.");
        }
    }

    internal sealed class Handler(IIdentityDbContext db, ICurrentUser currentUser, IClock clock)
        : IHandler<Request, Result<UserProfileResponse>>
    {
        public async Task<Result<UserProfileResponse>> Handle(Request request, CancellationToken ct)
        {
            if (currentUser.UserId is not { } userId)
                return Error.Unauthorized("identity.not_authenticated");

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return Error.NotFound("identity.user_not_found");

            var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
            if (phone is not null && await db.Users.AnyAsync(u => u.PhoneNumber == phone && u.Id != userId, ct))
                return Error.Conflict("identity.phone_taken", "That phone number is already in use.");

            user.UpdateProfile(request.FullName, phone, clock.UtcNow);
            await db.SaveChangesAsync(ct);

            return new UserProfileResponse(user.Id, user.Email, user.FullName, user.PhoneNumber,
                user.EmailVerified, user.TwoFactorEnabled, [.. currentUser.Permissions]);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/", async (Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("UpdateProfile")
            .WithSummary("Update the signed-in user's name and phone number.")
            .RequireAuthorization()
            .Validate<Request>()
            .Produces<UserProfileResponse>();
}
