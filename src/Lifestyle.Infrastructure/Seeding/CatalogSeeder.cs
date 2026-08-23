using Lifestyle.Infrastructure.Persistence;
using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lifestyle.Infrastructure.Seeding;

/// <summary>
/// Seeds the four launch categories with real attribute sets (Plan Phase 1). Idempotent — matched
/// on the attribute-set code and the category slug, so re-running adds nothing.
/// </summary>
internal sealed class CatalogSeeder(AppDbContext db, IClock clock, ILogger<CatalogSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(ct))
        {
            logger.LogInformation("Categories already present; skipping catalog seed.");
            return;
        }

        var now = clock.UtcNow;
        var sets = BuildAttributeSets(now);

        db.AttributeSets.AddRange(sets.Values);
        await db.SaveChangesAsync(ct);

        var order = 0;

        // Two roots, four leaves. "Fashion" and "Electronics" give the tree somewhere to grow
        // without restructuring when categories five and six arrive.
        var fashion = Category.CreateRoot("Fashion", "fashion", order++, now);
        var electronics = Category.CreateRoot("Electronics", "electronics", order++, now);
        db.Categories.AddRange(fashion, electronics);

        var leaves = new List<Category>
        {
            Child(fashion, "Ladies' Dresses", sets["dresses"].Id, 0, now),
            Child(fashion, "Ladies' Bags", sets["bags"].Id, 1, now),
            Child(fashion, "Shoes", sets["shoes"].Id, 2, now),
            Child(electronics, "Mobile Accessories", sets["mobile_accessories"].Id, 0, now)
        };

        db.Categories.AddRange(leaves);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded {Sets} attribute sets and {Categories} categories.",
            sets.Count, leaves.Count + 2);
    }

    private static Category Child(Category parent, string name, Guid attributeSetId, int sortOrder, DateTimeOffset now)
    {
        var category = Category.CreateChild(parent, name, Slug.From(name), sortOrder, now);
        category.SetAttributeSet(attributeSetId, now);
        return category;
    }

    private static Dictionary<string, AttributeSet> BuildAttributeSets(DateTimeOffset now)
    {
        var dresses = AttributeSet.Create("Ladies' Dresses", "dresses", "Attributes for dresses and gowns.", now);
        dresses.AddAttribute(Size(["XS", "S", "M", "L", "XL", "XXL"], 0));
        dresses.AddAttribute(Colour(1));
        dresses.AddAttribute(ProductAttribute.Create("Material", "material", AttributeDataType.Select,
            isRequired: true, isVariantAxis: false, isFilterable: true, 2,
            allowedValues: ["Cotton", "Silk", "Chiffon", "Linen", "Georgette", "Polyester", "Rayon", "Denim"]));
        dresses.AddAttribute(ProductAttribute.Create("Sleeve Length", "sleeve_length", AttributeDataType.Select,
            isRequired: false, isVariantAxis: false, isFilterable: true, 3,
            allowedValues: ["Sleeveless", "Short", "Three-quarter", "Full"]));
        dresses.AddAttribute(ProductAttribute.Create("Occasion", "occasion", AttributeDataType.Select,
            isRequired: false, isVariantAxis: false, isFilterable: true, 4,
            allowedValues: ["Casual", "Formal", "Party", "Wedding", "Festive"]));

        var bags = AttributeSet.Create("Ladies' Bags", "bags", "Attributes for handbags and totes.", now);
        bags.AddAttribute(Colour(0));
        bags.AddAttribute(ProductAttribute.Create("Bag Type", "bag_type", AttributeDataType.Select,
            isRequired: true, isVariantAxis: false, isFilterable: true, 1,
            allowedValues: ["Handbag", "Tote", "Clutch", "Shoulder", "Crossbody", "Backpack", "Sling"]));
        bags.AddAttribute(ProductAttribute.Create("Material", "material", AttributeDataType.Select,
            isRequired: true, isVariantAxis: false, isFilterable: true, 2,
            allowedValues: ["Genuine Leather", "Faux Leather", "Canvas", "Nylon", "Jute", "Suede"]));
        bags.AddAttribute(ProductAttribute.Create("Closure", "closure", AttributeDataType.Select,
            isRequired: false, isVariantAxis: false, isFilterable: false, 3,
            allowedValues: ["Zip", "Magnetic", "Drawstring", "Flap", "Open"]));

        var shoes = AttributeSet.Create("Shoes", "shoes", "Attributes for men's and women's footwear.", now);
        // Two variant axes: size × colour. This is the case that exercises variant generation.
        shoes.AddAttribute(ProductAttribute.Create("Size (EU)", "size_eu", AttributeDataType.Select,
            isRequired: true, isVariantAxis: true, isFilterable: true, 0,
            allowedValues: ["35", "36", "37", "38", "39", "40", "41", "42", "43", "44", "45", "46"]));
        shoes.AddAttribute(Colour(1));
        shoes.AddAttribute(ProductAttribute.Create("Gender", "gender", AttributeDataType.Select,
            isRequired: true, isVariantAxis: false, isFilterable: true, 2,
            allowedValues: ["Women", "Men", "Unisex"]));
        shoes.AddAttribute(ProductAttribute.Create("Shoe Type", "shoe_type", AttributeDataType.Select,
            isRequired: true, isVariantAxis: false, isFilterable: true, 3,
            allowedValues: ["Sneaker", "Sandal", "Heel", "Flat", "Loafer", "Boot", "Slipper", "Formal"]));
        shoes.AddAttribute(ProductAttribute.Create("Heel Height", "heel_height_mm", AttributeDataType.Number,
            isRequired: false, isVariantAxis: false, isFilterable: false, 4, unit: "mm"));

        var mobile = AttributeSet.Create("Mobile Accessories", "mobile_accessories",
            "Attributes for phone cases, cables, chargers and audio.", now);
        mobile.AddAttribute(ProductAttribute.Create("Accessory Type", "accessory_type", AttributeDataType.Select,
            isRequired: true, isVariantAxis: false, isFilterable: true, 0,
            allowedValues: ["Case", "Screen Protector", "Charger", "Cable", "Power Bank", "Earphones", "Holder", "Stand"]));
        mobile.AddAttribute(Colour(1));
        mobile.AddAttribute(ProductAttribute.Create("Compatible Brand", "compatible_brand", AttributeDataType.Select,
            isRequired: false, isVariantAxis: false, isFilterable: true, 2,
            allowedValues: ["Apple", "Samsung", "Xiaomi", "Oppo", "Vivo", "Realme", "Huawei", "OnePlus", "Universal"]));
        mobile.AddAttribute(ProductAttribute.Create("Connector", "connector", AttributeDataType.Select,
            isRequired: false, isVariantAxis: false, isFilterable: true, 3,
            allowedValues: ["USB-C", "Lightning", "Micro-USB", "3.5mm", "Wireless", "Not applicable"]));

        return new Dictionary<string, AttributeSet>(StringComparer.Ordinal)
        {
            ["dresses"] = dresses,
            ["bags"] = bags,
            ["shoes"] = shoes,
            ["mobile_accessories"] = mobile
        };

    }

    private static ProductAttribute Size(IReadOnlyList<string> values, int sortOrder) =>
        ProductAttribute.Create("Size", "size", AttributeDataType.Select,
            isRequired: true, isVariantAxis: true, isFilterable: true, sortOrder, allowedValues: values);

    private static ProductAttribute Colour(int sortOrder) =>
        ProductAttribute.Create("Colour", "colour", AttributeDataType.Select,
            isRequired: true, isVariantAxis: true, isFilterable: true, sortOrder,
            allowedValues:
            [
                "Black", "White", "Grey", "Navy", "Blue", "Red", "Maroon", "Pink", "Purple",
                "Green", "Olive", "Yellow", "Orange", "Brown", "Beige", "Gold", "Silver", "Multicolour"
            ]);
}
