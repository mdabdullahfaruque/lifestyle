namespace Lifestyle.SharedKernel.Http;

/// <summary>
/// Names of the rate-limit policies the host registers. Modules attach them to their own endpoint
/// groups by name (<c>.RequireRateLimiting(RateLimitPolicies.Auth)</c>); the limits themselves are
/// configuration, owned by the host (docs/05: FRD §19.5).
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Credential endpoints — login, register, TOTP. The brute-force surface.</summary>
    public const string Auth = "auth-per-ip";

    /// <summary>File uploads — expensive per request, easy to abuse.</summary>
    public const string Uploads = "uploads-per-ip";
}
