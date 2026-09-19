namespace Lifestyle.Infrastructure.Email;

/// <summary>
/// Bound from <c>Email:*</c>.
///
/// <para>
/// A blank <see cref="Host"/> is the "not configured" signal and selects the logging sender
/// instead of the SMTP one — see <c>InfrastructureModule.AddEmail</c>. That is deliberate: the
/// platform must boot and function without mail configured, because it did so for its whole first
/// phase, and a deployment that crashes at start-up over an unset SMTP host is worse than one
/// that tells you loudly in the log that a reset mail went nowhere.
/// </para>
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP host. Blank disables sending — see the class remarks.</summary>
    public string? Host { get; init; }

    public int Port { get; init; } = 587;

    /// <summary>
    /// STARTTLS. On by default because the credentials below are sent in the clear without it.
    /// Mailpit in local development wants this off — it speaks plain SMTP on 1025.
    /// </summary>
    public bool UseStartTls { get; init; } = true;

    public string? UserName { get; init; }

    public string? Password { get; init; }

    /// <summary>The envelope sender. Providers reject mail whose From is not a verified domain.</summary>
    public string FromAddress { get; init; } = "no-reply@mylifestylemart.com";

    public string FromName { get; init; } = "Lifestyle Mart";

    /// <summary>
    /// Seconds before a send is abandoned. Short on purpose: this runs inside a request, and a
    /// caller waiting 100 seconds on an unreachable relay has already given up and pressed the
    /// button again.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 15;
}
