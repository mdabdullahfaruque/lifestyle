using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.SharedKernel.Abstractions;

/// <summary>
/// Writes an integration event to the outbox in the current transaction. Implemented by
/// Infrastructure; used by a module only when an event is not a natural consequence of an
/// aggregate's own state change (otherwise raise a domain event and let the interceptor do it).
/// </summary>
public interface IIntegrationEventPublisher
{
    void Enqueue(IIntegrationEvent integrationEvent);
}
