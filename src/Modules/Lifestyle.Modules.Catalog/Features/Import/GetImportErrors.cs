using ClosedXML.Excel;
using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Internal;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lifestyle.Modules.Catalog.Features.Import;

/// <summary>
/// The failed rows, as a sheet the seller can fix and upload again (docs/08 §5.3).
/// <para>
/// This is what makes a partial import survivable: 200 rows with three bad ones import 197
/// products and hand back those three, with their own values still in them and a column saying
/// what was wrong. Because <c>product_code</c> is the idempotency key, re-uploading the corrected
/// file updates rather than duplicating. It is the single most important behaviour in the feature.
/// </para>
/// </summary>
internal static class GetImportErrors
{
    private const string ErrorColumn = "error";

    internal sealed class Handler(VendorScope scope, ImportSchemaFactory schemas)
        : IHandler<Handler.Command, Result<GetImportTemplate.TemplateFile>>
    {
        internal sealed record Command(Guid JobId);

        public async Task<Result<GetImportTemplate.TemplateFile>> Handle(Command command, CancellationToken ct)
        {
            var loaded = await scope.LoadOwnedImportJobAsync(command.JobId, ct);
            if (loaded.IsFailure) return loaded.Error;

            var job = loaded.Value;

            var failed = job.Rows
                .Where(r => r.Outcome == ImportRowOutcome.Error)
                .OrderBy(r => r.RowNumber)
                .ToList();

            if (failed.Count == 0)
                return Error.Validation("catalog.import_no_errors", "Every row in this import was accepted.");

            var schema = await schemas.ForCategoryAsync(job.CategoryId, ct);
            if (schema.IsFailure) return schema.Error;

            var content = Write(schema.Value, failed);

            return new GetImportTemplate.TemplateFile(
                content,
                ImportTemplateWriter.XlsxContentType,
                $"import-errors-{job.Id:N}.xlsx");
        }

        private static byte[] Write(ImportSchema schema, List<ImportJobRow> failed)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Products");

            // The error column comes first so the seller sees the problem before scrolling. It is
            // ignored on re-upload, so the corrected file can be sent straight back unedited.
            sheet.Cell(1, 1).Value = ErrorColumn;
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.LightSalmon;
            sheet.Column(1).Width = 60;

            for (var i = 0; i < schema.Columns.Count; i++)
            {
                var cell = sheet.Cell(1, i + 2);
                cell.Value = schema.Columns[i].Name;
                cell.Style.Font.Bold = true;
                sheet.Column(i + 2).Width = 22;
            }

            for (var r = 0; r < failed.Count; r++)
            {
                var row = failed[r];
                sheet.Cell(r + 2, 1).Value = row.ErrorMessage ?? row.ErrorCode ?? "Could not be imported.";

                for (var c = 0; c < schema.Columns.Count; c++)
                {
                    var name = schema.Columns[c].Name;
                    sheet.Cell(r + 2, c + 2).Value = row.Values.TryGetValue(name, out var value) ? value : string.Empty;
                }
            }

            sheet.SheetView.FreezeRows(1);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/import/{jobId:guid}/errors", async (
                Guid jobId, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(new Handler.Command(jobId), ct);

                return result.IsFailure
                    ? ResultExtensions.Problem(result.Error)
                    : Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .WithName("GetImportErrors")
            .WithSummary("The rows that failed, as a sheet to correct and upload again.")
            .RequirePermission(Permissions.Catalog.ReadOwn)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
