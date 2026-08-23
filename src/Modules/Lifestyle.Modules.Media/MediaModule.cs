using FluentValidation;
using Lifestyle.Modules.Media.Contracts;
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
        var media = app.MapGroup("/v1/media").WithTags("Media");
        UploadFile.Map(media);
        return app;
    }
}
