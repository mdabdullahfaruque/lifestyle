using FluentValidation;
using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Features.Attributes;

public sealed record AttributeSetResponse(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    IReadOnlyList<AttributeResponse> Attributes);

public sealed record AttributeResponse(
    Guid Id,
    string Name,
    string Code,
    string DataType,
    bool IsRequired,
    bool IsVariantAxis,
    bool IsFilterable,
    int SortOrder,
    string? Unit,
    IReadOnlyList<string> AllowedValues);

internal static class AttributeMapping
{
    public static AttributeSetResponse ToResponse(this AttributeSet s) => new(
        s.Id, s.Name, s.Code, s.Description,
        [.. s.Attributes.OrderBy(a => a.SortOrder).Select(a => new AttributeResponse(
            a.Id, a.Name, a.Code, a.DataType.ToString(), a.IsRequired, a.IsVariantAxis,
            a.IsFilterable, a.SortOrder, a.Unit, a.AllowedValues))]);
}

internal static class ListAttributeSets
{
    internal sealed class Handler(ICatalogDbContext db) : IHandler<Unit, Result<IReadOnlyList<AttributeSetResponse>>>
    {
        public async Task<Result<IReadOnlyList<AttributeSetResponse>>> Handle(Unit _, CancellationToken ct)
        {
            var sets = await db.AttributeSets
                .Include(s => s.Attributes)
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync(ct);

            // Result.Success<T> explicitly: C# forbids a user-defined implicit conversion whose
            // source type is an interface, so `return [..]` cannot reach Result<IReadOnlyList<T>>.
            return Result.Success<IReadOnlyList<AttributeSetResponse>>(
                [.. sets.Select(s => s.ToResponse())]);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/attribute-sets", async (Handler handler, CancellationToken ct) =>
                (await handler.Handle(Unit.Value, ct)).ToHttpResult())
            .WithName("ListAttributeSets")
            .WithSummary("All attribute sets with their attribute definitions.")
            .AllowAnonymous()
            .Produces<IReadOnlyList<AttributeSetResponse>>();
}

internal static class CreateAttributeSet
{
    public sealed record Request(string Name, string Code, string? Description, IReadOnlyList<AttributeInput> Attributes);

    public sealed record AttributeInput(
        string Name,
        string Code,
        string DataType,
        bool IsRequired,
        bool IsVariantAxis,
        bool IsFilterable,
        int SortOrder,
        string? Unit,
        IReadOnlyList<string>? AllowedValues);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
            RuleFor(r => r.Code).NotEmpty().MaximumLength(60).Matches("^[a-z][a-z0-9_]*$")
                .WithMessage("Code must be lowercase letters, numbers and underscores.");
            RuleFor(r => r.Description).MaximumLength(500);

            RuleForEach(r => r.Attributes).ChildRules(a =>
            {
                a.RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
                a.RuleFor(x => x.Code).NotEmpty().MaximumLength(60).Matches("^[a-z][a-z0-9_]*$");
                a.RuleFor(x => x.DataType)
                    .Must(t => Enum.TryParse<AttributeDataType>(t, ignoreCase: true, out _))
                    .WithMessage("DataType must be Text, Select, Number or Boolean.");

                // A variant axis must come from a fixed list. Free text would generate a new SKU
                // for every typo, which is how catalogues become unusable.
                a.RuleFor(x => x)
                    .Must(x => !x.IsVariantAxis
                               || (string.Equals(x.DataType, nameof(AttributeDataType.Select), StringComparison.OrdinalIgnoreCase)
                                   && x.AllowedValues is { Count: > 0 }))
                    .WithMessage("A variant axis must be a Select attribute with allowed values.")
                    .OverridePropertyName(nameof(AttributeInput.IsVariantAxis));
            });
        }
    }

    internal sealed class Handler(ICatalogDbContext db, IClock clock)
        : IHandler<Request, Result<AttributeSetResponse>>
    {
        public async Task<Result<AttributeSetResponse>> Handle(Request request, CancellationToken ct)
        {
            var code = request.Code.Trim().ToLowerInvariant();

            if (await db.AttributeSets.AnyAsync(s => s.Code == code, ct))
                return Error.Conflict("catalog.attribute_set_exists", $"An attribute set with code '{code}' already exists.");

            var set = AttributeSet.Create(request.Name, code, request.Description, clock.UtcNow);

            foreach (var input in request.Attributes)
            {
                var attribute = ProductAttribute.Create(
                    input.Name, input.Code,
                    Enum.Parse<AttributeDataType>(input.DataType, ignoreCase: true),
                    input.IsRequired, input.IsVariantAxis, input.IsFilterable,
                    input.SortOrder, input.Unit, input.AllowedValues);

                var added = set.AddAttribute(attribute);
                if (added.IsFailure) return added.Error;
            }

            db.AttributeSets.Add(set);
            await db.SaveChangesAsync(ct);

            return set.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/attribute-sets", async (Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToCreated(s => $"/v1/catalog/attribute-sets/{s.Id}"))
            .WithName("CreateAttributeSet")
            .WithSummary("Define an attribute set for a category.")
            .RequirePermission(Permissions.Catalog.ManageTaxonomy)
            .Validate<Request>()
            .Produces<AttributeSetResponse>(StatusCodes.Status201Created);
}
