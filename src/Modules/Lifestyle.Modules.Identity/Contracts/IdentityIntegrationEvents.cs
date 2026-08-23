using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Identity.Contracts;

/// <summary>Published when a new account is created. Notifications sends the welcome/verify email.</summary>
public sealed record UserRegisteredEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    string Email,
    string FullName) : IIntegrationEvent;

/// <summary>Published when an account is suspended, so other modules can react (hide listings, etc.).</summary>
public sealed record UserSuspendedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    string Reason) : IIntegrationEvent;
