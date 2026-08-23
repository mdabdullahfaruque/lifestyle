using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Lifestyle.Api.Middleware;

/// <summary>
/// Accepts an inbound <c>X-Correlation-Id</c>, generates one when absent, echoes it, and pushes it
/// onto the Serilog context so every log line for the request carries it (FRD §19.1).
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 100)
            correlationId = Guid.CreateVersion7().ToString("N");

        context.TraceIdentifier = correlationId;

        // OnStarting, not a direct write: the header must be set before the response begins, and
        // an endpoint may have written by the time control returns here.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
