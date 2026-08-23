namespace Lifestyle.Modules.Identity.Contracts;

/// <summary>
/// Every permission key in the platform, as constants. Public because endpoints in other modules
/// gate on them (<c>.RequirePermission(Permissions.Catalog.Moderate)</c>).
/// <para>
/// Keys are <c>{resource}.{action}</c> and are never renamed once shipped — a rename silently
/// removes access. Add a new key and migrate the roles instead.
/// </para>
/// </summary>
public static class Permissions
{
    public static class Vendors
    {
        public const string Read = "vendors.read";
        public const string Approve = "vendors.approve";
        public const string Suspend = "vendors.suspend";
        public const string Impersonate = "vendors.impersonate";
        public const string ManageOwn = "vendors.manage_own";
        public const string ManageStaff = "vendors.manage_staff";
    }

    public static class Catalog
    {
        public const string ReadOwn = "catalog.read_own";
        public const string WriteOwn = "catalog.write_own";
        public const string PublishOwn = "catalog.publish_own";
        public const string Moderate = "catalog.moderate";
        public const string ManageTaxonomy = "catalog.manage_taxonomy";
    }

    public static class Inventory
    {
        public const string ReadOwn = "inventory.read_own";
        public const string WriteOwn = "inventory.write_own";
    }

    public static class Users
    {
        public const string Read = "users.read";
        public const string Suspend = "users.suspend";
        public const string ManageRoles = "users.manage_roles";
    }

    public static class Platform
    {
        public const string ReadSettings = "platform.read_settings";
        public const string WriteSettings = "platform.write_settings";
        public const string ReadAuditLog = "platform.read_audit_log";
    }

    public static class Media
    {
        public const string UploadOwn = "media.upload_own";
    }

    /// <summary>Every key, used to validate role definitions at seed time and in tests.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Vendors.Read, Vendors.Approve, Vendors.Suspend, Vendors.Impersonate, Vendors.ManageOwn, Vendors.ManageStaff,
        Catalog.ReadOwn, Catalog.WriteOwn, Catalog.PublishOwn, Catalog.Moderate, Catalog.ManageTaxonomy,
        Inventory.ReadOwn, Inventory.WriteOwn,
        Users.Read, Users.Suspend, Users.ManageRoles,
        Platform.ReadSettings, Platform.WriteSettings, Platform.ReadAuditLog,
        Media.UploadOwn
    };
}
