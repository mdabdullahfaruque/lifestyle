using Lifestyle.Modules.Vendors.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Vendors.Persistence;

internal interface IVendorsDbContext
{
    DbSet<Vendor> Vendors { get; }
    DbSet<VendorStaff> VendorStaff { get; }
    DbSet<VendorDocument> VendorDocuments { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
