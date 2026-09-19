using Lifestyle.SharedKernel.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lifestyle.Infrastructure.Email;

/// <summary>
/// The sender used when <c>Email:Host</c> is blank.
///
/// <para>
/// It exists so that "mail is not configured" degrades to something visible instead of something
/// silent. Every message is logged at Warning, so an operator who wonders why no vendor ever
/// receives a reset link finds the answer in the log rather than in the source.
/// </para>
///
/// <para>
/// **Only outside production does it log the body.** A reset link is a bearer credential for the
/// account, and writing one into a production log puts it in every place logs are shipped, read
/// and retained. In development that is exactly what you want — it is how you complete a reset
/// with no mail server running — so the environment decides.
/// </para>
/// </summary>
internal sealed class LoggingEmailSender(
    IHostEnvironment environment,
    ILogger<LoggingEmailSender> logger)
    : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (environment.IsProduction())
        {
            logger.LogWarning(
                "Email is not configured (Email:Host is blank), so {Subject} to {Recipient} was not sent.",
                message.Subject, message.To);
        }
        else
        {
            logger.LogWarning(
                "Email is not configured, so {Subject} to {Recipient} was not sent. Body follows:\n{Body}",
                message.Subject, message.To, message.TextBody);
        }

        return Task.CompletedTask;
    }
}
