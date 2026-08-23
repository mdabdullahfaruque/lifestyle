using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Identity.Domain;
using Lifestyle.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Identity.Internal;

/// <summary>
/// Works out what a user may do on a given surface, and (for the seller surface) which vendor they
/// are acting for. Roles are per-surface, so a buyer token never carries admin permissions even if
/// the same person holds an admin role.
/// </summary>
internal sealed class PermissionResolver(IIdentityDbContext db)
{
    public async Task<SurfaceAccess> ResolveAsync(Guid userId, string surface, Guid? requestedVendorId, CancellationToken ct)
    {
        var grants = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.Role.Surface == surface)
            .Select(ur => new { ur.ScopeId, ur.Role.Name, Permissions = ur.Role.Permissions })
            .ToListAsync(ct);

        if (grants.Count == 0)
            return SurfaceAccess.None;

        Guid? vendorId = null;
        if (surface == Surfaces.Seller)
        {
            var vendorScopes = grants
                .Where(g => g.ScopeId.HasValue)
                .Select(g => g.ScopeId!.Value)
                .Distinct()
                .ToList();

            if (vendorScopes.Count == 0)
                return SurfaceAccess.None;

            // A user may staff several vendors. If they asked for one, honour it only if they hold
            // it; otherwise fall back to their single vendor. Never silently pick from many.
            if (requestedVendorId is { } requested)
            {
                if (!vendorScopes.Contains(requested)) return SurfaceAccess.None;
                vendorId = requested;
            }
            else if (vendorScopes.Count == 1)
            {
                vendorId = vendorScopes[0];
            }
            else
            {
                return SurfaceAccess.AmbiguousVendor(vendorScopes);
            }

            var scoped = grants
                .Where(g => g.ScopeId == vendorId)
                .SelectMany(g => g.Permissions)
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            return new SurfaceAccess(true, scoped, vendorId, []);
        }

        var permissions = grants
            .SelectMany(g => g.Permissions)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        return new SurfaceAccess(true, permissions, null, []);
    }

    /// <summary>
    /// The default role for the surface a user is registering on. Buyers self-register; seller and
    /// admin roles are always granted by someone else.
    /// </summary>
    public async Task<Role?> GetSystemRoleAsync(Guid roleId, CancellationToken ct) =>
        await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
}

internal sealed record SurfaceAccess(
    bool HasAccess,
    IReadOnlySet<string> Permissions,
    Guid? VendorId,
    IReadOnlyList<Guid> CandidateVendorIds)
{
    public static SurfaceAccess None { get; } = new(false, new HashSet<string>(StringComparer.Ordinal), null, []);

    public static SurfaceAccess AmbiguousVendor(IReadOnlyList<Guid> candidates) =>
        new(false, new HashSet<string>(StringComparer.Ordinal), null, candidates);

    public bool NeedsVendorChoice => !HasAccess && CandidateVendorIds.Count > 1;
}
