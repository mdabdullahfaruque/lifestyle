using System.Text.Json;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lifestyle.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Drains domain events off aggregates and writes the ones that have an integration-event mapping
/// into the outbox — in the same transaction as the state change (FRD §21.1).
/// <para>
/// The mapping lives in <see cref="IntegrationEventMap"/> rather than on the domain events
/// themselves, so a module's internal events stay internal and only the ones deliberately made
/// public cross a boundary.
/// </para>
/// </summary>
internal sealed class DomainEventsToOutboxInterceptor(IClock clock) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Collect(DbContext? context)
    {
        if (context is not AppDbContext db) return;

        var aggregates = db.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (aggregates.Count == 0) return;

        var now = clock.UtcNow;

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var integrationEvent = IntegrationEventMap.Translate(domainEvent, now);

                // Not every domain event is meant to leave its module. No mapping means in-module
                // only, and it is simply dropped here.
                if (integrationEvent is null) continue;

                db.OutboxMessages.Add(OutboxMessage.Create(
                    integrationEvent.GetType().AssemblyQualifiedName!,
                    JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), JsonOptions),
                    now));
            }

            // Cleared after collection so a second SaveChanges in the same unit of work does not
            // enqueue the same events twice.
            aggregate.ClearDomainEvents();
        }
    }
}
