using Lifestyle.SharedKernel.Results;

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

    Task<UserSummary?> FindByEmailAsync(string email, CancellationToken ct);

    /// <summary>
    /// Creates an account on someone's behalf, for a shop an administrator onboards directly.
    /// <para>
    /// Returns the generated password once, because the administrator has to hand it over — there
    /// is no email sender yet. It is never stored in readable form and never logged; if it is lost,
    /// the owner resets it rather than anyone looking it up.
    /// </para>
    /// </summary>
    Task<Result<CreatedUser>> CreateForVendorOwnerAsync(
        string email, string fullName, string? phoneNumber, CancellationToken ct);

    /// <summary>Grants a vendor-scoped role. Called by Vendors when staff are added or a vendor is approved.</summary>
    Task GrantVendorRoleAsync(Guid userId, Guid vendorId, VendorRoleKind kind, CancellationToken ct);

    Task RevokeVendorRolesAsync(Guid userId, Guid vendorId, CancellationToken ct);
}

/// <summary>A newly provisioned account, with the one-time password to pass on.</summary>
public sealed record CreatedUser(Guid UserId, string TemporaryPassword);

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
