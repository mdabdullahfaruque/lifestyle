using FluentValidation;
using Lifestyle.Modules.Catalog.Contracts;
using Lifestyle.Modules.Catalog.Features.Admin;
using Lifestyle.Modules.Catalog.Features.Attributes;
using Lifestyle.Modules.Catalog.Features.Categories;
using Lifestyle.Modules.Catalog.Features.Inventory;
using Lifestyle.Modules.Catalog.Features.Products;
using Lifestyle.Modules.Catalog.Internal;
using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.SharedKernel.Domain;
using Lifestyle.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lifestyle.Modules.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddOptions<CatalogModuleOptions>()
            .BindConfiguration(CatalogModuleOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ICatalogModule, CatalogFacade>();
        services.AddScoped<VendorScope>();
        services.AddScoped<ProductSlugFactory>();

        services.AddScoped<IIntegrationEventHandler<VendorSuspendedEvent>, UnpublishProductsOnVendorSuspended>();

        services.AddValidatorsFromAssembly(typeof(CatalogModule).Assembly, includeInternalTypes: true);
        services.AddHandlersFromAssembly(typeof(CatalogModule).Assembly);

        return services;
    }

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Public: the marketplace and every storefront ──
        var pub = app.MapGroup("/v1/catalog").WithTags("Catalog");
        BrowseProducts.Map(pub);
        GetPublicProduct.Map(pub);
        GetCategoryTree.Map(pub);
        ListAttributeSets.Map(pub);

        // ── Vendor Admin ──
        var vendor = app.MapGroup("/v1/vendor/products").WithTags("Vendor · Products").RequireVendorStaff();
        ListOwnProducts.Map(vendor);
        GetOwnProduct.Map(vendor);
        CreateProduct.Map(vendor);
        UpdateProduct.Map(vendor);
        DeleteProduct.Map(vendor);
        SetProductImages.Map(vendor);
        SubmitProductForReview.Map(vendor);
        UnpublishOwnProduct.Map(vendor);
        AddVariant.Map(vendor);
        UpdateVariant.Map(vendor);
        RemoveVariant.Map(vendor);
        AdjustStock.Map(vendor);

        // ── Super Admin ──
        var adminCatalog = app.MapGroup("/v1/admin/catalog").WithTags("Admin · Catalog").RequireAdminSurface();
        ListProductsForModeration.Map(adminCatalog);
        GetProductForModeration.Map(adminCatalog);
        ModerateProduct.Map(adminCatalog);
        TakeDownProduct.Map(adminCatalog);
        CreateAttributeSet.Map(adminCatalog);

        var adminCategories = app.MapGroup("/v1/admin/catalog/categories")
            .WithTags("Admin · Categories").RequireAdminSurface();
        CreateCategory.Map(adminCategories);
        UpdateCategory.Map(adminCategories);

        return app;
    }
}
