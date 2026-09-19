using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Media.Contracts;
using Lifestyle.Modules.Media.Features.Uploads;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lifestyle.Modules.Media.Features.Library;

/// <summary>The outcome of one file in a bulk upload. A failure here never fails the batch.</summary>
public sealed record BulkUploadItemResponse(
    string FileName,
    bool Succeeded,
    string? MediaId,
    string? Url,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record BulkUploadResponse(
    int Succeeded,
    int Failed,
    IReadOnlyList<BulkUploadItemResponse> Items);

/// <summary>
/// Uploads many images into the vendor's library in one request (docs/08 §2.1, §6.1).
/// <para>
/// Files are processed <b>one at a time, deliberately</b>. ImageSharp decodes an image into
/// uncompressed memory — a 12 MP photo is roughly 48 MB of pixels — and this server is shared with
/// four other products (docs/07 §1). Fanning 40 files out with <c>Task.WhenAll</c> would not make
/// the upload meaningfully faster, because the work is CPU-bound on the same few cores, but it
/// would multiply peak memory by the degree of parallelism and take the whole box down with it.
/// </para>
/// </summary>
internal static class BulkUploadToLibrary
{
    /// <summary>
    /// Cap per request. Keeps one request's worst case bounded; a seller with 400 photos sends ten
    /// batches, which also gives the browser somewhere to show progress.
    /// </summary>
    private const int MaxFilesPerRequest = 40;

    internal sealed class Handler(
        UploadFile.Handler upload,
        IMediaModule media,
        ICurrentUser currentUser)
        : IHandler<Handler.Command, Result<BulkUploadResponse>>
    {
        internal sealed record Command(IFormFileCollection Files);

        public async Task<Result<BulkUploadResponse>> Handle(Command command, CancellationToken ct)
        {
            if (currentUser.VendorId is not { } vendorId)
                return Error.Forbidden("media.no_vendor_context", "This token is not scoped to a vendor.");

            var files = command.Files;

            if (files.Count == 0)
                return Error.Validation("media.no_files", "No files were uploaded.");

            if (files.Count > MaxFilesPerRequest)
                return Error.Validation("media.too_many_files",
                    $"Upload at most {MaxFilesPerRequest} files at a time.");

            var items = new List<BulkUploadItemResponse>(files.Count);
            var uploaded = new List<string>(files.Count);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                // Reuses the single-file handler rather than restating its size, type and
                // magic-byte checks — one upload path, one set of rules.
                var result = await upload.Handle(new UploadFile.Handler.Command(file, IsPrivate: false), ct);

                if (result.IsFailure)
                {
                    items.Add(new BulkUploadItemResponse(
                        file.FileName, Succeeded: false, null, null,
                        result.Error.Code, result.Error.Message));
                    continue;
                }

                uploaded.Add(result.Value.Id);
                items.Add(new BulkUploadItemResponse(
                    file.FileName, Succeeded: true, result.Value.Id, result.Value.Url, null, null));
            }

            // Claim them for the library in one pass, so an interrupted batch leaves the files it
            // did finish as orphans the sweeper will tidy rather than as permanent storage cost.
            if (uploaded.Count > 0)
                await media.AttachAsync(uploaded, MediaOwnerTypes.VendorLibrary, vendorId, ct);

            return new BulkUploadResponse(uploaded.Count, items.Count - uploaded.Count, items);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/bulk", async (IFormFileCollection files, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(files), ct)).ToHttpResult())
            .WithName("BulkUploadToLibrary")
            .WithSummary("Upload up to 40 images into the vendor's library. One bad file does not fail the batch.")
            .RequirePermission(Permissions.Media.UploadOwn)
            .DisableAntiforgery()
            .Produces<BulkUploadResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
}
