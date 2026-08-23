using Lifestyle.Modules.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lifestyle.Modules.Platform.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("audit_entries", PlatformSchema.Name);
        b.HasKey(a => a.Id);

        b.Property(a => a.Action).HasMaxLength(100).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        b.Property(a => a.IpAddress).HasMaxLength(45);
        b.Property(a => a.CorrelationId).HasMaxLength(100);
        b.Property(a => a.DataJson).HasColumnType("jsonb");

        // The three ways an investigator actually searches this table.
        b.HasIndex(a => new { a.EntityType, a.EntityId, a.OccurredAt });
        b.HasIndex(a => new { a.ActorUserId, a.OccurredAt });
        b.HasIndex(a => a.OccurredAt);
    }
}

internal sealed class PlatformSettingConfiguration : IEntityTypeConfiguration<PlatformSetting>
{
    public void Configure(EntityTypeBuilder<PlatformSetting> b)
    {
        b.ToTable("settings", PlatformSchema.Name);
        b.HasKey(s => s.Id);

        b.Property(s => s.Key).HasMaxLength(100).IsRequired();
        b.Property(s => s.Value).HasMaxLength(4000).IsRequired();
        b.Property(s => s.Description).HasMaxLength(500);

        b.HasIndex(s => s.Key).IsUnique();
    }
}

internal static class PlatformSchema
{
    public const string Name = "platform";
}
