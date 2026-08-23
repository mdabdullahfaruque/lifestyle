using FluentValidation;
using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Lifestyle.SharedKernel.Identifiers;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Features.Categories;

public sealed record CategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    Guid? ParentId,
    int Depth,
    int SortOrder,
    bool IsLeaf,
    bool IsActive,
    Guid? AttributeSetId,
    string? IconMediaId,
    IReadOnlyList<CategoryResponse> Children);

/// <summary>
/// The whole active tree in one call. It is small (tens of nodes), changes rarely, and every
/// storefront needs it for navigation — so paging it would only add round trips.
/// </summary>
internal static class GetCategoryTree
{
    internal sealed class Handler(ICatalogDbContext db) : IHandler<bool, Result<IReadOnlyList<CategoryResponse>>>
    {
        public async Task<Result<IReadOnlyList<CategoryResponse>>> Handle(bool includeInactive, CancellationToken ct)
        {
            var query = db.Categories.AsNoTracking();
            if (!includeInactive) query = query.Where(c => c.IsActive);

            var all = await query.OrderBy(c => c.Depth).ThenBy(c => c.SortOrder).ToListAsync(ct);

            // Build the tree in memory: one query, then a single pass, rather than a query per level.
            var byParent = all.ToLookup(c => c.ParentId);

            List<CategoryResponse> Build(Guid? parentId) =>
                [.. byParent[parentId].Select(c => new CategoryResponse(
                    c.Id, c.Name, c.Slug, c.ParentId, c.Depth, c.SortOrder, c.IsLeaf, c.IsActive,
                    c.AttributeSetId, c.IconMediaId, Build(c.Id)))];

            return Result.Success<IReadOnlyList<CategoryResponse>>(Build(null));
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/categories", async (bool? includeInactive, Handler handler, CancellationToken ct) =>
                (await handler.Handle(includeInactive ?? false, ct)).ToHttpResult())
            .WithName("GetCategoryTree")
            .WithSummary("The full category tree.")
            .AllowAnonymous()
            .Produces<IReadOnlyList<CategoryResponse>>();
}

internal static class CreateCategory
{
    public sealed record Request(string Name, Guid? ParentId, int SortOrder, Guid? AttributeSetId, string? IconMediaId);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
            RuleFor(r => r.SortOrder).GreaterThanOrEqualTo(0);
            RuleFor(r => r.IconMediaId).MaximumLength(64);
        }
    }

    internal sealed class Handler(ICatalogDbContext db, IClock clock)
        : IHandler<Request, Result<CategoryResponse>>
    {
        public async Task<Result<CategoryResponse>> Handle(Request request, CancellationToken ct)
        {
            var slug = Slug.From(request.Name);
            if (!Slug.IsValid(slug))
                return Error.Validation("catalog.slug_invalid", "That category name cannot be turned into a web address.");

            if (await db.Categories.AnyAsync(c => c.Slug == slug, ct))
                return Error.Conflict("catalog.category_slug_taken", $"A category already uses '{slug}'.");

            Category category;

            if (request.ParentId is { } parentId)
            {
                var parent = await db.Categories.FirstOrDefaultAsync(c => c.Id == parentId, ct);
                if (parent is null) return Error.NotFound("catalog.parent_not_found");

                if (await db.Products.AnyAsync(p => p.CategoryId == parentId, ct))
                    return Error.Conflict("catalog.parent_has_products",
                        "That category already holds products, so it cannot gain sub-categories. Move the products first.");

                category = Category.CreateChild(parent, request.Name, slug, request.SortOrder, clock.UtcNow);
            }
            else
            {
                category = Category.CreateRoot(request.Name, slug, request.SortOrder, clock.UtcNow);
            }

            category.SetAttributeSet(request.AttributeSetId, clock.UtcNow);
            category.SetIcon(request.IconMediaId, clock.UtcNow);

            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);

            return new CategoryResponse(category.Id, category.Name, category.Slug, category.ParentId,
                category.Depth, category.SortOrder, category.IsLeaf, category.IsActive,
                category.AttributeSetId, category.IconMediaId, []);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/", async (Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(request, ct)).ToCreated(c => $"/v1/catalog/categories/{c.Id}"))
            .WithName("CreateCategory")
            .WithSummary("Add a category to the platform taxonomy.")
            .RequirePermission(Permissions.Catalog.ManageTaxonomy)
            .Validate<Request>()
            .Produces<CategoryResponse>(StatusCodes.Status201Created);
}

internal static class UpdateCategory
{
    public sealed record Request(string Name, int SortOrder, bool IsActive, Guid? AttributeSetId, string? IconMediaId);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
            RuleFor(r => r.SortOrder).GreaterThanOrEqualTo(0);
            RuleFor(r => r.IconMediaId).MaximumLength(64);
        }
    }

    internal sealed class Handler(ICatalogDbContext db, IClock clock)
        : IHandler<Handler.Command, Result<CategoryResponse>>
    {
        internal sealed record Command(Guid CategoryId, Request Request);

        public async Task<Result<CategoryResponse>> Handle(Command command, CancellationToken ct)
        {
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == command.CategoryId, ct);
            if (category is null) return Error.NotFound("catalog.category_not_found");

            var now = clock.UtcNow;

            // The slug is deliberately not regenerated on rename: it is in published URLs, and
            // silently changing it breaks every existing link and search-engine entry.
            category.Rename(command.Request.Name, now);
            category.Reorder(command.Request.SortOrder, now);
            category.SetActive(command.Request.IsActive, now);
            category.SetAttributeSet(command.Request.AttributeSetId, now);
            category.SetIcon(command.Request.IconMediaId, now);

            await db.SaveChangesAsync(ct);

            return new CategoryResponse(category.Id, category.Name, category.Slug, category.ParentId,
                category.Depth, category.SortOrder, category.IsLeaf, category.IsActive,
                category.AttributeSetId, category.IconMediaId, []);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/{categoryId:guid}", async (
                Guid categoryId, Request request, Handler handler, CancellationToken ct) =>
                (await handler.Handle(new Handler.Command(categoryId, request), ct)).ToHttpResult())
            .WithName("UpdateCategory")
            .WithSummary("Rename, reorder, activate or re-map a category. The slug never changes.")
            .RequirePermission(Permissions.Catalog.ManageTaxonomy)
            .Validate<Request>()
            .Produces<CategoryResponse>();
}
