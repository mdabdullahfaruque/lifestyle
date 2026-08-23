using Lifestyle.Modules.Catalog.Contracts;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.SharedKernel.Domain;
using CatalogEvents = Lifestyle.Modules.Catalog.Domain.Events;
using IdentityEvents = Lifestyle.Modules.Identity.Domain.Events;
using VendorEvents = Lifestyle.Modules.Vendors.Domain.Events;

namespace Lifestyle.Infrastructure.Persistence;

/// <summary>
/// Maps a module's internal domain event to its public integration event.
/// <para>
/// This table lives in Infrastructure on purpose. It is the one place that sees inside every
/// module, and keeping it here means a module never has to know which of its events other modules
/// care about — a domain event with no entry here simply never leaves its module.
/// </para>
/// </summary>
internal static class IntegrationEventMap
{
    public static IIntegrationEvent? Translate(IDomainEvent domainEvent, DateTimeOffset now) => domainEvent switch
    {
        IdentityEvents.UserRegistered e =>
            new UserRegisteredEvent(Guid.CreateVersion7(), now, e.UserId, e.Email, e.FullName),

        IdentityEvents.UserSuspended e =>
            new UserSuspendedEvent(Guid.CreateVersion7(), now, e.UserId, e.Reason),

        VendorEvents.VendorApproved e =>
            new VendorApprovedEvent(Guid.CreateVersion7(), now, e.VendorId, e.DisplayName, e.Slug, e.OwnerUserId),

        VendorEvents.VendorSuspended e =>
            new VendorSuspendedEvent(Guid.CreateVersion7(), now, e.VendorId, e.Reason),

        VendorEvents.VendorReinstated e =>
            new VendorReinstatedEvent(Guid.CreateVersion7(), now, e.VendorId),

        CatalogEvents.ProductPublished e =>
            new ProductPublishedEvent(Guid.CreateVersion7(), now, e.ProductId, e.VendorId, e.Name, e.Slug),

        CatalogEvents.ProductUnpublished e =>
            new ProductUnpublishedEvent(Guid.CreateVersion7(), now, e.ProductId, e.VendorId, e.Reason),

        // In-module only: VendorApplied, VendorSubmittedForReview, VendorRejected,
        // ProductSubmittedForReview, ProductRejected, RefreshTokenFamilyRevoked.
        _ => null
    };
}
