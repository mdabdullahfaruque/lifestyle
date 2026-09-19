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
/// Serves the bulk-import template for one category, with that category's own attribute columns
/// already in it (docs/08 §3).
/// <para>
/// Generated per category rather than shipped as a static file: the columns come from the
/// category's attribute set, so adding an attribute updates every seller's template with no
/// release. It is also why the template can carry dropdowns of the legal values.
/// </para>
/// </summary>
internal static class GetImportTemplate
{
    internal sealed record TemplateFile(byte[] Content, string ContentType, string FileName);

    internal sealed class Handler(ImportSchemaFactory schemas, VendorScope scope)
        : IHandler<Handler.Command, Result<TemplateFile>>
    {
        internal sealed record Command(Guid CategoryId, string? Format);

        public async Task<Result<TemplateFile>> Handle(Command command, CancellationToken ct)
        {
            var vendorId = scope.RequireVendorId();
            if (vendorId.IsFailure) return vendorId.Error;

            var format = (command.Format ?? "xlsx").Trim().ToLowerInvariant();
            if (format is not ("xlsx" or "csv"))
                return Error.Validation("catalog.import_format_invalid", "Format must be xlsx or csv.");

            var schema = await schemas.ForCategoryAsync(command.CategoryId, ct);
            if (schema.IsFailure) return schema.Error;

            return format == "csv"
                ? new TemplateFile(
                    ImportTemplateWriter.ToCsv(schema.Value),
                    ImportTemplateWriter.CsvContentType,
                    ImportTemplateWriter.FileName(schema.Value, "csv"))
                : new TemplateFile(
                    ImportTemplateWriter.ToXlsx(schema.Value),
                    ImportTemplateWriter.XlsxContentType,
                    ImportTemplateWriter.FileName(schema.Value, "xlsx"));
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/import/template", async (
                Guid categoryId, string? format, Handler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(new Handler.Command(categoryId, format), ct);

                return result.IsFailure
                    ? ResultExtensions.Problem(result.Error)
                    : Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .WithName("GetImportTemplate")
            .WithSummary("Download the import sheet for a category, with its attribute columns and dropdowns.")
            .RequirePermission(Permissions.Catalog.WriteOwn)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
