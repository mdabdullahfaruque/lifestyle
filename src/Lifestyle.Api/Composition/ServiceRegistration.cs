using System.Text;
using System.Text.Json.Serialization;
using Lifestyle.Api.Middleware;
using Lifestyle.Infrastructure;
using Lifestyle.Modules.Catalog;
using Lifestyle.Modules.Identity;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Identity.Internal;
using Lifestyle.Modules.Media;
using Lifestyle.Modules.Platform;
using Lifestyle.Modules.Vendors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        AddAuthentication(services);
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

    private static void AddAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured from IdentityModuleOptions rather than read from IConfiguration here.
        //
        // Reading the signing key eagerly at registration time meant validation could bind a
        // different value than TokenService did — any configuration source added after the builder
        // was constructed (a test host's in-memory overrides, for instance) reached the lazily
        // bound options but not this call. The tokens were then signed with one key and validated
        // with another, and every authenticated request failed with a bare 401.
        //
        // Binding both sides through the same options object makes that class of mismatch
        // impossible, and IdentityModuleOptions already validates the key on start.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<IdentityModuleOptions>>((jwt, identity) =>
            {
                var options = identity.Value;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.JwtIssuer,

                    // One audience per surface. A buyer token presented to an admin endpoint fails
                    // validation outright, before any permission check runs (FRD §4.2).
                    ValidateAudience = true,
                    ValidAudiences = [.. Surfaces.All.Select(s => $"{options.JwtAudienceBase}:{s}")],

                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtSigningKey)),

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
