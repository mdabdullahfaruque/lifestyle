using System.Text.Json;
using Lifestyle.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lifestyle.Modules.Catalog.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories", CatalogSchema.Name);
        b.HasKey(c => c.Id);

        b.Property(c => c.Name).HasMaxLength(150).IsRequired();
        b.Property(c => c.Slug).HasColumnType("citext").HasMaxLength(80).IsRequired();
        b.Property(c => c.Path).HasMaxLength(1000).IsRequired();
        b.Property(c => c.IconMediaId).HasMaxLength(64);

        b.HasIndex(c => c.Slug).IsUnique().HasFilter("deleted_at IS NULL");

        // Prefix scan for "this category and everything under it".
        // AppDbContext adds the text_pattern_ops operator class to this index: naming it here would
        // mean referencing the Npgsql provider from a module, which the architecture tests forbid.
        b.HasIndex(c => c.Path);

        b.HasIndex(c => new { c.ParentId, c.SortOrder });

        b.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AttributeSet>().WithMany().HasForeignKey(c => c.AttributeSetId).OnDelete(DeleteBehavior.SetNull);

        b.Ignore(c => c.DomainEvents);
        b.Ignore(c => c.DescendantPathPrefix);
    }
}

internal sealed class AttributeSetConfiguration : IEntityTypeConfiguration<AttributeSet>
{
    public void Configure(EntityTypeBuilder<AttributeSet> b)
    {
        b.ToTable("attribute_sets", CatalogSchema.Name);
        b.HasKey(a => a.Id);

        b.Property(a => a.Name).HasMaxLength(150).IsRequired();
        b.Property(a => a.Code).HasMaxLength(60).IsRequired();
        b.Property(a => a.Description).HasMaxLength(500);

        b.HasIndex(a => a.Code).IsUnique();

        b.HasMany(a => a.Attributes)
            .WithOne()
            .HasForeignKey(p => p.AttributeSetId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(a => a.Attributes).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(a => a.DomainEvents);
        b.Ignore(a => a.VariantAttributes);
    }
}

internal sealed class ProductAttributeConfiguration : IEntityTypeConfiguration<ProductAttribute>
{
    public void Configure(EntityTypeBuilder<ProductAttribute> b)
    {
        b.ToTable("product_attributes", CatalogSchema.Name);
        b.HasKey(a => a.Id);

        b.Property(a => a.Name).HasMaxLength(150).IsRequired();
        b.Property(a => a.Code).HasMaxLength(60).IsRequired();
        b.Property(a => a.DataType).HasConversion<int>();
        b.Property(a => a.Unit).HasMaxLength(20);

        b.PrimitiveCollection(a => a.AllowedValues).HasColumnType("text[]").IsRequired();

        b.HasIndex(a => new { a.AttributeSetId, a.Code }).IsUnique();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products", CatalogSchema.Name);
        b.HasKey(p => p.Id);

        b.Property(p => p.Name).HasMaxLength(300).IsRequired();
        b.Property(p => p.Slug).HasColumnType("citext").HasMaxLength(120).IsRequired();
        b.Property(p => p.Description).HasMaxLength(20000);
        b.Property(p => p.ShortDescription).HasMaxLength(500);
        b.Property(p => p.Brand).HasMaxLength(150);
        b.Property(p => p.ModerationNote).HasMaxLength(1000);

        b.Property(p => p.Status).HasConversion<int>();
        b.Property(p => p.Currency).HasColumnType("char(3)").IsRequired();

        b.Property(p => p.MinPrice).HasColumnType("numeric(18,4)");
        b.Property(p => p.MaxPrice).HasColumnType("numeric(18,4)");

        // Slug is unique per vendor, not globally: two shops may both sell a "red-silk-scarf".
        b.HasIndex(p => new { p.VendorId, p.Slug }).IsUnique().HasFilter("deleted_at IS NULL");

        // The vendor's own product list, newest first.
        b.HasIndex(p => new { p.VendorId, p.Status, p.CreatedAt });

        // Public category browse: only published rows are ever served, so the filter keeps the
        // index small and matches the query exactly.
        b.HasIndex(p => new { p.CategoryId, p.Status, p.MinPrice })
            .HasFilter("status = 3 AND deleted_at IS NULL");

        // The moderation queue.
        b.HasIndex(p => new { p.Status, p.SubmittedAt }).HasFilter("status = 2");

        b.Property<uint>("xmin").IsRowVersion();

        b.HasMany(p => p.Variants).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(p => p.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(p => p.AttributeValues).WithOne().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);

        b.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(p => p.Images).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(p => p.AttributeValues).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Categories live in this module, so this FK is fine. VendorId deliberately has no FK —
        // it points into another module's schema (docs/04 §4.3).
        b.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);

        b.Ignore(p => p.DomainEvents);
        b.Ignore(p => p.IsVisible);
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ProductVariant> b)
    {
        b.ToTable("product_variants", CatalogSchema.Name);
        b.HasKey(v => v.Id);

        b.Property(v => v.Sku).HasMaxLength(80).IsRequired();
        b.Property(v => v.AxisSignature).HasMaxLength(500).IsRequired();
        b.Property(v => v.Price).HasColumnType("numeric(18,4)");
        b.Property(v => v.CompareAtPrice).HasColumnType("numeric(18,4)");
        b.Property(v => v.WeightGrams).HasColumnType("numeric(10,2)");
        b.Property(v => v.BarCode).HasMaxLength(64);

        // Axis values are read whole with the variant and never filtered on in SQL — jsonb is the
        // right shape, and a value comparer keeps change tracking honest.
        b.Property(v => v.AxisValues)
            .HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, JsonOptions),
                json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new ValueComparer<Dictionary<string, string>>(
                    (l, r) => l != null && r != null && l.Count == r.Count && !l.Except(r).Any(),
                    d => d.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key.GetHashCode(StringComparison.Ordinal), kv.Value.GetHashCode(StringComparison.Ordinal))),
                    d => new Dictionary<string, string>(d, StringComparer.OrdinalIgnoreCase)))
            .IsRequired();

        b.HasIndex(v => new { v.ProductId, v.Sku }).IsUnique();
        b.HasIndex(v => new { v.ProductId, v.AxisSignature }).IsUnique();

        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.ToTable("product_images", CatalogSchema.Name);
        b.HasKey(i => i.Id);

        b.Property(i => i.MediaId).HasMaxLength(64).IsRequired();
        b.Property(i => i.AltText).HasMaxLength(300);

        b.HasIndex(i => new { i.ProductId, i.Position });
        b.HasIndex(i => new { i.ProductId, i.MediaId }).IsUnique();
    }
}

internal sealed class ProductAttributeValueConfiguration : IEntityTypeConfiguration<ProductAttributeValue>
{
    public void Configure(EntityTypeBuilder<ProductAttributeValue> b)
    {
        b.ToTable("product_attribute_values", CatalogSchema.Name);
        b.HasKey(a => a.Id);

        b.Property(a => a.Code).HasMaxLength(60).IsRequired();
        b.Property(a => a.Value).HasMaxLength(1000).IsRequired();

        b.HasIndex(a => new { a.ProductId, a.AttributeId }).IsUnique();

        // Faceted filtering: "all products where code=colour and value=Red".
        b.HasIndex(a => new { a.Code, a.Value });
    }
}

internal static class CatalogSchema
{
    public const string Name = "catalog";
}
