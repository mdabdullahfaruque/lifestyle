using Lifestyle.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Lifestyle.Modules.Identity.Internal;

internal interface IPasswordService
{
    string Hash(string password);

    /// <summary>
    /// Verifies and reports whether the stored hash used outdated parameters, so we can silently
    /// upgrade it on the next successful login.
    /// </summary>
    PasswordVerificationResult Verify(string hash, string password);
}

/// <summary>
/// Wraps ASP.NET Core's <see cref="PasswordHasher{TUser}"/> (PBKDF2-HMAC-SHA512, 210k iterations
/// in V3). We take the hasher only — not the rest of the Identity stack, whose EF schema and
/// UserManager we do not want (docs/04 §1.2).
/// </summary>
internal sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public PasswordVerificationResult Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(null!, hash, password);
}
