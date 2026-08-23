using Lifestyle.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Catalog.Persistence;

internal interface ICatalogDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<AttributeSet> AttributeSets { get; }
    DbSet<ProductAttribute> ProductAttributes { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<ProductAttributeValue> ProductAttributeValues { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
