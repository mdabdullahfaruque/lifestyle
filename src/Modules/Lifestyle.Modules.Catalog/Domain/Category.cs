using Lifestyle.SharedKernel.Domain;
using Lifestyle.SharedKernel.Results;

namespace Lifestyle.Modules.Catalog.Domain;

/// <summary>
/// A node in the platform category tree (FRD §7.4). Platform-owned, not vendor-owned: vendors pick
/// a category, they do not invent one, or the taxonomy stops being comparable across shops.
/// </summary>
internal sealed class Category : AggregateRoot, ISoftDeletable
{
    private Category() { }

    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public Guid? ParentId { get; private set; }

    /// <summary>
    /// Materialised path of ancestor ids, e.g. <c>/root/child/</c>. Makes "everything under X" one
    /// indexed prefix scan instead of a recursive CTE on every browse request.
    /// </summary>
    public string Path { get; private set; } = "/";

    public int Depth { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Only a leaf may hold products. Allowing products at an interior node makes "show me
    /// everything in Shoes" ambiguous and breaks faceting.
    /// </summary>
    public bool IsLeaf { get; private set; } = true;

    public Guid? AttributeSetId { get; private set; }
    public string? IconMediaId { get; private set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static Category CreateRoot(string name, string slug, int sortOrder, DateTimeOffset now) => new()
    {
        Name = name.Trim(),
        Slug = slug,
        ParentId = null,
        Depth = 0,
        SortOrder = sortOrder,
        CreatedAt = now,
        Path = "/"
    };

    public static Category CreateChild(Category parent, string name, string slug, int sortOrder, DateTimeOffset now)
    {
        var child = new Category
        {
            Name = name.Trim(),
            Slug = slug,
            ParentId = parent.Id,
            Depth = parent.Depth + 1,
            SortOrder = sortOrder,
            CreatedAt = now
        };

        child.Path = $"{parent.Path}{parent.Id:N}/";
        parent.MarkAsBranch(now);
        return child;
    }

    private void MarkAsBranch(DateTimeOffset now)
    {
        IsLeaf = false;
        UpdatedAt = now;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = name.Trim();
        UpdatedAt = now;
    }

    public void SetAttributeSet(Guid? attributeSetId, DateTimeOffset now)
    {
        AttributeSetId = attributeSetId;
        UpdatedAt = now;
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedAt = now;
    }

    public void Reorder(int sortOrder, DateTimeOffset now)
    {
        SortOrder = sortOrder;
        UpdatedAt = now;
    }

    public void SetIcon(string? iconMediaId, DateTimeOffset now)
    {
        IconMediaId = iconMediaId;
        UpdatedAt = now;
    }

    public Result EnsureCanHoldProducts() => IsLeaf
        ? IsActive
            ? Result.Success()
            : Error.Validation("catalog.category_inactive", "That category is not currently accepting products.")
        : Error.Validation("catalog.category_not_leaf", "Products must be listed in a specific sub-category.");

    /// <summary>The prefix that matches this category and everything beneath it.</summary>
    public string DescendantPathPrefix => $"{Path}{Id:N}/";
}
