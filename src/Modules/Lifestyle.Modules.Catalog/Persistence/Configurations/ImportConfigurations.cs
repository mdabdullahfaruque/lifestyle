using System.Text.Json;
using Lifestyle.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lifestyle.Modules.Catalog.Persistence.Configurations;

internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> b)
    {
        b.ToTable("import_jobs", CatalogSchema.Name);
        b.HasKey(j => j.Id);

        b.Property(j => j.SourceFileName).HasMaxLength(255).IsRequired();
        b.Property(j => j.FailureReason).HasMaxLength(1000);
        b.Property(j => j.Status).HasConversion<int>();

        // The vendor's import history, newest first, and the lookup the expiry sweep uses.
        b.HasIndex(j => new { j.VendorId, j.CreatedAt });
        b.HasIndex(j => new { j.Status, j.ExpiresAt });

        b.Property<uint>("xmin").IsRowVersion();

        b.HasMany(j => j.Rows).WithOne().HasForeignKey(r => r.ImportJobId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(j => j.Images).WithOne().HasForeignKey(i => i.ImportJobId).OnDelete(DeleteBehavior.Cascade);

        b.Navigation(j => j.Rows).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(j => j.Images).UsePropertyAccessMode(PropertyAccessMode.Field);

        // CategoryId points at this module's own table, but no FK: a category deleted between
        // upload and commit should fail the commit with a readable error, not a constraint error.
        b.Ignore(j => j.DomainEvents);
    }
}

internal sealed class ImportJobRowConfiguration : IEntityTypeConfiguration<ImportJobRow>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ImportJobRow> b)
    {
        b.ToTable("import_job_rows", CatalogSchema.Name);
        b.HasKey(r => r.Id);

        b.Property(r => r.ProductCode).HasMaxLength(64);
        b.Property(r => r.Sku).HasMaxLength(80);
        b.Property(r => r.ErrorCode).HasMaxLength(100);
        b.Property(r => r.ErrorMessage).HasMaxLength(1000);
        b.Property(r => r.Outcome).HasConversion<int>();

        // The raw cells, kept verbatim so the errors-only sheet can be written back out with the
        // seller's own values in it. Read whole with the row, never filtered on — jsonb.
        b.Property(r => r.Values)
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

        b.HasIndex(r => new { r.ImportJobId, r.RowNumber });
        b.HasIndex(r => new { r.ImportJobId, r.ProductCode });
    }
}

internal sealed class ImportJobImageConfiguration : IEntityTypeConfiguration<ImportJobImage>
{
    public void Configure(EntityTypeBuilder<ImportJobImage> b)
    {
        b.ToTable("import_job_images", CatalogSchema.Name);
        b.HasKey(i => i.Id);

        b.Property(i => i.MediaId).HasMaxLength(64).IsRequired();
        b.Property(i => i.FileName).HasMaxLength(255).IsRequired();
        b.Property(i => i.ProductCode).HasMaxLength(64);
        b.Property(i => i.MatchedBy).HasMaxLength(40);
        b.Property(i => i.Confidence).HasConversion<int>();

        b.HasIndex(i => new { i.ImportJobId, i.ProductCode });
    }
}
