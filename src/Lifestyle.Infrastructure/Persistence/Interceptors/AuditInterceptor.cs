using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lifestyle.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Fills in created/updated audit columns and turns a delete on an <see cref="ISoftDeletable"/>
/// into an update. Doing this here rather than in handlers means it cannot be forgotten.
/// </summary>
internal sealed class AuditInterceptor(IClock clock, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;

        var now = clock.UtcNow;
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Aggregates that set CreatedAt in their factory keep it; this catches the rest.
                    if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;

                    // Never let an update rewrite who created the row.
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Property(e => e.CreatedBy).IsModified = false;
                    break;

                default:
                    break;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted) continue;

            entry.State = EntityState.Modified;
            entry.Entity.DeletedAt = now;
            entry.Entity.DeletedBy = userId;
        }
    }
}
