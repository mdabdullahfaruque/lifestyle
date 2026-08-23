using Lifestyle.Modules.Media.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lifestyle.Modules.Media.Persistence.Configurations;

internal sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> b)
    {
        b.ToTable("media_files", MediaSchema.Name);
        b.HasKey(m => m.Id);

        b.Property(m => m.PublicId).HasMaxLength(64).IsRequired();
        b.HasIndex(m => m.PublicId).IsUnique();

        b.Property(m => m.FileName).HasMaxLength(255).IsRequired();
        b.Property(m => m.ContentType).HasMaxLength(100).IsRequired();
        b.Property(m => m.StorageKey).HasMaxLength(500).IsRequired();
        b.Property(m => m.OwnerType).HasMaxLength(50);

        b.HasIndex(m => new { m.OwnerType, m.OwnerId }).HasFilter("owner_type IS NOT NULL");

        // Drives the orphan sweeper: unattached files older than the retention window.
        b.HasIndex(m => m.CreatedAt).HasFilter("owner_type IS NULL");

        b.HasIndex(m => m.VendorId).HasFilter("vendor_id IS NOT NULL");

        b.HasMany(m => m.Derivatives)
            .WithOne()
            .HasForeignKey(d => d.MediaFileId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(m => m.Derivatives).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(m => m.DomainEvents);
        b.Ignore(m => m.IsOrphan);
    }
}

internal sealed class MediaDerivativeConfiguration : IEntityTypeConfiguration<MediaDerivative>
{
    public void Configure(EntityTypeBuilder<MediaDerivative> b)
    {
        b.ToTable("media_derivatives", MediaSchema.Name);
        b.HasKey(d => d.Id);

        b.Property(d => d.Variant).HasMaxLength(20).IsRequired();
        b.Property(d => d.StorageKey).HasMaxLength(500).IsRequired();

        b.HasIndex(d => new { d.MediaFileId, d.Variant }).IsUnique();
    }
}

internal static class MediaSchema
{
    public const string Name = "media";
}
