using Lifestyle.Modules.Identity.Domain.Events;
using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Identity.Domain;

/// <summary>
/// One account per person, whatever surfaces they use (FRD §4.1). A buyer who becomes a vendor
/// keeps the same user; what changes is their memberships and roles.
/// </summary>
internal sealed class User : AggregateRoot, ISoftDeletable
{
    private readonly List<UserRole> _roles = [];
    private readonly List<RefreshToken> _refreshTokens = [];
    private readonly List<UserExternalLogin> _externalLogins = [];

    private User() { }

    public string Email { get; private set; } = null!;
    public bool EmailVerified { get; private set; }
    public string? PhoneNumber { get; private set; }
    public bool PhoneVerified { get; private set; }
    /// <summary>
    /// Null for an account that has only ever signed in through an external provider. Password
    /// login checks for null and refuses with the same generic error as a wrong password, so the
    /// response cannot be used to discover which accounts are Google-only.
    /// </summary>
    public string? PasswordHash { get; private set; }
    public string FullName { get; private set; } = null!;
    public UserStatus Status { get; private set; } = UserStatus.Active;

    /// <summary>Base32 TOTP secret. Mandatory for platform admins and vendor owners (FRD §4.2).</summary>
    public string? TwoFactorSecret { get; private set; }
    public bool TwoFactorEnabled { get; private set; }

    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockedOutUntil { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();
    public IReadOnlyCollection<UserExternalLogin> ExternalLogins => _externalLogins.AsReadOnly();

    /// <summary>True when this account can be signed into with a password at all.</summary>
    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash);

    public static User Register(string email, string passwordHash, string fullName, string? phoneNumber, DateTimeOffset now)
    {
        var user = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            FullName = fullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
            Status = UserStatus.Active,
            CreatedAt = now
        };

        user.Raise(new UserRegistered(user.Id, user.Email, user.FullName, now));
        return user;
    }

    /// <summary>
    /// An account created by signing in with an external provider. It has no password — the
    /// provider is the only way in until the owner sets one.
    /// </summary>
    public static User RegisterExternal(
        string email, string fullName, string provider, string subject, bool emailVerified, DateTimeOffset now)
    {
        var user = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = null,
            FullName = fullName.Trim(),
            EmailVerified = emailVerified,
            Status = UserStatus.Active,
            CreatedAt = now
        };

        user._externalLogins.Add(UserExternalLogin.Create(user.Id, provider, subject, now));
        user.Raise(new UserRegistered(user.Id, user.Email, user.FullName, now));
        return user;
    }

    /// <summary>
    /// Links an external identity to an existing account. Idempotent, so signing in twice does not
    /// accumulate duplicates.
    /// </summary>
    public void LinkExternalLogin(string provider, string subject, DateTimeOffset now)
    {
        if (_externalLogins.Any(l => l.Provider == provider && l.Subject == subject)) return;
        _externalLogins.Add(UserExternalLogin.Create(Id, provider, subject, now));
        UpdatedAt = now;
    }

    /// <summary>
    /// An external provider asserting a verified email is proof of control of that address, which
    /// is the same thing an email confirmation loop proves.
    /// </summary>
    public void MarkEmailVerifiedByProvider(DateTimeOffset now)
    {
        if (EmailVerified) return;
        EmailVerified = true;
        UpdatedAt = now;
    }

    public void AssignRole(Guid roleId, Guid? scopeId, DateTimeOffset now)
    {
        if (_roles.Any(r => r.RoleId == roleId && r.ScopeId == scopeId)) return;
        _roles.Add(UserRole.Create(Id, roleId, scopeId, now));
        UpdatedAt = now;
    }

    public void RemoveRole(Guid roleId, Guid? scopeId = null) =>
        _roles.RemoveAll(r => r.RoleId == roleId && r.ScopeId == scopeId);

    public bool IsLockedOut(DateTimeOffset now) => LockedOutUntil is { } until && until > now;

    /// <summary>
    /// Locks the account for 15 minutes after 5 consecutive failures. Deliberately independent of
    /// the per-identifier rate limit — that one throttles, this one stops credential stuffing.
    /// </summary>
    public void RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= 5)
        {
            LockedOutUntil = now.AddMinutes(15);
            FailedLoginAttempts = 0;
        }
        UpdatedAt = now;
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockedOutUntil = null;
        LastLoginAt = now;
        UpdatedAt = now;
    }

    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        UpdatedAt = now;
        // Changing a password invalidates every session — this is the whole point of the operation.
        foreach (var token in _refreshTokens.Where(t => t.IsActive(now)))
            token.Revoke(now, "password_changed");
    }

    public void VerifyEmail(DateTimeOffset now)
    {
        EmailVerified = true;
        UpdatedAt = now;
    }

    public void UpdateProfile(string fullName, string? phoneNumber, DateTimeOffset now)
    {
        var trimmedPhone = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        if (trimmedPhone != PhoneNumber) PhoneVerified = false;

        FullName = fullName.Trim();
        PhoneNumber = trimmedPhone;
        UpdatedAt = now;
    }

    public void EnableTwoFactor(string secret, DateTimeOffset now)
    {
        TwoFactorSecret = secret;
        TwoFactorEnabled = true;
        UpdatedAt = now;
    }

    public void DisableTwoFactor(DateTimeOffset now)
    {
        TwoFactorSecret = null;
        TwoFactorEnabled = false;
        UpdatedAt = now;
    }

    public void Suspend(DateTimeOffset now, string reason)
    {
        Status = UserStatus.Suspended;
        UpdatedAt = now;
        foreach (var token in _refreshTokens.Where(t => t.IsActive(now)))
            token.Revoke(now, reason);
        Raise(new UserSuspended(Id, reason, now));
    }

    public void Reinstate(DateTimeOffset now)
    {
        Status = UserStatus.Active;
        LockedOutUntil = null;
        FailedLoginAttempts = 0;
        UpdatedAt = now;
    }

    public RefreshToken IssueRefreshToken(string tokenHash, Guid familyId, DateTimeOffset now, TimeSpan lifetime, string? userAgent, string? ipAddress)
    {
        var token = RefreshToken.Issue(Id, tokenHash, familyId, now, now.Add(lifetime), userAgent, ipAddress);
        _refreshTokens.Add(token);
        return token;
    }

    /// <summary>
    /// Reuse of an already-rotated token means the token was stolen. Kill the whole family, not
    /// just the replayed token (FRD §4.2).
    /// </summary>
    public void RevokeTokenFamily(Guid familyId, DateTimeOffset now, string reason)
    {
        foreach (var token in _refreshTokens.Where(t => t.FamilyId == familyId && t.IsActive(now)))
            token.Revoke(now, reason);

        Raise(new RefreshTokenFamilyRevoked(Id, familyId, reason, now));
    }
}

internal enum UserStatus
{
    Active = 1,
    Suspended = 2,
    PendingVerification = 3
}
