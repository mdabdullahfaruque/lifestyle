using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Identity.Domain;

/// <summary>
/// An identity a user can sign in with that we do not issue ourselves — today only Google.
/// <para>
/// Stored as a row per link rather than a column on <see cref="User"/> so a second provider costs
/// no schema change, and so the uniqueness that matters — one account per (provider, subject) —
/// can be a database constraint rather than a check someone has to remember to write.
/// </para>
/// <para>
/// The subject is the provider's stable id for the person, never their email. Google's `sub` is
/// immutable; an email address can be changed, and re-assigned by a Workspace admin to somebody
/// else entirely. Keying on email would hand the second person the first person's account.
/// </para>
/// </summary>
internal sealed class UserExternalLogin : Entity
{
    private UserExternalLogin() { }

    public Guid UserId { get; private set; }

    /// <summary>Lower-case provider key, e.g. <c>google</c>.</summary>
    public string Provider { get; private set; } = null!;

    /// <summary>The provider's immutable subject identifier.</summary>
    public string Subject { get; private set; } = null!;

    public DateTimeOffset LinkedAt { get; private set; }

    // Entity assigns a time-ordered v7 id on construction; no id is set here.
    public static UserExternalLogin Create(Guid userId, string provider, string subject, DateTimeOffset now) => new()
    {
        UserId = userId,
        Provider = provider.Trim().ToLowerInvariant(),
        Subject = subject.Trim(),
        LinkedAt = now
    };
}

/// <summary>Providers we accept. A closed set, so a typo cannot silently create a new one.</summary>
internal static class ExternalProviders
{
    public const string Google = "google";
}
