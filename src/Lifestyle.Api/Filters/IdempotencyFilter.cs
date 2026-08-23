using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;

namespace Lifestyle.Api.Filters;

/// <summary>
/// Implements <c>Idempotency-Key</c> (FRD §19.6). The key plus the endpoint plus a hash of the body
/// is stored with the response for 24 hours: a repeat with the same key and body replays the stored
/// response; the same key with a different body is a 422, because silently succeeding would hide a
/// real client bug.
/// <para>
/// Not applied anywhere yet — payment, order placement, refund and payout are Phase 4. It is built
/// now because retrofitting idempotency after those endpoints exist is much harder.
/// </para>
/// </summary>
internal sealed class IdempotencyFilter(IDistributedCache cache) : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(key))
        {
            return SharedKernel.Http.ResultExtensions.Problem(Error.Validation(
                "idempotency.key_required",
                $"This endpoint requires an {HeaderName} header."));
        }

        if (key.Length > 200)
        {
            return SharedKernel.Http.ResultExtensions.Problem(Error.Validation(
                "idempotency.key_too_long", $"{HeaderName} must be 200 characters or fewer."));
        }

        var bodyHash = await HashBodyAsync(http);
        var cacheKey = string.Create(CultureInfo.InvariantCulture,
            $"idem:{http.Request.Method}:{http.Request.Path}:{key}");

        var stored = await cache.GetStringAsync(cacheKey, http.RequestAborted);

        if (stored is not null)
        {
            var separator = stored.IndexOf('|', StringComparison.Ordinal);
            var storedHash = stored[..separator];

            if (!string.Equals(storedHash, bodyHash, StringComparison.Ordinal))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Idempotency key reused with a different body",
                    detail: "This idempotency key was already used for a different request.",
                    type: "https://errors.lifestyle.dev/idempotency.key_reused");
            }

            return Results.Content(stored[(separator + 1)..], "application/json");
        }

        var result = await next(context);

        // Only successful responses are recorded. Replaying a failure would pin a transient error
        // in place for 24 hours.
        if (http.Response.StatusCode is >= 200 and < 300)
        {
            await cache.SetStringAsync(
                cacheKey,
                $"{bodyHash}|{System.Text.Json.JsonSerializer.Serialize(result)}",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Retention },
                http.RequestAborted);
        }

        return result;
    }

    private static async Task<string> HashBodyAsync(HttpContext http)
    {
        http.Request.EnableBuffering();
        http.Request.Body.Position = 0;

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(http.Request.Body, http.RequestAborted);

        http.Request.Body.Position = 0;
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

internal static class IdempotencyExtensions
{
    public static RouteHandlerBuilder RequireIdempotency(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<IdempotencyFilter>();
}

file static class Results
{
    public static IResult Problem(int statusCode, string title, string detail, string type) =>
        Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: statusCode, title: title, detail: detail, type: type);

    public static IResult Content(string content, string contentType) =>
        Microsoft.AspNetCore.Http.Results.Content(content, contentType);
}
