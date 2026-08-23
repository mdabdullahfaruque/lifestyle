using FluentValidation;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Vendors.Contracts;
using Lifestyle.Modules.Vendors.Features.Admin;
using Lifestyle.Modules.Vendors.Features.Storefronts;
using Lifestyle.Modules.Vendors.Features.Vendors;
using Lifestyle.Modules.Vendors.Internal;
using Lifestyle.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lifestyle.Modules.Vendors;

public static class VendorsModule
{
    public static IServiceCollection AddVendorsModule(this IServiceCollection services)
    {
        services.AddScoped<IVendorsModule, VendorsFacade>();
        services.AddScoped<ApplicantScope>();
        services.AddValidatorsFromAssembly(typeof(VendorsModule).Assembly, includeInternalTypes: true);
        services.AddHandlersFromAssembly(typeof(VendorsModule).Assembly);
        return services;
    }

    public static IEndpointRouteBuilder MapVendorsEndpoints(this IEndpointRouteBuilder app)
    {
        // Public — the storefront and marketplace shop pages.
        var storefronts = app.MapGroup("/v1/catalog/storefronts").WithTags("Storefronts");
        GetCurrentStorefront.Map(storefronts);
        GetStorefront.Map(storefronts);

        // Onboarding runs on an ordinary signed-in token. The seller role is granted on approval,
        // so requiring it here would make approval unreachable — see ApplicantScope.
        var applications = app.MapGroup("/v1/vendor-applications").WithTags("Vendor · Onboarding");
        ApplyAsVendor.Map(applications);
        GetMyApplication.Map(applications);
        UploadVendorDocument.Map(applications);
        SubmitVendorForReview.Map(applications);

        // Post-approval. Every endpoint here is scoped to the vendor in the token.
        var vendor = app.MapGroup("/v1/vendor").WithTags("Vendor").RequireVendorStaff();
        GetVendorProfile.Map(vendor);
        UpdateStorefront.Map(vendor);

        var admin = app.MapGroup("/v1/admin/vendors").WithTags("Admin · Vendors").RequireAdminSurface();
        ListVendorsForAdmin.Map(admin);
        GetVendorForAdmin.Map(admin);
        ReviewVendorApplication.Map(admin);
        SetVendorSuspension.Map(admin);

        return app;
    }
}
