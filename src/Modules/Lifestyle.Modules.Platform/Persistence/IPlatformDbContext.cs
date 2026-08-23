using Lifestyle.Modules.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Platform.Persistence;

internal interface IPlatformDbContext
{
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<PlatformSetting> PlatformSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
