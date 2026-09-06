using System.ComponentModel.DataAnnotations;

namespace Lifestyle.Modules.Identity.Internal;

/// <summary>Bound from <c>Identity:*</c> and validated at startup — a bad JWT key must fail fast.</summary>
public sealed class IdentityModuleOptions
{
    public const string SectionName = "Identity";

    [Required]
    public required string JwtIssuer { get; init; }

    [Required]
    public required string JwtAudienceBase { get; init; }

    /// <summary>
    /// HMAC signing key. The FRD calls for RS256 in production; HS256 keeps local development and
    /// tests free of key material. Must be at least 32 bytes.
    /// </summary>
    [Required, MinLength(32)]
    public required string JwtSigningKey { get; init; }

    [Range(1, 120)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 30;

    [Range(8, 128)]
    public int MinimumPasswordLength { get; init; } = 10;

    /// <summary>
    /// Whether the admin surface refuses an account that has not enrolled in TOTP (FRD §4.2).
    /// <para>
    /// Defaults to <c>true</c>, which is the intended production posture: a platform admin can
    /// approve vendors, moderate the catalogue and take shops down, so a stolen admin password
    /// alone must not be enough to do any of it. Setting this to <c>false</c> is a deliberate,
    /// temporary convenience for a deployment that holds no real vendor or buyer data yet, and
    /// must be turned back on before it does.
    /// </para>
    /// <para>
    /// This governs the mandatory-enrolment gate, not two-factor itself: an account that has
    /// enrolled is still asked for its code either way.
    /// </para>
    /// </summary>
    public bool RequireTwoFactorOnAdmin { get; init; } = true;
}
