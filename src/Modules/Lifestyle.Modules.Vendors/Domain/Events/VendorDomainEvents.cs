using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Vendors.Domain.Events;

internal sealed record VendorApplied(Guid VendorId, string DisplayName, Guid OwnerUserId, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record VendorSubmittedForReview(Guid VendorId, string DisplayName, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record VendorApproved(Guid VendorId, string DisplayName, string Slug, Guid OwnerUserId, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record VendorRejected(Guid VendorId, string Reason, Guid RejectedBy, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record VendorSuspended(Guid VendorId, string Reason, Guid SuspendedBy, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record VendorReinstated(Guid VendorId, DateTimeOffset OccurredAt) : IDomainEvent;
