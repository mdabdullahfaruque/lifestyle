using System.Reflection;
using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Identity.Domain;
using Lifestyle.Modules.Identity.Persistence;
using Lifestyle.Modules.Media.Domain;
using Lifestyle.Modules.Media.Persistence;
using Lifestyle.Modules.Platform.Domain;
using Lifestyle.Modules.Platform.Persistence;
using Lifestyle.Modules.Vendors.Domain;
using Lifestyle.Modules.Vendors.Persistence;
using Lifestyle.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Infrastructure.Persistence;

/// <summary>
/// The single EF context (docs/04 §4.1). It implements every module's context interface, and each
/// module is registered against only its own — so a Catalog handler cannot reach the users table
/// even though the same object is behind both interfaces.
/// <para>
/// One context, one migration history, and a cross-module use case is a single SaveChanges. Module
/// separation comes from the per-module PostgreSQL schema and the narrow interfaces, not from
/// having fourteen DbContexts.
/// </para>
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options),
        IIdentityDbContext,
        IVendorsDbContext,
        ICatalogDbContext,
        IMediaDbContext,
        IPlatformDbContext
{
    /// <summary>Every module assembly whose EF configurations and entities must be discovered.</summary>
    internal static IReadOnlyList<Assembly> ModuleAssemblies { get; } =
    [
        typeof(User).Assembly,
        typeof(Vendor).Assembly,
        typeof(Product).Assembly,
        typeof(MediaFile).Assembly,
        typeof(AuditEntry).Assembly
    ];

    // ── identity ──
    internal DbSet<User> Users => Set<User>();
    internal DbSet<Role> Roles => Set<Role>();
    internal DbSet<UserRole> UserRoles => Set<UserRole>();
    internal DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    DbSet<User> IIdentityDbContext.Users => Users;
    DbSet<Role> IIdentityDbContext.Roles => Roles;
    DbSet<UserRole> IIdentityDbContext.UserRoles => UserRoles;
    DbSet<RefreshToken> IIdentityDbContext.RefreshTokens => RefreshTokens;

    // ── vendors ──
    internal DbSet<Vendor> Vendors => Set<Vendor>();
    internal DbSet<VendorStaff> VendorStaff => Set<VendorStaff>();
    internal DbSet<VendorDocument> VendorDocuments => Set<VendorDocument>();

    DbSet<Vendor> IVendorsDbContext.Vendors => Vendors;
    DbSet<VendorStaff> IVendorsDbContext.VendorStaff => VendorStaff;
    DbSet<VendorDocument> IVendorsDbContext.VendorDocuments => VendorDocuments;

    // ── catalog ──
    internal DbSet<Category> Categories => Set<Category>();
    internal DbSet<AttributeSet> AttributeSets => Set<AttributeSet>();
    internal DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    internal DbSet<Product> Products => Set<Product>();
    internal DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    internal DbSet<ProductImage> ProductImages => Set<ProductImage>();
    internal DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();

    DbSet<Category> ICatalogDbContext.Categories => Categories;
    DbSet<AttributeSet> ICatalogDbContext.AttributeSets => AttributeSets;
    DbSet<ProductAttribute> ICatalogDbContext.ProductAttributes => ProductAttributes;
    DbSet<Product> ICatalogDbContext.Products => Products;
    DbSet<ProductVariant> ICatalogDbContext.ProductVariants => ProductVariants;
    DbSet<ProductImage> ICatalogDbContext.ProductImages => ProductImages;
    DbSet<ProductAttributeValue> ICatalogDbContext.ProductAttributeValues => ProductAttributeValues;

    // ── media ──
    internal DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    internal DbSet<MediaDerivative> MediaDerivatives => Set<MediaDerivative>();

    DbSet<MediaFile> IMediaDbContext.MediaFiles => MediaFiles;
    DbSet<MediaDerivative> IMediaDbContext.MediaDerivatives => MediaDerivatives;

    // ── platform ──
    internal DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    internal DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    DbSet<AuditEntry> IPlatformDbContext.AuditEntries => AuditEntries;
    DbSet<PlatformSetting> IPlatformDbContext.PlatformSettings => PlatformSettings;

    Task<int> IIdentityDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);
    Task<int> IVendorsDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);
    Task<int> ICatalogDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);
    Task<int> IMediaDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);
    Task<int> IPlatformDbContext.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // citext for case-insensitive emails, slugs and hosts (FRD §3.1).
        modelBuilder.HasPostgresExtension("citext");

        foreach (var assembly in ModuleAssemblies)
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        ApplySoftDeleteFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Adds <c>WHERE deleted_at IS NULL</c> to every soft-deletable entity, so a handler that
    /// forgets the filter cannot resurrect deleted rows. Reach past it deliberately with
    /// <c>IgnoreQueryFilters()</c> — the uniqueness checks do exactly that.
    /// </summary>
    private static void ApplySoftDeleteFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType)) continue;

            var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var property = System.Linq.Expressions.Expression.Property(parameter, nameof(ISoftDeletable.DeletedAt));
            var body = System.Linq.Expressions.Expression.Equal(
                property,
                System.Linq.Expressions.Expression.Constant(null, typeof(DateTimeOffset?)));

            builder.Entity(entityType.ClrType)
                .HasQueryFilter(System.Linq.Expressions.Expression.Lambda(body, parameter));
        }
    }
}
