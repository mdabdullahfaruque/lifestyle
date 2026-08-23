using System.Globalization;
using System.Security.Cryptography;
using OtpNet;

namespace Lifestyle.Modules.Identity.Internal;

internal interface ITotpService
{
    string GenerateSecret();

    /// <summary>The <c>otpauth://</c> URI an authenticator app scans.</summary>
    string BuildProvisioningUri(string secret, string email, string issuer);

    bool Verify(string secret, string code);
}

/// <summary>
/// RFC 6238 TOTP. Mandatory for platform admins and vendor owners (FRD §4.2).
/// A one-step window either side absorbs clock drift without meaningfully widening the attack window.
/// </summary>
internal sealed class TotpService : ITotpService
{
    public string GenerateSecret() => Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));

    public string BuildProvisioningUri(string secret, string email, string issuer) =>
        string.Create(CultureInfo.InvariantCulture,
            $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30");

    public bool Verify(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;

        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(secret));
            return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch (ArgumentException)
        {
            // A malformed stored secret must read as "wrong code", not as a 500.
            return false;
        }
    }
}
