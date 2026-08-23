using System.Text;
using System.Text.Json.Serialization;
using Lifestyle.Api.Middleware;
using Lifestyle.Infrastructure;
using Lifestyle.Modules.Catalog;
using Lifestyle.Modules.Identity;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Media;
using Lifestyle.Modules.Platform;
using Lifestyle.Modules.Vendors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Lifestyle.Api.Composition;

/// <summary>
/// The composition root. Every module registers itself through one call, so this file never learns
/// a module's internals (docs/04 §3.1).
/// </summary>
internal static class ServiceRegistration
{
    public static WebApplicationBuilder AddLifestyle(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddOptions<PlatformOptions>()
            .BindConfiguration(PlatformOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        AddDistributedCache(services, configuration);
        AddAuthentication(services, configuration);
        AddCors(services, configuration);

        services.AddInfrastructure(configuration);

        // ── Modules ──
        services.AddIdentityModule();
        services.AddPlatformModule();
        services.AddVendorsModule();
        services.AddMediaModule();
        services.AddCatalogModule();

        services.AddOpenApiDocument();
        services.AddHealthChecks();

        return builder;
    }

    private static void AddDistributedCache(IServiceCollection services, ConfigurationManager configuration)
    {
        var redis = configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(redis))
        {
            // In-memory is correct for a single-instance dev box. Rate limits and idempotency keys
            // will not be shared across instances, which is why production must set Redis.
            services.AddDistributedMemoryCache();
            return;
        }

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redis;
            options.InstanceName = "lifestyle:";
        });
    }

    private static void AddAuthentication(IServiceCollection services, ConfigurationManager configuration)
    {
        var signingKey = configuration["Identity:JwtSigningKey"]
            ?? throw new InvalidOperationException("Identity:JwtSigningKey is required.");

        var issuer = configuration["Identity:JwtIssuer"]
            ?? throw new InvalidOperationException("Identity:JwtIssuer is required.");

        var audienceBase = configuration["Identity:JwtAudienceBase"]
            ?? throw new InvalidOperationException("Identity:JwtAudienceBase is required.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,

                    // One audience per surface. A buyer token presented to an admin endpoint fails
                    // validation outright, before any permission check runs (FRD §4.2).
                    ValidateAudience = true,
                    ValidAudiences = [.. Surfaces.All.Select(s => $"{audienceBase}:{s}")],

                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),

                    ValidateLifetime = true,
                    // Default is five minutes, which silently extends every token's life.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization();
    }

    private static void AddCors(IServiceCollection services, ConfigurationManager configuration)
    {
        var origins = configuration.GetSection($"{PlatformOptions.SectionName}:CorsOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
        {
            if (origins.Length == 0)
            {
                // No wildcard fallback: the refresh cookie requires credentialed CORS, and
                // AllowAnyOrigin cannot be combined with AllowCredentials. Failing closed here is
                // a misconfigured deployment; failing open would be a vulnerability.
                policy.WithOrigins("https://localhost:4200").AllowAnyHeader().AllowAnyMethod().AllowCredentials();
                return;
            }

            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders(CorrelationIdMiddleware.HeaderName);
        }));
    }

    public const string CorsPolicyName = "LifestyleFrontends";
}
