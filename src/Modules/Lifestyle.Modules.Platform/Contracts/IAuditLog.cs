namespace Lifestyle.Modules.Platform.Contracts;

/// <summary>
/// How any module records an auditable action. Writes are fire-and-forget from the caller's point
/// of view but land in the same transaction, so an audited action and its record commit together.
/// </summary>
public interface IAuditLog
{
    void Record(string action, string entityType, Guid? entityId, object? data = null);
}

/// <summary>Read access to platform settings for other modules.</summary>
public interface IPlatformSettings
{
    Task<string?> GetAsync(string key, CancellationToken ct);

    Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : struct, IParsable<T>;
}
