using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lifestyle.Infrastructure.Email;

/// <summary>
/// Sends over SMTP, which every provider worth using speaks — SendGrid, Mailgun, Resend, Amazon
/// SES and a plain company mailbox all accept the same configuration, so choosing one later is an
/// <c>Email:*</c> change and not a code change.
///
/// <para>
/// <c>System.Net.Mail.SmtpClient</c> rather than MailKit: MailKit is the better library for the
/// hard cases (OAuth2, pipelining, IMAP), none of which apply to "send one short transactional
/// message", and it is not worth a new dependency here. Revisit if a provider ever requires
/// XOAUTH2.
/// </para>
/// </summary>
internal sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger)
    : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        // CA2000 wants deterministic disposal of both, and SmtpClient holds a socket.
        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = message.Subject,
            // The plain-text part is the body proper; the HTML rides as an alternative view. A
            // message with no text part scores worse with spam filters, and this is mail that has
            // to arrive.
            Body = message.TextBody,
            IsBodyHtml = false,
        };

        mail.To.Add(message.To);

        var html = AlternateView.CreateAlternateViewFromString(
            message.HtmlBody, null, MediaTypeNames.Text.Html);

        // The view owns an unmanaged stream; MailMessage disposes its views, but only once it is
        // itself disposed — adding it after construction keeps that ownership chain intact.
        mail.AlternateViews.Add(html);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseStartTls,
            Timeout = _options.TimeoutSeconds * 1000,
        };

        if (!string.IsNullOrWhiteSpace(_options.UserName))
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);

        try
        {
            await client.SendMailAsync(mail, ct);
            logger.LogInformation("Sent {Subject} to {Recipient}.", message.Subject, message.To);
        }
        catch (OperationCanceledException)
        {
            // The caller gave up or the host is shutting down — not a delivery failure, and not
            // ours to swallow.
            throw;
        }
        catch (Exception ex)
        {
            // Deliberately broad, and deliberately not rethrown (see IEmailSender's remarks): the
            // one caller that matters is password reset, where surfacing a failure to the client
            // would turn "we could not reach the relay" into a probe for which addresses exist.
            logger.LogError(ex, "Could not send {Subject} to {Recipient}.", message.Subject, message.To);
        }
    }
}
