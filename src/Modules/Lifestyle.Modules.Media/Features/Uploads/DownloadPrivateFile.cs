using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Media.Internal;
using Lifestyle.Modules.Media.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Media.Features.Uploads;

/// <summary>
/// The only way to read a private file (KYC documents). The public media host never serves the
/// <c>private/</c> key prefix — the edge blocks it and this endpoint re-checks authorisation on
/// every read, because an identity document on a public, CDN-cached URL is a breach, not a bug.
/// </summary>
internal static class DownloadPrivateFile
{
    internal sealed class Handler(IMediaDbContext db, IFileStorage storage, ICurrentUser currentUser)
        : IHandler<string, Result<Handler.PrivateFile>>
    {
        internal sealed record PrivateFile(Stream Content, string ContentType, string FileName);

        public async Task<Result<PrivateFile>> Handle(string publicId, CancellationToken ct)
        {
            if (currentUser.UserId is not { } userId)
                return Error.Unauthorized("identity.not_authenticated");

            var media = await db.MediaFiles
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.PublicId == publicId && m.IsPrivate, ct);

            // 404 for both "does not exist" and "not yours": confirming a private file's existence
            // to a caller who may not read it is itself a leak.
            if (media is null) return Error.NotFound("media.not_found");

            var mayRead =
                media.UploadedByUserId == userId
                || (media.VendorId is { } vendorId && currentUser.VendorId == vendorId)
                || (currentUser.Audience == Surfaces.Admin
                    && (currentUser.HasPermission(Permissions.Vendors.Read)
                        || currentUser.HasPermission(Permissions.Vendors.Approve)));

            if (!mayRead) return Error.NotFound("media.not_found");

            var content = await storage.OpenReadAsync(media.StorageKey, ct);
            if (content is null) return Error.NotFound("media.not_found");

            return new PrivateFile(content, media.ContentType, media.FileName);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/private/{publicId}", async (string publicId, Handler handler, HttpContext http, CancellationToken ct) =>
            {
                var result = await handler.Handle(publicId, ct);
                if (result.IsFailure) return ResultExtensions.Problem(result.Error);

                // Never cache: neither the browser, nor Cloudflare, nor any proxy in between.
                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";

                return Results.Stream(result.Value.Content, result.Value.ContentType,
                    // inline is fine for the reviewer's browser; the sandboxing CSP is on the
                    // media host only, so keep documents as attachments here.
                    result.Value.FileName, enableRangeProcessing: false);
            })
            .WithName("DownloadPrivateFile")
            .WithSummary("Read a private file (KYC document). Uploader, owning vendor's staff, or vendor-review admins only.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
