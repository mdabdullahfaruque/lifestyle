namespace Lifestyle.Modules.Identity.Contracts;

/// <summary>
/// JWT audiences (FRD §4.2). A buyer token is rejected by admin endpoints even if the user happens
/// to hold the role, because the token was not issued for that surface.
/// </summary>
public static class Surfaces
{
    public const string Buyer = "buyer";
    public const string Seller = "seller";
    public const string Admin = "admin";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal) { Buyer, Seller, Admin };
}

/// <summary>Stable ids for the seeded system roles (FRD §4.5).</summary>
public static class SystemRoles
{
    public const string Buyer = "Buyer";
    public const string VendorOwner = "Vendor Owner";
    public const string VendorStaff = "Vendor Staff";
    public const string SuperAdmin = "Super Admin";
    public const string CatalogModerator = "Catalog Moderator";
    public const string SupportAgent = "Support Agent";

    // Fixed ids so seeding is idempotent across environments and re-runs.
    public static readonly Guid BuyerId = new("00000000-0000-0000-0000-0000000000b1");
    public static readonly Guid VendorOwnerId = new("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid VendorStaffId = new("00000000-0000-0000-0000-0000000000a2");
    public static readonly Guid SuperAdminId = new("00000000-0000-0000-0000-0000000000a3");
    public static readonly Guid CatalogModeratorId = new("00000000-0000-0000-0000-0000000000a4");
    public static readonly Guid SupportAgentId = new("00000000-0000-0000-0000-0000000000a5");
}
