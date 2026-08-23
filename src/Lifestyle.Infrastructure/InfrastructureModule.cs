using Lifestyle.Infrastructure.Identity;
using Lifestyle.Infrastructure.Outbox;
using Lifestyle.Infrastructure.Persistence;
using Lifestyle.Infrastructure.Persistence.Interceptors;
using Lifestyle.Infrastructure.Storage;
using Lifestyle.Infrastructure.Time;
using Lifestyle.Modules.Catalog.Persistence;
using Lifestyle.Modules.Identity.Persistence;
using Lifestyle.Modules.Media.Internal;
using Lifestyle.Modules.Media.Persistence;
using Lifestyle.Modules.Platform.Persistence;
using Lifestyle.Modules.Vendors.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lifestyle.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.SectionName);
        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName);

        services.AddHttpContextAccessor();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        // One instance behind two registrations: the middleware writes through TenantContext,
        // everything else reads the immutable interface.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddScoped<AuditInterceptor>();
        services.AddScoped<DomainEventsToOutboxInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                    configuration.GetConnectionString("Default"),
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform"))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(
                    sp.GetRequiredService<AuditInterceptor>(),
                    sp.GetRequiredService<DomainEventsToOutboxInterceptor>());

            // Lazy loading is off by default and stays off: it turns a list page into N+1 queries
            // without anyone noticing until production.
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
        });

        // Each module sees only its own slice of the context (docs/04 §4.2).
        services.AddScoped<IIdentityDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IVendorsDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ICatalogDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IMediaDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IPlatformDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        AddStorage(services, configuration);

        services.AddHostedService<OutboxDispatcher>();

        return services;
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration[$"{StorageOptions.SectionName}:Provider"] ?? "local";

        if (string.Equals(provider, "s3", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorage, S3FileStorage>();
        else
            services.AddSingleton<IFileStorage, LocalFileStorage>();
    }
}
