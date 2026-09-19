using System.Globalization;
using System.Text;
using FluentValidation;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Identity.Domain;
using Lifestyle.Modules.Identity.Internal;
using Lifestyle.Modules.Identity.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Lifestyle.Modules.Identity.Features.Auth;

/// <summary>
/// Starts a password reset by mailing a single-use link.
///
/// <para>
/// **The response never varies.** Unknown address, suspended account, mail relay down — all answer
/// 202. An endpoint that says "no such account" is a free membership oracle: point it at a list of
/// addresses and it tells you which ones bank, shop or sell here. <c>Register</c> makes the
/// opposite call deliberately, because refusing to say "that email is taken" only confuses someone
/// signing up; here there is no honest user who benefits from the distinction.
/// </para>
///
/// <para>
/// The token is stored the way a refresh token is: the value goes in the mail, only its SHA-256
/// hash is kept, and the key *is* that hash so redeeming it is a single lookup. Whoever reads the
/// store therefore cannot reset anybody's password with what they find there. It lives in
/// <see cref="IDistributedCache"/> — Redis in production — following <c>EnrolTwoFactor</c>, which
/// keeps enrolment secrets the same way. That is why this feature needs no migration.
/// </para>
///
/// <para>
/// An account with no password (Google-only) is deliberately **not** excluded: this is the one
/// route by which such an owner can set a first password, and they need it most precisely when
/// Google sign-in is what stopped working for them.
/// </para>
/// </summary>
internal static class ForgotPassword
{
    public sealed record Request(string Email, string Surface);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(320);
            RuleFor(r => r.Surface)
                .NotEmpty()
                .Must(Surfaces.All.Contains)
                .WithMessage("Surface must be one of: buyer, seller, admin.");
        }
    }

    internal static string CacheKey(string tokenHash) => $"pwd-reset:{tokenHash}";

    internal sealed class Handler(
        IIdentityDbContext db,
        ITokenService tokens,
        IEmailSender email,
        IDistributedCache cache,
        IOptions<IdentityModuleOptions> options)
        : IHandler<Request, Result>
    {
        private readonly IdentityModuleOptions _options = options.Value;

        public async Task<Result> Handle(Request request, CancellationToken ct)
        {
            // Configuration, not account state — safe to report, and the alternative is mailing a
            // link to a console that does not exist.
            if (!_options.PasswordResetUrls.TryGetValue(request.Surface, out var resetUrl)
                || string.IsNullOrWhiteSpace(resetUrl))
            {
                return Error.Validation("identity.password_reset_unavailable",
                    "Password reset is not configured for this application.");
            }

            var address = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address, ct);

            // Every early return below is Success. See the class remarks.
            if (user is null || user.Status == UserStatus.Suspended)
                return Result.Success();

            var (value, hash) = tokens.CreateSingleUseToken();

            await cache.SetStringAsync(
                CacheKey(hash),
                user.Id.ToString(),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.PasswordResetMinutes),
                },
                ct);

            var link = $"{resetUrl.TrimEnd('/')}?token={Uri.EscapeDataString(value)}";
            await email.SendAsync(Compose(address, link, _options.PasswordResetMinutes), ct);

            return Result.Success();
        }

        /// <summary>
        /// Bengali and English in one message, rather than a language chosen for the reader. We do
        /// not record a preferred language on the account, and guessing from a mail header would
        /// get it wrong for exactly the bilingual audience this serves — so both are present and
        /// the reader picks.
        /// </summary>
        private static EmailMessage Compose(string address, string link, int minutes)
        {
            var validFor = minutes.ToString(CultureInfo.InvariantCulture);

            var text = new StringBuilder()
                .AppendLine("Reset your Lifestyle Mart password")
                .AppendLine()
                .AppendLine("Open this link to choose a new password:")
                .AppendLine(link)
                .AppendLine()
                .Append("The link works once and expires in ").Append(validFor).AppendLine(" minutes.")
                .AppendLine("If you did not ask for this, you can ignore this email — nothing has changed.")
                .AppendLine()
                .AppendLine("— — —")
                .AppendLine()
                .AppendLine("আপনার Lifestyle Mart পাসওয়ার্ড পরিবর্তন করুন")
                .AppendLine()
                .AppendLine("নতুন পাসওয়ার্ড দিতে এই লিঙ্কটি খুলুন:")
                .AppendLine(link)
                .AppendLine()
                .Append("লিঙ্কটি একবারই কাজ করবে এবং ").Append(validFor).AppendLine(" মিনিট পরে মেয়াদ শেষ হবে।")
                .AppendLine("আপনি যদি এটি না চেয়ে থাকেন, এই ইমেলটি উপেক্ষা করতে পারেন — কিছুই পরিবর্তন হয়নি।")
                .ToString();

            var html = new StringBuilder()
                .Append("<div style=\"font-family:system-ui,-apple-system,sans-serif;max-width:32rem\">")
                .Append("<h2 style=\"margin:0 0 1rem\">Reset your Lifestyle Mart password</h2>")
                .Append("<p>Open this link to choose a new password:</p>")
                .Append("<p><a href=\"").Append(link).Append("\">Choose a new password</a></p>")
                .Append("<p>The link works once and expires in ").Append(validFor).Append(" minutes. ")
                .Append("If you did not ask for this, you can ignore this email — nothing has changed.</p>")
                .Append("<hr style=\"border:none;border-top:1px solid #e5e7eb;margin:1.5rem 0\">")
                .Append("<h2 style=\"margin:0 0 1rem\">আপনার Lifestyle Mart পাসওয়ার্ড পরিবর্তন করুন</h2>")
                .Append("<p>নতুন পাসওয়ার্ড দিতে এই লিঙ্কটি খুলুন:</p>")
                .Append("<p><a href=\"").Append(link).Append("\">নতুন পাসওয়ার্ড দিন</a></p>")
                .Append("<p>লিঙ্কটি একবারই কাজ করবে এবং ").Append(validFor).Append(" মিনিট পরে মেয়াদ শেষ হবে। ")
                .Append("আপনি যদি এটি না চেয়ে থাকেন, এই ইমেলটি উপেক্ষা করতে পারেন — কিছুই পরিবর্তন হয়নি।</p>")
                .Append("</div>")
                .ToString();

            return new EmailMessage(address, "Reset your Lifestyle Mart password", html, text);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/forgot-password", async (Request request, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(request, ct);
                return result.IsFailure
                    ? ResultExtensions.Problem(result.Error)
                    : Microsoft.AspNetCore.Http.Results.Accepted();
            })
            .WithName("ForgotPassword")
            .WithSummary("Send a password-reset link. Always accepted, whether or not the account exists.")
            .AllowAnonymous()
            .Validate<Request>()
            .Produces(StatusCodes.Status202Accepted);
}
