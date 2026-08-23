using System.Text.Json;
using Lifestyle.Modules.Platform.Contracts;
using Lifestyle.Modules.Platform.Domain;
using Lifestyle.Modules.Platform.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Platform.Internal;

/// <summary>
/// Adds the entry to the change tracker without saving. It commits with the caller's own
/// <c>SaveChanges</c>, so an action and its audit record are atomic — you cannot end up with an
/// approved vendor and no record of who approved it.
/// </summary>
internal sealed class AuditLog(
    IPlatformDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IHttpContextAccessor httpContextAccessor)
    : IAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, Guid? entityId, object? data = null)
    {
        var http = httpContextAccessor.HttpContext;

        db.AuditEntries.Add(AuditEntry.Record(
            action,
            entityType,
            entityId,
            currentUser.UserId,
            currentUser.ImpersonatedBy,
            currentUser.VendorId,
            http?.Connection.RemoteIpAddress?.ToString(),
            http?.TraceIdentifier,
            data is null ? null : JsonSerializer.Serialize(data, JsonOptions),
            clock.UtcNow));
    }
}

internal sealed class PlatformSettings(IPlatformDbContext db) : IPlatformSettings
{
    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var normalised = key.Trim().ToLowerInvariant();

        return await db.PlatformSettings
            .AsNoTracking()
            .Where(s => s.Key == normalised)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : struct, IParsable<T>
    {
        var raw = await GetAsync(key, ct);

        // A malformed setting reads as "not set" rather than throwing: ops typing "ten" into a
        // number field should degrade to the default, not take the site down.
        return raw is not null && T.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
