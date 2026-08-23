using Lifestyle.Modules.Vendors.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lifestyle.Modules.Vendors.Persistence.Configurations;

internal sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> b)
    {
        b.ToTable("vendors", VendorsSchema.Name);
        b.HasKey(v => v.Id);

        b.Property(v => v.LegalName).HasMaxLength(300).IsRequired();
        b.Property(v => v.DisplayName).HasMaxLength(200).IsRequired();

        // citext so slug lookups are case-insensitive without defeating the unique index —
        // hosts are case-insensitive, so "MyShop" and "myshop" must not be two vendors.
        b.Property(v => v.Slug).HasColumnType("citext").HasMaxLength(80).IsRequired();
        b.HasIndex(v => v.Slug).IsUnique();

        b.Property(v => v.CustomDomain).HasColumnType("citext").HasMaxLength(253);
        b.HasIndex(v => v.CustomDomain).IsUnique().HasFilter("custom_domain IS NOT NULL");
        b.Property(v => v.CustomDomainStatus).HasConversion<int>();

        b.Property(v => v.ContactEmail).HasColumnType("citext").HasMaxLength(320).IsRequired();
        b.Property(v => v.ContactPhone).HasMaxLength(32).IsRequired();
        b.Property(v => v.RegistrationNumber).HasMaxLength(60);
        b.Property(v => v.WhatsAppNumber).HasMaxLength(32);

        b.Property(v => v.Status).HasConversion<int>();
        b.Property(v => v.StatusReason).HasMaxLength(1000);

        b.Property(v => v.About).HasMaxLength(4000);
        b.Property(v => v.AccentColour).HasMaxLength(9);
        b.Property(v => v.LogoMediaId).HasMaxLength(64);
        b.Property(v => v.BannerMediaId).HasMaxLength(64);

        // The approval queue is the hot admin query: filter by status, oldest first.
        b.HasIndex(v => new { v.Status, v.SubmittedAt });

        b.Property<uint>("xmin").IsRowVersion();

        b.HasMany(v => v.Staff).WithOne().HasForeignKey(s => s.VendorId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(v => v.Documents).WithOne().HasForeignKey(d => d.VendorId).OnDelete(DeleteBehavior.Cascade);

        b.Navigation(v => v.Staff).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(v => v.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(v => v.DomainEvents);
        b.Ignore(v => v.OwnerUserId);
        b.Ignore(v => v.CanSell);
    }
}

internal sealed class VendorStaffConfiguration : IEntityTypeConfiguration<VendorStaff>
{
    public void Configure(EntityTypeBuilder<VendorStaff> b)
    {
        b.ToTable("vendor_staff", VendorsSchema.Name);
        b.HasKey(s => s.Id);

        b.Property(s => s.Role).HasConversion<int>();
        b.HasIndex(s => new { s.VendorId, s.UserId }).IsUnique();

        // "Which vendors does this user staff?" — the seller-surface login path.
        b.HasIndex(s => s.UserId);
    }
}

internal sealed class VendorDocumentConfiguration : IEntityTypeConfiguration<VendorDocument>
{
    public void Configure(EntityTypeBuilder<VendorDocument> b)
    {
        b.ToTable("vendor_documents", VendorsSchema.Name);
        b.HasKey(d => d.Id);

        b.Property(d => d.Kind).HasConversion<int>();
        b.Property(d => d.MediaId).HasMaxLength(64).IsRequired();
        b.Property(d => d.FileName).HasMaxLength(255).IsRequired();

        b.HasIndex(d => new { d.VendorId, d.Kind }).IsUnique();
    }
}

internal static class VendorsSchema
{
    public const string Name = "vendors";
}
