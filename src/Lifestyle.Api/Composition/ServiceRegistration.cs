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
using Lifestyle.SharedKernel.Http;
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

        AddDistributedCache(services, configuration, builder.Environment);
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

        // Real checks, not a bare 200: the Dockerfile HEALTHCHECK, compose service_healthy gates
        // and deploy.sh --wait all key off this endpoint, so it must actually prove the database
        // and cache are reachable — an API that cannot reach Postgres is not healthy.
        services.AddHealthChecks()
            .AddDbContextCheck<Lifestyle.Infrastructure.Persistence.AppDbContext>("database")
            .AddCheck<CacheHealthCheck>("cache");

        AddRateLimiting(services, configuration);

        return builder;
    }

    private static void AddDistributedCache(
        IServiceCollection services, ConfigurationManager configuration, IWebHostEnvironment environment)
    {
        var redis = configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(redis))
        {
            // In-memory is only correct where a single instance is guaranteed and losing the
            // state on restart is fine — development and tests. In production a missing Redis
            // means idempotency keys and rate limits silently stop being shared, so fail loudly.
            if (environment.IsProduction())
                throw new InvalidOperationException(
                    "ConnectionStrings:Redis is not configured. Production requires Redis (compose provides one).");

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

    /// <summary>
    /// Per-IP throttles on the endpoints worth attacking (FRD §19.5). The client IP is real
    /// because UseForwardedHeaders runs first and Caddy sends a trusted-proxy-aware value.
    /// In-memory per instance — correct for the single-server deployment; revisit with Redis
    /// partitioning when a second instance exists.
    /// </summary>
    private static void AddRateLimiting(IServiceCollection services, ConfigurationManager configuration)
    {
        // Configurable so the integration-test host can raise them; production uses the defaults.
        var authPerMinute = configuration.GetValue("RateLimits:AuthPerMinute", 20);
        var uploadsPerMinute = configuration.GetValue("RateLimits:UploadsPerMinute", 30);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };

            limiter.AddPolicy(RateLimitPolicies.Auth, context =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            limiter.AddPolicy(RateLimitPolicies.Uploads, context =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = uploadsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });
    }
}
