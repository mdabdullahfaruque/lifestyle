namespace Lifestyle.Modules.Identity.Contracts;

/// <summary>
/// The only way another module may reach Identity. Returns DTOs, never entities
/// (docs/04 §3.6).
/// </summary>
public interface IIdentityModule
{
    Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<UserSummary>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);

    Task<bool> ExistsAsync(Guid userId, CancellationToken ct);

    /// <summary>Grants a vendor-scoped role. Called by Vendors when staff are added or a vendor is approved.</summary>
    Task GrantVendorRoleAsync(Guid userId, Guid vendorId, VendorRoleKind kind, CancellationToken ct);

    Task RevokeVendorRolesAsync(Guid userId, Guid vendorId, CancellationToken ct);
}

public enum VendorRoleKind
{
    Owner = 1,
    Staff = 2
}

public sealed record UserSummary(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    bool EmailVerified,
    bool IsActive);
