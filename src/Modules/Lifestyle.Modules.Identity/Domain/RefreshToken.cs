using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Identity.Domain;

/// <summary>
/// An opaque, rotating refresh token (FRD §4.2). Only the SHA-256 hash is stored — a database leak
/// must not hand out working sessions. Every use issues a successor and revokes this one;
/// presenting an already-rotated token means it was stolen, and revokes the whole family.
/// </summary>
internal sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 of the opaque token value. The value itself is never persisted.</summary>
    public string TokenHash { get; private set; } = null!;

    /// <summary>
    /// Groups a token with its ancestors and successors. Detecting reuse anywhere in the chain
    /// revokes every token that shares this id.
    /// </summary>
    public Guid FamilyId { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }

    /// <summary>Set when this token was rotated — points at its successor, for audit.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    public string? UserAgent { get; private set; }
    public string? IpAddress { get; private set; }

    public static RefreshToken Issue(
        Guid userId, string tokenHash, Guid familyId,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt,
        string? userAgent, string? ipAddress) => new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            UserAgent = Truncate(userAgent, 400),
            IpAddress = Truncate(ipAddress, 45)
        };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>True when this token was already rotated and is being presented again — theft.</summary>
    public bool IsReplayed(DateTimeOffset now) => RevokedAt is not null || ExpiresAt <= now;

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevokedReason = reason;
    }

    public void MarkRotated(Guid successorId, DateTimeOffset now)
    {
        ReplacedByTokenId = successorId;
        Revoke(now, "rotated");
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
