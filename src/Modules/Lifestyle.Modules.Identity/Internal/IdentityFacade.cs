using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Identity.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Identity.Internal;

/// <summary>
/// Implements the module's public contract. This is the only class other modules reach, and it
/// returns DTOs — never a <c>User</c> entity (docs/04 §3.6).
/// </summary>
internal sealed class IdentityFacade(IIdentityDbContext db, IClock clock, IPasswordService passwords) : IIdentityModule
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

    public async Task<UserSummary?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var normalised = email.Trim().ToLowerInvariant();

        return await db.Users
            .AsNoTracking()
            .Where(u => u.Email == normalised)
            .Select(u => new UserSummary(u.Id, u.Email, u.FullName, u.PhoneNumber, u.EmailVerified,
                u.Status == Domain.UserStatus.Active))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Result<CreatedUser>> CreateForVendorOwnerAsync(
        string email, string fullName, string? phoneNumber, CancellationToken ct)
    {
        var normalisedEmail = email.Trim().ToLowerInvariant();
        var normalisedPhone = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();

        // Email *and* phone are unique on users. Checking both here, where the constraints are,
        // keeps the caller from having to know that — and turns what was a bare unique violation
        // surfacing as "conflict.duplicate" into something an admin can act on.
        if (await db.Users.AnyAsync(u => u.Email == normalisedEmail, ct))
            return Error.Conflict("identity.email_taken", "An account with this email already exists.");

        if (normalisedPhone is not null
            && await db.Users.AnyAsync(u => u.PhoneNumber == normalisedPhone, ct))
        {
            return Error.Conflict("identity.phone_taken",
                "Another account already uses that phone number.");
        }

        var now = clock.UtcNow;
        var password = TemporaryPassword.Generate();

        var user = Domain.User.Register(normalisedEmail, passwords.Hash(password), fullName, normalisedPhone, now);

        // Buyer only. The vendor-owner role is granted when the shop is approved, by the same call
        // the review queue uses — so there is one path that makes someone a seller, not two.
        user.AssignRole(SystemRoles.BuyerId, null, now);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return new CreatedUser(user.Id, password);
    }

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
