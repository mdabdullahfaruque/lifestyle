namespace Lifestyle.Modules.Identity.Features.Auth;

/// <summary>
/// What a successful authentication returns. The refresh token is NOT in this body — it is set as
/// an HttpOnly cookie by the endpoint, because a token readable by JavaScript is a token an XSS
/// bug can steal (FRD §4.2).
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    int ExpiresInSeconds,
    string Surface,
    Guid? VendorId,
    UserProfileResponse User);

public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    bool EmailVerified,
    bool TwoFactorEnabled,
    IReadOnlyCollection<string> Permissions);

/// <summary>Returned when a login needs a second factor before an access token is issued.</summary>
public sealed record TwoFactorRequiredResponse(string ChallengeToken, string Method = "totp");
