namespace Lifestyle.SharedKernel.Abstractions;

/// <summary>
/// One outbound message. Both bodies are supplied: mail clients that refuse HTML — and the
/// spam filters that score a message with no text alternative — are common enough that sending
/// HTML alone costs deliverability on exactly the mail that must arrive.
/// </summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Sends transactional mail. Implemented in <c>Infrastructure/Email/</c>, which is where anything
/// that talks to the world outside the process lives.
///
/// <para>
/// It began in <c>Identity/Internal/</c> when password reset was the only thing that sent mail, and
/// moved here the moment Vendors needed it too — which is exactly what the SharedKernel admission
/// rule prescribes (docs/04 §3.2: two or more modules, no dependency on any module).
/// </para>
///
/// <para>
/// Sending must not throw for an ordinary delivery failure. A password-reset request that 500s
/// because an SMTP host was briefly unreachable tells the caller their account is broken, and —
/// worse on an endpoint that must not leak account existence — makes a failure distinguishable
/// from a success. Implementations log and return.
/// </para>
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
