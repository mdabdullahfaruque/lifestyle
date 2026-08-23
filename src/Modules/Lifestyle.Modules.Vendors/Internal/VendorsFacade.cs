using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.Modules.Vendors.Domain;
using Lifestyle.Modules.Vendors.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Vendors.Internal;

internal sealed class VendorsFacade(IVendorsDbContext db) : IVendorsModule
{
    public async Task<VendorSnapshot?> GetAsync(Guid vendorId, CancellationToken ct) =>
        await Project(db.Vendors.Where(v => v.Id == vendorId)).FirstOrDefaultAsync(ct);

    public async Task<VendorSnapshot?> GetBySlugAsync(string slug, CancellationToken ct) =>
        await Project(db.Vendors.Where(v => v.Slug == slug)).FirstOrDefaultAsync(ct);

    public async Task<VendorSnapshot?> ResolveStorefrontAsync(string slugOrDomain, bool isCustomDomain, CancellationToken ct)
    {
        var query = isCustomDomain
            ? db.Vendors.Where(v => v.CustomDomain == slugOrDomain && v.CustomDomainStatus == CustomDomainStatus.Active)
            : db.Vendors.Where(v => v.Slug == slugOrDomain);

        // Only approved vendors resolve as a host. A suspended shop's subdomain stops working,
        // which is the intended consequence of suspension.
        return await Project(query.Where(v => v.Status == VendorStatus.Approved)).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<VendorSnapshot>> GetManyAsync(IReadOnlyCollection<Guid> vendorIds, CancellationToken ct)
    {
        if (vendorIds.Count == 0) return [];
        return await Project(db.Vendors.Where(v => vendorIds.Contains(v.Id))).ToListAsync(ct);
    }

    public Task<bool> CanSellAsync(Guid vendorId, CancellationToken ct) =>
        db.Vendors.AnyAsync(v => v.Id == vendorId && v.Status == VendorStatus.Approved, ct);

    public Task<bool> IsStaffAsync(Guid vendorId, Guid userId, CancellationToken ct) =>
        db.VendorStaff.AnyAsync(s => s.VendorId == vendorId && s.UserId == userId, ct);

    private static IQueryable<VendorSnapshot> Project(IQueryable<Vendor> query) =>
        query.AsNoTracking().Select(v => new VendorSnapshot(
            v.Id, v.DisplayName, v.Slug, v.CustomDomain, v.LogoMediaId, v.BannerMediaId,
            v.AccentColour, v.WhatsAppNumber, v.About, v.Status == VendorStatus.Approved));
}
