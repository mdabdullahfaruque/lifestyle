using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Lifestyle.SharedKernel.Http;

/// <summary>
/// Authorisation helpers used at the point an endpoint is mapped, so the requirement is visible in
/// the same line as the route (docs/04 §3.1).
/// </summary>
public static class EndpointExtensions
{
    /// <summary>The claim type carrying one granted permission. Matches Identity's token builder.</summary>
    public const string PermissionClaim = "perm";

    /// <summary>The claim type carrying which surface the token was minted for.</summary>
    public const string SurfaceClaim = "surface";

    /// <summary>The vendor a seller-surface token acts for.</summary>
    public const string VendorIdClaim = "vendor_id";

    /// <summary>The real admin behind an impersonated session (BR-A-03).</summary>
    public const string ImpersonatorClaim = "act";

    public const string BuyerSurface = "buyer";
    public const string SellerSurface = "seller";
    public const string AdminSurface = "admin";

    /// <summary>
    /// Requires an authenticated caller holding <paramref name="permission"/>. Built as an inline
    /// policy rather than a named one so adding a permission never means also registering a policy
    /// — a step that is easy to forget and fails open-ended.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(PermissionClaim, permission)
            .Build());

    /// <summary>Requires any one of several permissions.</summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(PermissionClaim, permissions)
            .Build());

    /// <summary>
    /// Requires a token minted for the seller surface. The vendor the caller acts for comes from
    /// the token's <c>vendor_id</c>, never from the request — that is what stops one vendor from
    /// addressing another's data by changing a parameter.
    /// </summary>
    public static TBuilder RequireVendorStaff<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(SurfaceClaim, SellerSurface)
            .Build());

    /// <summary>Requires a token minted for the admin surface.</summary>
    public static TBuilder RequireAdminSurface<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(SurfaceClaim, AdminSurface)
            .Build());
}
