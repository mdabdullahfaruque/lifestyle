using Lifestyle.Api.Middleware;
using Lifestyle.Modules.Catalog;
using Lifestyle.Modules.Identity;
using Lifestyle.Modules.Media;
using Lifestyle.Modules.Platform;
using Lifestyle.Modules.Vendors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog;

namespace Lifestyle.Api.Composition;

internal static class EndpointRegistration
{
    /// <summary>
    /// The middleware pipeline, in the order FRD §1.4 specifies. Order here is behaviour: tenant
    /// resolution must precede authorisation, and the exception handler must wrap everything.
    /// </summary>
    public static WebApplication UseLifestyle(this WebApplication app)
    {
        app.UseExceptionHandler();

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLoggingConfigured();

        app.UseCors(ServiceRegistration.CorsPolicyName);

        // Before auth: an endpoint's authorisation may depend on the resolved tenant.
        app.UseMiddleware<TenantResolutionMiddleware>();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapLifestyleEndpoints();

        return app;
    }

    private static WebApplication MapLifestyleEndpoints(this WebApplication app)
    {
        app.MapIdentityEndpoints();
        app.MapPlatformEndpoints();
        app.MapVendorsEndpoints();
        app.MapMediaEndpoints();
        app.MapCatalogEndpoints();

        // Not publicly routed — the reverse proxy keeps /v1/internal/* to the private network.
        app.MapHealthChecks("/v1/internal/health").AllowAnonymous();

        app.MapGet("/v1/internal/ready", () => Results.Ok(new { status = "ready" }))
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }

    private static WebApplication UseSerilogRequestLoggingConfigured(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("Host", httpContext.Request.Host.Value ?? string.Empty);
                diagnosticContext.Set("UserId", httpContext.User.Identity?.IsAuthenticated == true
                    ? httpContext.User.FindFirst("sub")?.Value ?? "unknown"
                    : "anonymous");
            };
        });

        return app;
    }
}
