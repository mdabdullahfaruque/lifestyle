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

internal static class GetCurrentUser
{
    internal sealed class Handler(IIdentityDbContext db, ICurrentUser currentUser)
        : IHandler<Unit, Result<UserProfileResponse>>
    {
        public async Task<Result<UserProfileResponse>> Handle(Unit _, CancellationToken ct)
        {
            if (currentUser.UserId is not { } userId)
                return Error.Unauthorized("identity.not_authenticated");

            var user = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new UserProfileResponse(
                    u.Id, u.Email, u.FullName, u.PhoneNumber,
                    u.EmailVerified, u.TwoFactorEnabled, new List<string>()))
                .FirstOrDefaultAsync(ct);

            if (user is null) return Error.NotFound("identity.user_not_found");

            // Permissions come from the token, not another query — they are already resolved for
            // this surface and re-deriving them here could disagree with what the token allows.
            return user with { Permissions = [.. currentUser.Permissions] };
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/", async (Handler handler, CancellationToken ct) =>
                (await handler.Handle(Unit.Value, ct)).ToHttpResult())
            .WithName("GetCurrentUser")
            .WithSummary("The signed-in user's profile and effective permissions.")
            .RequireAuthorization()
            .Produces<UserProfileResponse>();
}
