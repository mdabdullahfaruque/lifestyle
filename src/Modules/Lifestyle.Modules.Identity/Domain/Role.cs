using Lifestyle.SharedKernel.Domain;

namespace Lifestyle.Modules.Identity.Domain;

/// <summary>
/// A named bundle of permissions (FRD §4.5). Roles are data, so an admin can adjust the permission
/// set without a deployment — but the permission *keys* are code (<see cref="Permissions"/>).
/// </summary>
internal sealed class Role : Entity
{
    private Role() { }

    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = string.Empty;

    /// <summary>Which surface this role applies to: <c>buyer</c>, <c>seller</c> or <c>admin</c>.</summary>
    public string Surface { get; private set; } = null!;

    /// <summary>System roles are seeded and cannot be deleted or renamed by an admin.</summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// Mapped as a PostgreSQL <c>text[]</c> primitive collection, not ignored — the permission
    /// resolver projects this in SQL, so it has to be a real mapped property.
    /// </summary>
    public List<string> Permissions { get; private set; } = [];

    /// <summary>
    /// Seeded roles pass a fixed <paramref name="id"/> so re-seeding is idempotent and issued
    /// tokens stay meaningful across deployments.
    /// </summary>
    public static Role Create(Guid id, string name, string surface, string description, bool isSystem, IEnumerable<string> permissions) => new()
    {
        Id = id,
        Name = name,
        Surface = surface,
        Description = description,
        IsSystem = isSystem,
        Permissions = [.. permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]
    };

    public void SetPermissions(IEnumerable<string> permissions) =>
        Permissions = [.. permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}

/// <summary>
/// A role granted to a user, optionally scoped to one vendor. <see cref="ScopeId"/> is null for
/// platform-wide roles and set to the vendor id for seller roles — that is what makes
/// "owner of vendor A, staff of vendor B" expressible.
/// </summary>
internal sealed class UserRole : Entity
{
    private UserRole() { }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? ScopeId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }

    public Role Role { get; private set; } = null!;

    public static UserRole Create(Guid userId, Guid roleId, Guid? scopeId, DateTimeOffset now) => new()
    {
        UserId = userId,
        RoleId = roleId,
        ScopeId = scopeId,
        GrantedAt = now
    };
}
