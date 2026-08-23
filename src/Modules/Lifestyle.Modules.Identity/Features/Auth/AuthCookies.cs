using Microsoft.AspNetCore.Http;

namespace Lifestyle.Modules.Identity.Features.Auth;

/// <summary>
/// The refresh-token cookie. One place, so the flags cannot drift between login, refresh and
/// logout — a SameSite mismatch between those three is a subtle, expensive bug.
/// </summary>
internal static class AuthCookies
{
    public const string RefreshTokenName = "lf_rt";

    public static void SetRefreshToken(HttpResponse response, string value, DateTimeOffset expiresAt, bool secure) =>
        response.Cookies.Append(RefreshTokenName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Expires = expiresAt,
            Path = "/v1/auth",
            IsEssential = true
        });

    public static void Clear(HttpResponse response, bool secure) =>
        response.Cookies.Append(RefreshTokenName, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UnixEpoch,
            Path = "/v1/auth",
            IsEssential = true
        });

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(RefreshTokenName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
