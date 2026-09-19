using Lifestyle.Modules.Media.Contracts;
using Lifestyle.Modules.Media.Features.Uploads;
using Lifestyle.Modules.Media.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Media.Internal;

internal sealed class MediaFacade(
    IMediaDbContext db,
    IFileStorage storage,
    IClock clock,
    RemoteImageFetcher remoteImages,
    Features.Uploads.UploadFile.Handler upload)
    : IMediaModule
{
    /// <summary>
    /// Downloads a seller-named URL and files it like any other upload. The fetcher is where every
    /// SSRF guard lives; this only hands the bytes to the one upload path so the size, type and
    /// decoder checks still apply to something that came off the internet.
    /// </summary>
    public async Task<Result<MediaAsset>> ImportFromUrlAsync(string url, CancellationToken ct)
    {
        var fetched = await remoteImages.FetchAsync(url, ct);
        if (fetched.IsFailure) return fetched.Error;

        return await upload.Handle(
            new Features.Uploads.UploadFile.Handler.Command(fetched.Value, IsPrivate: false, SquareCanvas: true), ct);
    }

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

    public async Task<IReadOnlyList<MediaAsset>> ListForVendorAsync(Guid vendorId, bool unusedOnly, CancellationToken ct)
    {
        // Private files are KYC documents, never storefront imagery.
        var query = db.MediaFiles
            .Include(m => m.Derivatives)
            .AsNoTracking()
            .Where(m => m.VendorId == vendorId && !m.IsPrivate);

        if (unusedOnly)
            query = query.Where(m => m.OwnerType == MediaOwnerTypes.VendorLibrary);

        var files = await query.OrderByDescending(m => m.CreatedAt).ToListAsync(ct);

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
