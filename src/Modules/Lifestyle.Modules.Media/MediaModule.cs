using FluentValidation;
using Lifestyle.Modules.Media.Contracts;
using Lifestyle.Modules.Media.Features.Library;
using Lifestyle.Modules.Media.Features.Uploads;
using Lifestyle.Modules.Media.Internal;
using Lifestyle.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lifestyle.Modules.Media;

public static class MediaModule
{
    public static IServiceCollection AddMediaModule(this IServiceCollection services)
    {
        services.AddOptions<MediaModuleOptions>()
            .BindConfiguration(MediaModuleOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IMediaModule, MediaFacade>();
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();

        services.AddValidatorsFromAssembly(typeof(MediaModule).Assembly, includeInternalTypes: true);
        services.AddHandlersFromAssembly(typeof(MediaModule).Assembly);

        return services;
    }

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var media = app.MapGroup("/v1/media").WithTags("Media").RequireRateLimiting(RateLimitPolicies.Uploads);
        UploadFile.Map(media);
        DownloadPrivateFile.Map(media);

        // ── Vendor Admin: the image library (docs/08 §2) ──
        var library = app.MapGroup("/v1/vendor/media")
            .WithTags("Vendor · Media")
            .RequireVendorStaff()
            .RequireRateLimiting(RateLimitPolicies.Uploads);
        ListVendorMedia.Map(library);
        BulkUploadToLibrary.Map(library);
        UploadLibraryZip.Map(library);
        DeleteVendorMedia.Map(library);

        return app;
    }
}
