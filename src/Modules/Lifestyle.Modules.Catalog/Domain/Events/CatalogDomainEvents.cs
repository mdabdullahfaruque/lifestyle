using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Catalog.Domain.Events;

internal sealed record ProductSubmittedForReview(Guid ProductId, Guid VendorId, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record ProductPublished(Guid ProductId, Guid VendorId, string Name, string Slug, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record ProductRejected(Guid ProductId, Guid VendorId, string Note, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed record ProductUnpublished(Guid ProductId, Guid VendorId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
