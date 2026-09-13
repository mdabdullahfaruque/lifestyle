using System.Security.Cryptography;

namespace Lifestyle.Modules.Identity.Internal;

/// <summary>
/// The one-time password handed to an owner whose account an administrator created for them.
/// </summary>
internal static class TemporaryPassword
{
    // Ambiguous characters are left out on purpose: this password is read off a screen and typed
    // in by hand, often from a phone call, so 0/O and 1/l/I cost more in failed sign-ins than the
    // handful of bits they add. Twenty characters from this alphabet is ~103 bits either way.
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 20;

    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}
