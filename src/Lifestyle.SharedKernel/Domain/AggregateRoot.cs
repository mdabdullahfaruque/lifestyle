namespace Lifestyle.SharedKernel.Domain;

/// <summary>
/// An aggregate root is the only thing a repository or handler loads and saves as a unit, and the
/// only thing that raises domain events. Events are drained by the SaveChanges interceptor and
/// written to the outbox in the same transaction as the state change (FRD §21.1).
/// </summary>
public abstract class AggregateRoot : Entity, IAuditable
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
