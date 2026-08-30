using Lifestyle.Api.Middleware;
using Lifestyle.Modules.Catalog;
using Lifestyle.Modules.Identity;
using Lifestyle.Modules.Media;
using Lifestyle.Modules.Platform;
using Lifestyle.Modules.Vendors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
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
        // First, before anything reads scheme or client IP. Caddy terminates TLS and talks plain
        // HTTP to this container, so without this every request looks like HTTP from the proxy's
        // container IP: the refresh cookie loses its Secure flag, and audit logs and refresh-token
        // records store Caddy's address instead of the caller's.
        //
        // KnownNetworks/KnownProxies are cleared because the container's peer (Caddy) has an
        // unpredictable Docker-network address. That is safe here, and only here, because nothing
        // but Caddy can reach this container: the internal network publishes no host port, and
        // Caddy overwrites X-Forwarded-For with its trusted-proxy-aware client IP — a client
        // cannot smuggle its own value through.
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
        app.UseForwardedHeaders(forwarded);

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
