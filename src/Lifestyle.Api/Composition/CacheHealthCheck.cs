using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lifestyle.Api.Composition;

/// <summary>
/// Proves the distributed cache actually round-trips — a write followed by a read. Works the same
/// against Redis and the in-memory fallback, so tests need no special casing, and a production
/// Redis outage turns the health endpoint unhealthy instead of silently degrading idempotency and
/// rate limiting.
/// </summary>
internal sealed class CacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = $"health:{Guid.CreateVersion7():N}";
            var payload = new byte[] { 1 };

            await cache.SetAsync(key, payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) },
                cancellationToken);

            var read = await cache.GetAsync(key, cancellationToken);
            await cache.RemoveAsync(key, cancellationToken);

            return read is { Length: 1 }
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Cache read did not return what was written.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cache is unreachable.", ex);
        }
    }
}
