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
using Microsoft.EntityFrameworkCore.Metadata;

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
    internal DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();

    DbSet<User> IIdentityDbContext.Users => Users;
    DbSet<Role> IIdentityDbContext.Roles => Roles;
    DbSet<UserRole> IIdentityDbContext.UserRoles => UserRoles;
    DbSet<RefreshToken> IIdentityDbContext.RefreshTokens => RefreshTokens;
    DbSet<UserExternalLogin> IIdentityDbContext.UserExternalLogins => UserExternalLogins;

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

        ApplyApplicationGeneratedKeys(modelBuilder);
        ApplySoftDeleteFilters(modelBuilder);
        ApplyProviderSpecificIndexes(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Declares every Guid primary key as application-generated.
    /// <para>
    /// This is not cosmetic. <see cref="SharedKernel.Domain.Entity"/> assigns
    /// <c>Guid.CreateVersion7()</c> in its field initialiser, so a new child arrives at the change
    /// tracker with its key already set. EF's default for a Guid key is <c>ValueGenerated.OnAdd</c>,
    /// and its graph attacher reads "store-generated key + value already set" as "this row already
    /// exists" — so a child added to a <em>loaded</em> aggregate is tracked as <c>Modified</c> and
    /// EF issues an UPDATE against a row that was never inserted.
    /// </para>
    /// <para>
    /// That silently broke every load-aggregate-then-add-child path: issuing a refresh token on
    /// login, attaching a KYC document, adding a variant or image, adding vendor staff. Declaring
    /// the keys never-generated makes EF track them as <c>Added</c>, which is what they are.
    /// </para>
    /// </summary>
    private static void ApplyApplicationGeneratedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey is null) continue;

            foreach (var property in primaryKey.Properties.Where(p => p.ClrType == typeof(Guid)))
                property.ValueGenerated = ValueGenerated.Never;
        }
    }

    /// <summary>
    /// PostgreSQL-specific index tuning. It lives here rather than in a module's EF configuration
    /// because naming an operator class needs the Npgsql provider, and modules must not reference
    /// it (docs/04 §4.3). Infrastructure already depends on the provider, so this is its job.
    /// </summary>
    private static void ApplyProviderSpecificIndexes(ModelBuilder modelBuilder)
    {
        // text_pattern_ops is what lets PostgreSQL use this index for LIKE 'prefix%' under a
        // non-C collation. That prefix scan resolves "this category and everything under it" on
        // every browse request; without the operator class it degrades to a sequential scan.
        modelBuilder.Entity<Category>()
            .HasIndex(c => c.Path)
            .HasOperators("text_pattern_ops");
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
