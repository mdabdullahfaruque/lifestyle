using Lifestyle.Modules.Media.Contracts;
using Lifestyle.Modules.Media.Features.Uploads;
using Lifestyle.Modules.Media.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Media.Internal;

internal sealed class MediaFacade(IMediaDbContext db, IFileStorage storage, IClock clock) : IMediaModule
{
    public async Task<MediaAsset?> GetAsync(string mediaId, CancellationToken ct)
    {
        var media = await db.MediaFiles
            .Include(m => m.Derivatives)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.PublicId == mediaId, ct);

        return media is null ? null : UploadFile.ToAsset(media, storage);
    }

    public async Task<IReadOnlyList<MediaAsset>> GetManyAsync(IReadOnlyCollection<string> mediaIds, CancellationToken ct)
    {
        if (mediaIds.Count == 0) return [];

        var files = await db.MediaFiles
            .Include(m => m.Derivatives)
            .AsNoTracking()
            .Where(m => mediaIds.Contains(m.PublicId))
            .ToListAsync(ct);

        return [.. files.Select(m => UploadFile.ToAsset(m, storage))];
    }

    public async Task AttachAsync(IReadOnlyCollection<string> mediaIds, string ownerType, Guid ownerId, CancellationToken ct)
    {
        if (mediaIds.Count == 0) return;

        var files = await db.MediaFiles.Where(m => mediaIds.Contains(m.PublicId)).ToListAsync(ct);
        foreach (var file in files) file.Attach(ownerType, ownerId, clock.UtcNow);

        await db.SaveChangesAsync(ct);
    }
}
