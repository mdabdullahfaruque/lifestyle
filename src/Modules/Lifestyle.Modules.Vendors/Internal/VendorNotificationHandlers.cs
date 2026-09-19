using System.Net;
using System.Text;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Vendors.Internal;

/// <summary>
/// Tells a shop owner what happened to their application.
///
/// <para>
/// Until these existed the platform decided someone's livelihood in silence: an applicant who was
/// approved had to keep signing in to notice, and one who was rejected was never told why — and so
/// could not fix it and reapply. The decision was already recorded; only the telling was missing.
/// </para>
///
/// <para>
/// These run off the outbox rather than inline in the review handler, which matters for a reason
/// worth stating: the email is then sent only if the approval actually committed. Sending inline
/// before <c>SaveChanges</c> risks congratulating someone whose approval then rolled back, and
/// sending after it risks losing the message if the process dies. The outbox already solves this,
/// and the retry it provides is the reason a transient SMTP failure is not a lost notification.
/// </para>
///
/// <para>
/// Delivery is at-least-once, so an owner may occasionally receive the same notice twice. That is
/// the right trade here — a duplicate "your shop is live" costs nothing, while the deduplication
/// state needed to prevent it would cost a table and a migration.
/// </para>
///
/// <para>
/// Mail lookups go through <see cref="IIdentityModule"/>: Vendors knows an owner's id, never their
/// address, and must not read Identity's tables to find one (docs/04 §3.6).
/// </para>
///
/// <para>
/// **Everything interpolated into the HTML body is encoded.** A shop's display name is chosen by
/// the vendor and a rejection reason is typed by a reviewer; both reach this template as raw text.
/// Scripts do not run in a mail client, but unencoded markup can still restructure the message —
/// including into a link that appears to come from us.
/// </para>
/// </summary>
internal sealed class EmailOwnerOnVendorApproved(
    IIdentityModule identity,
    IEmailSender email,
    IOptions<VendorsModuleOptions> options,
    ILogger<EmailOwnerOnVendorApproved> logger)
    : IIntegrationEventHandler<VendorApprovedEvent>
{
    private readonly VendorsModuleOptions _options = options.Value;

    public async Task Handle(VendorApprovedEvent integrationEvent, CancellationToken ct)
    {
        var owner = await identity.GetUserAsync(integrationEvent.OwnerUserId, ct);

        if (owner is null)
        {
            logger.LogWarning(
                "Vendor {VendorId} was approved but its owner {OwnerUserId} no longer exists; no email sent.",
                integrationEvent.VendorId, integrationEvent.OwnerUserId);
            return;
        }

        var shopUrl = _options.ShopUrlFor(integrationEvent.Slug);
        var console = _options.SellerConsoleUrl;

        var text = new StringBuilder()
            .Append("Your shop ").Append(integrationEvent.DisplayName).AppendLine(" has been approved.")
            .AppendLine()
            .AppendLine("You can now add products and start selling.");

        if (shopUrl is not null) text.AppendLine().Append("Your shop address: ").AppendLine(shopUrl);
        if (console is not null) text.AppendLine().Append("Manage your shop: ").AppendLine(console);

        text.AppendLine()
            .AppendLine("— — —")
            .AppendLine()
            .Append("আপনার দোকান ").Append(integrationEvent.DisplayName).AppendLine(" অনুমোদিত হয়েছে।")
            .AppendLine()
            .AppendLine("এখন আপনি পণ্য যোগ করে বিক্রি শুরু করতে পারেন।");

        var name = WebUtility.HtmlEncode(integrationEvent.DisplayName);

        var html = new StringBuilder()
            .Append("<div style=\"font-family:system-ui,-apple-system,sans-serif;max-width:32rem\">")
            .Append("<h2 style=\"margin:0 0 1rem\">Your shop has been approved</h2>")
            .Append("<p><strong>").Append(name)
            .Append("</strong> is live. You can now add products and start selling.</p>");

        if (shopUrl is not null)
        {
            var encoded = WebUtility.HtmlEncode(shopUrl);
            html.Append("<p>Your shop address:<br><a href=\"").Append(encoded).Append("\">")
                .Append(encoded).Append("</a></p>");
        }

        if (console is not null)
        {
            html.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(console))
                .Append("\">Manage your shop</a></p>");
        }

        html.Append("<hr style=\"border:none;border-top:1px solid #e5e7eb;margin:1.5rem 0\">")
            .Append("<h2 style=\"margin:0 0 1rem\">আপনার দোকান অনুমোদিত হয়েছে</h2>")
            .Append("<p><strong>").Append(name)
            .Append("</strong> এখন সক্রিয়। আপনি পণ্য যোগ করে বিক্রি শুরু করতে পারেন।</p>")
            .Append("</div>");

        await email.SendAsync(
            new EmailMessage(owner.Email, $"Your shop {integrationEvent.DisplayName} is approved",
                html.ToString(), text.ToString()),
            ct);
    }
}

