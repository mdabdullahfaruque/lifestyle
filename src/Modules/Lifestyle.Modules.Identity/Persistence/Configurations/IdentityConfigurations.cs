using Lifestyle.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lifestyle.Modules.Identity.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", IdentitySchema.Name);
        b.HasKey(u => u.Id);

        // citext: emails compare case-insensitively without LOWER() defeating the index (FRD §3.1).
        b.Property(u => u.Email).HasColumnType("citext").HasMaxLength(320).IsRequired();
        b.HasIndex(u => u.Email).IsUnique().HasFilter("deleted_at IS NULL");

        b.Property(u => u.PhoneNumber).HasMaxLength(32);
        b.HasIndex(u => u.PhoneNumber).IsUnique().HasFilter("phone_number IS NOT NULL AND deleted_at IS NULL");

        b.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        b.Property(u => u.Status).HasConversion<int>();
        b.Property(u => u.TwoFactorSecret).HasMaxLength(128);

        b.Property<uint>("xmin").IsRowVersion();

        b.HasMany(u => u.Roles)
            .WithOne()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(u => u.RefreshTokens)
            .WithOne()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(u => u.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(u => u.RefreshTokens).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(u => u.DomainEvents);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles", IdentitySchema.Name);
        b.HasKey(r => r.Id);

        b.Property(r => r.Name).HasMaxLength(100).IsRequired();
        b.Property(r => r.Surface).HasMaxLength(20).IsRequired();
        b.Property(r => r.Description).HasMaxLength(400);

        b.HasIndex(r => new { r.Name, r.Surface }).IsUnique();

        // text[] rather than jsonb: Npgsql maps it natively, it projects in SQL, and it can take a
        // GIN index if we ever need "which roles grant X".
        b.PrimitiveCollection(r => r.Permissions)
            .HasColumnName("permissions")
            .HasColumnType("text[]")
            .IsRequired();
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("user_roles", IdentitySchema.Name);
        b.HasKey(r => r.Id);

        b.HasIndex(r => new { r.UserId, r.RoleId, r.ScopeId }).IsUnique();
        b.HasIndex(r => r.ScopeId).HasFilter("scope_id IS NOT NULL");

        b.HasOne(r => r.Role)
            .WithMany()
            .HasForeignKey(r => r.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens", IdentitySchema.Name);
        b.HasKey(t => t.Id);

        b.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => t.FamilyId);

        // Drives the nightly purge of expired tokens.
        b.HasIndex(t => t.ExpiresAt);

        b.Property(t => t.RevokedReason).HasMaxLength(100);
        b.Property(t => t.UserAgent).HasMaxLength(400);
        b.Property(t => t.IpAddress).HasMaxLength(45);
    }
}

/// <summary>The PostgreSQL schema this module owns (docs/04 §4.1).</summary>
internal static class IdentitySchema
{
    public const string Name = "identity";
}
