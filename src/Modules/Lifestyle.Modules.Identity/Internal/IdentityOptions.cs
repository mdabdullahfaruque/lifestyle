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

    /// <summary>
    /// Google OAuth 2.0 **web client id**, the audience every Google ID token we accept must carry.
    /// Blank disables Google sign-in entirely: the endpoint answers
    /// <c>identity.google_not_configured</c> rather than half-working.
    /// <para>
    /// Public by nature — it ships in the browser bundle — so it is configuration, not a secret.
    /// It still must be exact: accepting a token minted for a different Google app would let that
    /// app's owner sign in as any of our users.
    /// </para>
    /// </summary>
    public string? GoogleClientId { get; init; }

    /// <summary>
    /// How long a password-reset link stays valid. An hour is the usual balance: long enough to
    /// survive a message sitting unread through a meeting, short enough that a link left in an
    /// inbox is not a standing key to the account.
    /// </summary>
    [Range(5, 1440)]
    public int PasswordResetMinutes { get; init; } = 60;

    /// <summary>
    /// Where a reset link should land, keyed by surface ("buyer", "seller", "admin") — the three
    /// consoles are different origins, so one URL cannot serve them.
    /// <para>
    /// A surface with no entry configured cannot send reset mail at all: the endpoint refuses
    /// rather than mailing a link to nowhere, because a reset link that 404s is indistinguishable
    /// to the recipient from the account being broken.
    /// </para>
    /// </summary>
    public Dictionary<string, string> PasswordResetUrls { get; init; } = [];
}