/// <summary>
/// Tells an applicant their shop was rejected, and what the reviewer said. See
/// <see cref="EmailOwnerOnVendorApproved"/> for why this runs off the outbox and why every
/// interpolated value is encoded.
/// </summary>
internal sealed class EmailOwnerOnVendorRejected(
    IIdentityModule identity,
    IEmailSender email,
    IOptions<VendorsModuleOptions> options,
    ILogger<EmailOwnerOnVendorRejected> logger)
    : IIntegrationEventHandler<VendorRejectedEvent>
{
    private readonly VendorsModuleOptions _options = options.Value;

    public async Task Handle(VendorRejectedEvent integrationEvent, CancellationToken ct)
    {
        var owner = await identity.GetUserAsync(integrationEvent.OwnerUserId, ct);

        if (owner is null)
        {
            logger.LogWarning(
                "Vendor {VendorId} was rejected but its owner {OwnerUserId} no longer exists; no email sent.",
                integrationEvent.VendorId, integrationEvent.OwnerUserId);
            return;
        }

        // The reason is the entire point of the message: it is what the applicant has to act on.
        var console = _options.SellerConsoleUrl;

        var text = new StringBuilder()
            .Append("Your application for ").Append(integrationEvent.DisplayName)
            .AppendLine(" was not approved.")
            .AppendLine()
            .AppendLine("Reason:")
            .AppendLine(integrationEvent.Reason)
            .AppendLine()
            .AppendLine("You can correct this and submit the application again.");

        if (console is not null) text.AppendLine().Append("Your application: ").AppendLine(console);

        text.AppendLine()
            .AppendLine("— — —")
            .AppendLine()
            .Append("আপনার ").Append(integrationEvent.DisplayName)
            .AppendLine(" এর আবেদন অনুমোদিত হয়নি।")
            .AppendLine()
            .AppendLine("কারণ:")
            .AppendLine(integrationEvent.Reason)
            .AppendLine()
            .AppendLine("আপনি সংশোধন করে আবার আবেদন করতে পারেন।");

        var name = WebUtility.HtmlEncode(integrationEvent.DisplayName);
        var reason = WebUtility.HtmlEncode(integrationEvent.Reason);

        var html = new StringBuilder()
            .Append("<div style=\"font-family:system-ui,-apple-system,sans-serif;max-width:32rem\">")
            .Append("<h2 style=\"margin:0 0 1rem\">Your application was not approved</h2>")
            .Append("<p>We could not approve <strong>").Append(name).Append("</strong> yet.</p>")
            .Append("<p><strong>Reason:</strong><br>").Append(reason).Append("</p>")
            .Append("<p>You can correct this and submit the application again.</p>");

        if (console is not null)
        {
            html.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(console))
                .Append("\">Open your application</a></p>");
        }

        html.Append("<hr style=\"border:none;border-top:1px solid #e5e7eb;margin:1.5rem 0\">")
            .Append("<h2 style=\"margin:0 0 1rem\">আপনার আবেদন অনুমোদিত হয়নি</h2>")
            .Append("<p><strong>কারণ:</strong><br>").Append(reason).Append("</p>")
            .Append("<p>আপনি সংশোধন করে আবার আবেদন করতে পারেন।</p>")
            .Append("</div>");

        await email.SendAsync(
            new EmailMessage(owner.Email, $"About your application for {integrationEvent.DisplayName}",
                html.ToString(), text.ToString()),
            ct);
    }
}
