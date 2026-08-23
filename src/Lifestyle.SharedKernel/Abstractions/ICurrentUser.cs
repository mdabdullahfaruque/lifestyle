namespace Lifestyle.SharedKernel.Abstractions;

/// <summary>
/// The authenticated caller, projected from the JWT. Feature code depends on this, never on
/// <c>HttpContext</c>.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }

    /// <summary>Which surface the token was issued for: <c>buyer</c>, <c>seller</c> or <c>admin</c>.</summary>
    string? Audience { get; }

    /// <summary>The vendor this caller acts for on the seller surface, if any.</summary>
    Guid? VendorId { get; }

    /// <summary>Set when an admin is impersonating (BR-A-03) — the real actor behind the action.</summary>
    Guid? ImpersonatedBy { get; }

    IReadOnlySet<string> Permissions { get; }

    bool HasPermission(string permission);
}
