using Lifestyle.Modules.Media.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Media.Persistence;

internal interface IMediaDbContext
{
    DbSet<MediaFile> MediaFiles { get; }
    DbSet<MediaDerivative> MediaDerivatives { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
