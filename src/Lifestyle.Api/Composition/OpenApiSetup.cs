using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Lifestyle.Api.Composition;

internal static class OpenApiSetup
{
    private const string BearerScheme = "Bearer";

    /// <summary>
    /// The OpenAPI document is the authoritative API contract (FRD §19.2): the Angular client is
    /// generated from it in CI, so a breaking change fails the frontend build rather than
    /// surfacing at runtime.
    /// </summary>
    public static IServiceCollection AddOpenApiDocument(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Lifestyle API",
                Version = "v1",
                Description =
                    "Multi-vendor marketplace API. Endpoints are grouped by surface: /v1/catalog (public), "
                    + "/v1/me (buyer), /v1/vendor (seller), /v1/admin (platform)."
            });

            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Access token from /v1/auth/login. The refresh token is an HttpOnly cookie."
            });

            // Microsoft.OpenApi 3.x/2.x replaced the inline Reference property with a dedicated
            // reference type; the requirement points at the definition registered above.
            options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme)] = []
            });

            options.SupportNonNullableReferenceTypes();
        });

        return services;
    }

    public static WebApplication UseOpenApiDocument(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Lifestyle API v1");
            options.DocumentTitle = "Lifestyle API";
        });

        return app;
    }
}
