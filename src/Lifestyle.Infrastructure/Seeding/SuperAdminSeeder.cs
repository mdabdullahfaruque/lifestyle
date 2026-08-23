using Lifestyle.Infrastructure.Persistence;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Identity.Domain;
using Lifestyle.Modules.Identity.Internal;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lifestyle.Infrastructure.Seeding;

/// <summary>
/// Creates the first super-admin from configuration, so a fresh deployment has someone who can log
/// in. Does nothing if the account already exists, and refuses to run without an explicitly
/// configured password — a hard-coded default admin password is how platforms get owned.
/// </summary>
internal sealed class SuperAdminSeeder(
    AppDbContext db,
    IPasswordService passwords,
    IClock clock,
    IConfiguration configuration,
    ILogger<SuperAdminSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var email = configuration["Seed:SuperAdmin:Email"];
        var password = configuration["Seed:SuperAdmin:Password"];
        var name = configuration["Seed:SuperAdmin:FullName"] ?? "Platform Administrator";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation(
                "No Seed:SuperAdmin:Email/Password configured; skipping super-admin seed.");
            return;
        }

        var normalised = email.Trim().ToLowerInvariant();

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == normalised, ct))
        {
            logger.LogInformation("Super-admin {Email} already exists.", normalised);
            return;
        }

        var now = clock.UtcNow;
        var user = User.Register(normalised, passwords.Hash(password), name, null, now);
        user.VerifyEmail(now);
        user.AssignRole(SystemRoles.SuperAdminId, null, now);

        // Also a buyer, so the same person can use the shopper surface without a second account.
        user.AssignRole(SystemRoles.BuyerId, null, now);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Seeded super-admin {Email}. Two-factor enrolment is required before this account can sign in to the admin surface.",
            normalised);
    }
}
