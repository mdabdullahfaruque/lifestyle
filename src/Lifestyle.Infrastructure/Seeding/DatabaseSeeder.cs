using Lifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lifestyle.Infrastructure.Seeding;

/// <summary>
/// Runs the seeders in dependency order. Invoked explicitly by <c>Lifestyle.Api -- seed</c>,
/// never as a side effect of application start-up (docs/04 §4.4).
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseSeeder));

        logger.LogInformation("Seeding database.");

        // Roles first: SuperAdminSeeder grants one.
        await ActivatorUtilities.CreateInstance<RoleSeeder>(provider).SeedAsync(ct);
        await ActivatorUtilities.CreateInstance<SuperAdminSeeder>(provider).SeedAsync(ct);
        await ActivatorUtilities.CreateInstance<CatalogSeeder>(provider).SeedAsync(ct);

        logger.LogInformation("Seeding complete.");
    }

    /// <summary>Applies pending migrations. Run as a separate deploy step, before the app starts.</summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseSeeder));

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("No pending migrations.");
            return;
        }

        logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);
        logger.LogInformation("Migrations applied.");
    }
}
