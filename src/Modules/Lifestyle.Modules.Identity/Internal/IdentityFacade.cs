using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Identity.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Identity.Internal;

/// <summary>
/// Implements the module's public contract. This is the only class other modules reach, and it
/// returns DTOs — never a <c>User</c> entity (docs/04 §3.6).
/// </summary>
internal sealed class IdentityFacade(IIdentityDbContext db, IClock clock) : IIdentityModule
{
    public async Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken ct) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSummary(u.Id, u.Email, u.FullName, u.PhoneNumber, u.EmailVerified,
                u.Status == Domain.UserStatus.Active))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return [];

        return await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email, u.FullName, u.PhoneNumber, u.EmailVerified,
                u.Status == Domain.UserStatus.Active))
            .ToListAsync(ct);
    }

    public Task<bool> ExistsAsync(Guid userId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId, ct);

    public async Task GrantVendorRoleAsync(Guid userId, Guid vendorId, VendorRoleKind kind, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException($"User {userId} does not exist.");

        var roleId = kind == VendorRoleKind.Owner ? SystemRoles.VendorOwnerId : SystemRoles.VendorStaffId;
        user.AssignRole(roleId, vendorId, clock.UtcNow);

        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeVendorRolesAsync(Guid userId, Guid vendorId, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null) return;

        user.RemoveRole(SystemRoles.VendorOwnerId, vendorId);
        user.RemoveRole(SystemRoles.VendorStaffId, vendorId);

        await db.SaveChangesAsync(ct);
    }
}
