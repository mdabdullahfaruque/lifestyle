namespace Lifestyle.SharedKernel.Domain;

/// <summary>Raised by an aggregate, in-module. Never crosses a module boundary directly.</summary>
public interface IDomainEvent
{
    Guid EventId => Guid.CreateVersion7();
}

/// <summary>
/// The cross-module contract. Lives in a module's <c>Contracts</c> namespace, is written to the
/// outbox, and is delivered at least once — so every handler must be idempotent.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

/// <summary>Implemented by any module that reacts to another module's integration event.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task Handle(TEvent integrationEvent, CancellationToken ct);
}
