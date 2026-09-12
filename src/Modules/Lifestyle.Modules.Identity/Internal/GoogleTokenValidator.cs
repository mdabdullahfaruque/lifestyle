using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Lifestyle.Modules.Identity.Internal;

/// <summary>What a validated Google ID token tells us about the person holding it.</summary>
internal sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name);

internal interface IGoogleTokenValidator
{
    Task<Result<GoogleIdentity>> ValidateAsync(string idToken, CancellationToken ct);
}

/// <summary>
/// Verifies a Google ID token.
///
/// <para>
/// The token is a JWT signed by Google, so this checks the signature against Google's published
/// keys and then the claims. Every one of those checks matters, and skipping any turns "sign in
/// with Google" into "sign in as anybody":
/// </para>
/// <list type="bullet">
///   <item>Signature — without it the token is just a base64 string the caller wrote.</item>
///   <item><c>aud</c> equals our client id — a token minted for a *different* Google app is still
///   validly signed by Google. Accepting one lets the owner of any other Google app log in as any
///   of our users.</item>
///   <item><c>iss</c> is Google — pinned rather than read from the token.</item>
///   <item>Expiry, with almost no clock skew allowed.</item>
///   <item><c>email_verified</c> — an unverified Google address proves nothing about who owns it,
///   and this is the claim that makes auto-linking to an existing account safe.</item>
/// </list>
///
/// <para>
/// Google's signing keys rotate. They are cached for an hour rather than fetched per sign-in — a
/// network round trip on every login is both slow and a hard dependency on Google being reachable
/// at that instant — and a token whose key id is unknown forces one refetch before being rejected,
/// so a rotation does not lock users out until the cache expires.
/// </para>
/// </summary>
internal sealed class GoogleTokenValidator(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IOptions<IdentityModuleOptions> options,
    ILogger<GoogleTokenValidator> logger)
    : IGoogleTokenValidator
{
    private const string JwksUri = "https://www.googleapis.com/oauth2/v3/certs";
    private const string CacheKey = "identity:google:jwks";

    // Google mints tokens with either issuer; both are legitimate and long-standing.
    private static readonly string[] Issuers = ["https://accounts.google.com", "accounts.google.com"];

    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result<GoogleIdentity>> ValidateAsync(string idToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.GoogleClientId))
        {
            return Error.Validation("identity.google_not_configured",
                "Google sign-in is not configured for this deployment.");
        }

        var keys = await GetSigningKeysAsync(refresh: false, ct);
        var result = await ValidateAgainstAsync(idToken, keys);

        // An unknown key id usually means Google rotated. Refetch once before giving up, so a
        // rotation is a single slow request rather than an outage.
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            keys = await GetSigningKeysAsync(refresh: true, ct);
            result = await ValidateAgainstAsync(idToken, keys);
        }

        if (!result.IsValid)
        {
            logger.LogWarning(result.Exception, "Rejected a Google ID token.");
            return Error.Unauthorized("identity.google_token_invalid", "That Google sign-in could not be verified.");
        }

        var claims = result.ClaimsIdentity;
        var subject = claims.FindFirst("sub")?.Value;
        var email = claims.FindFirst("email")?.Value;
        var verified = string.Equals(claims.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        var name = claims.FindFirst("name")?.Value;

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            return Error.Unauthorized("identity.google_token_invalid", "That Google sign-in could not be verified.");
        }

        if (!verified)
        {
            // Refused rather than treated as a weaker sign-in: an unverified address is exactly the
            // one an attacker would claim in order to be linked to someone else's account.
            return Error.Forbidden("identity.google_email_unverified",
                "This Google account's email address is not verified, so it cannot be used to sign in.");
        }

        return new GoogleIdentity(subject, email.Trim().ToLowerInvariant(), true, name);
    }

    private Task<TokenValidationResult> ValidateAgainstAsync(string idToken, IEnumerable<SecurityKey> keys) =>
        new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = Issuers,
            ValidateAudience = true,
            ValidAudience = _options.GoogleClientId,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            ValidateLifetime = true,
            // Google tokens are short-lived and both clocks are NTP-synced; a generous skew here
            // would only widen the window in which a stolen token still works.
            ClockSkew = TimeSpan.FromSeconds(30),
        });

    private async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(bool refresh, CancellationToken ct)
    {
        if (!refresh && cache.TryGetValue(CacheKey, out IReadOnlyCollection<SecurityKey>? cached) && cached is not null)
        {
            return cached;
        }

        var http = httpClientFactory.CreateClient(nameof(GoogleTokenValidator));
        var jwks = await http.GetFromJsonAsync<GoogleJwks>(JwksUri, ct)
                   ?? new GoogleJwks([]);

        var keys = jwks.Keys
            .Select(k => new JsonWebKey
            {
                Kid = k.Kid,
                Kty = k.Kty,
                N = k.N,
                E = k.E,
                Alg = k.Alg,
                Use = k.Use,
            })
            .Cast<SecurityKey>()
            .ToList();

        cache.Set(CacheKey, (IReadOnlyCollection<SecurityKey>)keys, TimeSpan.FromHours(1));
        return keys;
    }

    private sealed record GoogleJwks([property: JsonPropertyName("keys")] IReadOnlyList<GoogleJwk> Keys);

    private sealed record GoogleJwk(
        [property: JsonPropertyName("kid")] string Kid,
        [property: JsonPropertyName("kty")] string Kty,
        [property: JsonPropertyName("n")] string N,
        [property: JsonPropertyName("e")] string E,
        [property: JsonPropertyName("alg")] string? Alg,
        [property: JsonPropertyName("use")] string? Use);
}
