using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Media.Contracts;
using Lifestyle.Modules.Media.Domain;
using Lifestyle.Modules.Media.Features.Uploads;
using Lifestyle.Modules.Media.Internal;
using Lifestyle.Modules.Media.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Paging;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Media.Features.Library;

/// <summary>
/// One image in a vendor's library, with the flag the grid needs to tell "spare" from "in use".
/// </summary>
public sealed record LibraryItemResponse(
    string MediaId,
    string FileName,
    string ContentType,
    long SizeBytes,
    int? Width,
    int? Height,
    string Url,
    IReadOnlyDictionary<string, string> Derivatives,
    bool IsUsed,
    DateTimeOffset UploadedAt);

/// <summary>
/// The vendor's image library (docs/08 §2) — every image they have uploaded, whether or not it is
/// on a product yet.
/// <para>
/// The library is defined by <see cref="MediaFile.VendorId"/>, not by owner type, so an image stays
/// listed after it is attached to a product. <see cref="MediaOwnerTypes.VendorLibrary"/> exists
/// only to keep an unused upload from being swept as an orphan.
/// </para>
/// </summary>
internal static class ListVendorMedia
{
    public sealed record Request(bool? UnusedOnly, string? Search, int Page = 1, int PageSize = 40);

    internal sealed class Handler(IMediaDbContext db, IFileStorage storage, ICurrentUser currentUser)
        : IHandler<Request, Result<PagedResult<LibraryItemResponse>>>
    {
        public async Task<Result<PagedResult<LibraryItemResponse>>> Handle(Request request, CancellationToken ct)
        {
            if (currentUser.VendorId is not { } vendorId)
                return Error.Forbidden("media.no_vendor_context", "This token is not scoped to a vendor.");

            // Private files are KYC documents, never storefront imagery. They must not appear in a
            // picker that exists to put images on a product page.
            var query = db.MediaFiles
                .AsNoTracking()
                .Where(m => m.VendorId == vendorId && !m.IsPrivate);

            if (request.UnusedOnly == true)
                query = query.Where(m => m.OwnerType == MediaOwnerTypes.VendorLibrary);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var term = $"%{request.Search.Trim().ToLowerInvariant()}%";
#pragma warning disable CA1304, CA1311 // Translated to SQL lower(); see VendorApprovalQueue for the full note.
                query = query.Where(m => EF.Functions.Like(m.FileName.ToLower(), term));
#pragma warning restore CA1304, CA1311
            }

            var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
            var total = await query.CountAsync(ct);

            var files = await query
                .Include(m => m.Derivatives)
                .OrderByDescending(m => m.CreatedAt)
                .Skip(page.Skip)
                .Take(page.Size)
                .ToListAsync(ct);

            var items = files.Select(m =>
            {
                var asset = UploadFile.ToAsset(m, storage);
                return new LibraryItemResponse(
                    asset.Id, asset.FileName, asset.ContentType, asset.SizeBytes,
                    asset.Width, asset.Height, asset.Url, asset.Derivatives,
                    IsUsed: m.OwnerType == MediaOwnerTypes.Product,
                    m.CreatedAt);
            }).ToList();

            return PagedResult<LibraryItemResponse>.From(items, page, total);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/", async ([AsParameters] Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToHttpResult())
            .WithName("ListVendorMedia")
            .WithSummary("The vendor's image library, newest first.")
            .RequirePermission(Permissions.Media.UploadOwn)
            .Produces<PagedResult<LibraryItemResponse>>();
}

/// <summary>
/// Removes an image from the library and from storage.
/// <para>
/// An image that is on a product is refused. Media owns <see cref="MediaFile.OwnerType"/>, so this
/// check needs no call into Catalog — which is just as well, because the dependency only runs
/// Catalog → Media and never back (docs/04 §4.3).
/// </para>
/// </summary>
internal static class DeleteVendorMedia
{
    internal sealed class Handler(IMediaDbContext db, IFileStorage storage, ICurrentUser currentUser)
        : IHandler<Handler.Command, Result>
    {
        internal sealed record Command(string MediaId);

        public async Task<Result> Handle(Command command, CancellationToken ct)
        {
            if (currentUser.VendorId is not { } vendorId)
                return Error.Forbidden("media.no_vendor_context", "This token is not scoped to a vendor.");

            var file = await db.MediaFiles
                .Include(m => m.Derivatives)
                .FirstOrDefaultAsync(m => m.PublicId == command.MediaId, ct);

            // 404 rather than 403 for someone else's file: confirming it exists would let a caller
            // enumerate other vendors' media ids.
            if (file is null || file.VendorId != vendorId)
                return Error.NotFound("media.not_found");

            // Allow-list, not a block-list. Refusing only "product" would let a vendor delete their
            // own KYC documents and shop branding through the library — including the trade licence
            // a moderator has not read yet, which docs/07 G1 says there is no backup for. Anything
            // this endpoint does not positively recognise as a spare library image is refused.
            if (file.OwnerType != MediaOwnerTypes.VendorLibrary)
                return Error.Conflict("media.in_use",
                    file.OwnerType == MediaOwnerTypes.Product
                        ? "This image is on a product. Remove it from the product first."
                        : "This file is in use elsewhere in your shop and cannot be deleted here.");

            // Storage first, then the row: if the process dies between the two, the row is still
            // present and the next attempt re-deletes idempotently. The reverse order would leak an
            // unreachable file forever — same reasoning as MediaOrphanSweeper.
            foreach (var derivative in file.Derivatives)
                await storage.DeleteAsync(derivative.StorageKey, ct);

            await storage.DeleteAsync(file.StorageKey, ct);

            db.MediaFiles.Remove(file);
            await db.SaveChangesAsync(ct);

            return Result.Success();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete("/{mediaId}", async (string mediaId, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(mediaId), ct)).ToHttpResult())
            .WithName("DeleteVendorMedia")
            .WithSummary("Delete an image from the library. Refused while it is on a product.")
            .RequirePermission(Permissions.Media.UploadOwn)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
}
