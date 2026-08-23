using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Identity.Domain.Events;

internal sealed record UserRegistered(Guid UserId, string Email, string FullName, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record UserSuspended(Guid UserId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record RefreshTokenFamilyRevoked(Guid UserId, Guid FamilyId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
