using Lifestyle.Infrastructure.Persistence;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lifestyle.Infrastructure.Seeding;

/// <summary>
/// Creates or updates the system roles (FRD §4.5). Runs on every deployment, not just the first —
/// adding a permission to a role must reach existing environments.
/// </summary>
internal sealed class RoleSeeder(AppDbContext db, ILogger<RoleSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var definitions = Definitions();

        foreach (var definition in definitions)
        {
            var existing = await db.Roles.FirstOrDefaultAsync(r => r.Id == definition.Id, ct);

            if (existing is null)
            {
                db.Roles.Add(Role.Create(definition.Id, definition.Name, definition.Surface,
                    definition.Description, isSystem: true, definition.Permissions));

                logger.LogInformation("Seeded role {Role}.", definition.Name);
            }
            else
            {
                // Permissions are code, roles are data: re-apply so a new permission key reaches
                // environments that already have the row.
                existing.SetPermissions(definition.Permissions);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<RoleDefinition> Definitions() =>
    [
        new(SystemRoles.BuyerId, SystemRoles.Buyer, Surfaces.Buyer,
            "A shopper. Browses, saves and orders.",
            []),

        new(SystemRoles.VendorOwnerId, SystemRoles.VendorOwner, Surfaces.Seller,
            "Owns a shop. Full control over its catalogue, staff and settings.",
            [
                Permissions.Vendors.ManageOwn, Permissions.Vendors.ManageStaff,
                Permissions.Catalog.ReadOwn, Permissions.Catalog.WriteOwn, Permissions.Catalog.PublishOwn,
                Permissions.Inventory.ReadOwn, Permissions.Inventory.WriteOwn,
                Permissions.Media.UploadOwn
            ]),

        new(SystemRoles.VendorStaffId, SystemRoles.VendorStaff, Surfaces.Seller,
            "Works in a shop. Manages the catalogue but not staff or shop settings.",
            [
                Permissions.Catalog.ReadOwn, Permissions.Catalog.WriteOwn,
                Permissions.Inventory.ReadOwn, Permissions.Inventory.WriteOwn,
                Permissions.Media.UploadOwn
            ]),

        new(SystemRoles.SuperAdminId, SystemRoles.SuperAdmin, Surfaces.Admin,
            "Platform operator. Everything.",
            [.. Permissions.All]),

        new(SystemRoles.CatalogModeratorId, SystemRoles.CatalogModerator, Surfaces.Admin,
            "Reviews products and manages the taxonomy.",
            [
                Permissions.Catalog.Moderate, Permissions.Catalog.ManageTaxonomy,
                Permissions.Vendors.Read, Permissions.Media.UploadOwn
            ]),

        new(SystemRoles.SupportAgentId, SystemRoles.SupportAgent, Surfaces.Admin,
            "Answers customer and vendor queries. Read-mostly.",
            [
                Permissions.Vendors.Read, Permissions.Users.Read, Permissions.Platform.ReadAuditLog
            ])
    ];

    private sealed record RoleDefinition(
        Guid Id, string Name, string Surface, string Description, IReadOnlyList<string> Permissions);
}
