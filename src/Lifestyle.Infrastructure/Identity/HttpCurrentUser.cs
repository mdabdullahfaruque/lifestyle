using System.Security.Claims;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Lifestyle.Infrastructure.Identity;

/// <summary>
/// Projects the validated JWT onto <see cref="ICurrentUser"/>. This is the only adapter between
/// <c>HttpContext</c> and feature code, which is what keeps handlers testable and free of
/// web-framework types.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);

    public string? Audience => Principal?.FindFirstValue(EndpointExtensions.SurfaceClaim);

    public Guid? VendorId =>
        Guid.TryParse(Principal?.FindFirstValue(EndpointExtensions.VendorIdClaim), out var id) ? id : null;

    public Guid? ImpersonatedBy =>
        Guid.TryParse(Principal?.FindFirstValue(EndpointExtensions.ImpersonatorClaim), out var id) ? id : null;

    public IReadOnlySet<string> Permissions =>
        Principal?.FindAll(EndpointExtensions.PermissionClaim)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(StringComparer.Ordinal);

    public bool HasPermission(string permission) => Permissions.Contains(permission);
}
