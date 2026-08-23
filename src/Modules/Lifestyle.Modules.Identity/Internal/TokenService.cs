using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Lifestyle.Modules.Identity.Domain;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Lifestyle.Modules.Identity.Internal;

internal interface ITokenService
{
    string CreateAccessToken(User user, string surface, IReadOnlyCollection<string> permissions, Guid? vendorId, Guid? impersonatedBy = null);

    /// <summary>Returns the opaque value to hand the client, and the hash to store.</summary>
    (string Value, string Hash) CreateRefreshToken();

    string HashRefreshToken(string value);

    string AudienceFor(string surface);
}

internal sealed class TokenService(IOptions<IdentityModuleOptions> options, IClock clock) : ITokenService
{
    public const string VendorIdClaim = "vendor_id";
    public const string PermissionClaim = "perm";
    public const string ImpersonatorClaim = "act";
    public const string SurfaceClaim = "surface";

    private readonly IdentityModuleOptions _options = options.Value;

    public string CreateAccessToken(
        User user, string surface, IReadOnlyCollection<string> permissions, Guid? vendorId, Guid? impersonatedBy = null)
    {
        // IClock, not DateTime.UtcNow: token lifetimes must be controllable in tests, and an
        // expiry that cannot be simulated is an expiry that never gets tested.
        var now = clock.UtcNow.UtcDateTime;

        var identity = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new Claim(SurfaceClaim, surface),
            new Claim("name", user.FullName)
        ]);

        if (vendorId is { } vid) identity.AddClaim(new Claim(VendorIdClaim, vid.ToString()));
        if (impersonatedBy is { } actor) identity.AddClaim(new Claim(ImpersonatorClaim, actor.ToString()));

        // One claim per permission. Compacting to a bitmask (FRD §4.2) is a later optimisation —
        // it only pays off once the set is large, and it makes tokens much harder to debug.
        foreach (var permission in permissions)
            identity.AddClaim(new Claim(PermissionClaim, permission));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.JwtIssuer,
            Audience = AudienceFor(surface),
            Subject = identity,
            NotBefore = now,
            IssuedAt = now,
            Expires = now.AddMinutes(_options.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>
    /// 256 bits of CSPRNG output, base64url. Opaque — it carries no claims, so revocation is a
    /// database fact rather than a decoding problem.
    /// </summary>
    public (string Value, string Hash) CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var value = Base64UrlEncoder.Encode(bytes);
        return (value, HashRefreshToken(value));
    }

    public string HashRefreshToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public string AudienceFor(string surface) =>
        string.Create(CultureInfo.InvariantCulture, $"{_options.JwtAudienceBase}:{surface}");
}
