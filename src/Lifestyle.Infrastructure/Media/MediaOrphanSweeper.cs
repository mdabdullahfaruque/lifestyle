using Lifestyle.Modules.Media.Internal;
using Lifestyle.Modules.Media.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lifestyle.Infrastructure.Media;

/// <summary>
/// Deletes uploads that were never attached to anything (docs/04: an abandoned upload must not
/// become permanent storage cost — disk is the constraint that bites first, doc 03 §7.2).
/// <para>
/// Storage objects are deleted before the row: if the process dies between the two, the next
/// sweep re-processes the still-present row and the storage deletes are idempotent. The reverse
/// order would leak unreachable files forever.
/// </para>
/// </summary>
internal sealed class MediaOrphanSweeper(
    IServiceScopeFactory scopeFactory,
    ILogger<MediaOrphanSweeper> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Media orphan sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IMediaDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MediaModuleOptions>>().Value;

        var cutoff = clock.UtcNow.AddHours(-options.OrphanRetentionHours);

        var orphans = await db.MediaFiles
            .Include(m => m.Derivatives)
            .Where(m => m.OwnerType == null && m.CreatedAt < cutoff)
            .OrderBy(m => m.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        if (orphans.Count == 0) return;

        foreach (var orphan in orphans)
        {
            foreach (var derivative in orphan.Derivatives)
                await storage.DeleteAsync(derivative.StorageKey, ct);

            await storage.DeleteAsync(orphan.StorageKey, ct);
            db.MediaFiles.Remove(orphan);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Swept {Count} orphaned upload(s) older than {Hours}h.",
            orphans.Count, options.OrphanRetentionHours);
    }
}
