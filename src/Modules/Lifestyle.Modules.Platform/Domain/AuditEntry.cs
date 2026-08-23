using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Platform.Domain;

/// <summary>
/// An append-only record of an action that changed money, stock or vendor state (Plan §8,
/// Definition of Done). Never updated, never deleted — an audit log you can edit is not one.
/// </summary>
internal sealed class AuditEntry : Entity
{
    private AuditEntry() { }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Dotted action name, e.g. <c>vendor.approved</c>, <c>product.moderated</c>.</summary>
    public string Action { get; private set; } = null!;

    public string EntityType { get; private set; } = null!;
    public Guid? EntityId { get; private set; }

    public Guid? ActorUserId { get; private set; }

    /// <summary>Set when an admin acted while impersonating — the real person behind it (BR-A-03).</summary>
    public Guid? ImpersonatedBy { get; private set; }

    public Guid? VendorId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }

    /// <summary>Free-form context as jsonb: what changed, old and new values where useful.</summary>
    public string? DataJson { get; private set; }

    public static AuditEntry Record(
        string action, string entityType, Guid? entityId, Guid? actorUserId, Guid? impersonatedBy,
        Guid? vendorId, string? ipAddress, string? correlationId, string? dataJson, DateTimeOffset now) => new()
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ActorUserId = actorUserId,
            ImpersonatedBy = impersonatedBy,
            VendorId = vendorId,
            IpAddress = ipAddress,
            CorrelationId = correlationId,
            DataJson = dataJson,
            OccurredAt = now
        };
}

/// <summary>
/// A platform setting that ops can change without a deployment. Deliberately narrow: anything that
/// needs validation, migration or a restart belongs in configuration, not here.
/// </summary>
internal sealed class PlatformSetting : Entity
{
    private PlatformSetting() { }

    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public static PlatformSetting Create(string key, string value, string? description, DateTimeOffset now) => new()
    {
        Key = key.Trim().ToLowerInvariant(),
        Value = value,
        Description = description,
        UpdatedAt = now
    };

    public void SetValue(string value, Guid? updatedBy, DateTimeOffset now)
    {
        Value = value;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
