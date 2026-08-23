using FluentValidation;
using Lifestyle.Modules.Platform.Contracts;
using Lifestyle.Modules.Platform.Features.Audit;
using Lifestyle.Modules.Platform.Internal;
using Lifestyle.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lifestyle.Modules.Platform;

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IPlatformSettings, PlatformSettings>();

        services.AddValidatorsFromAssembly(typeof(PlatformModule).Assembly, includeInternalTypes: true);
        services.AddHandlersFromAssembly(typeof(PlatformModule).Assembly);

        return services;
    }

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var audit = app.MapGroup("/v1/admin/audit").WithTags("Admin · Audit").RequireAdminSurface();
        ListAuditEntries.Map(audit);

        return app;
    }
}
