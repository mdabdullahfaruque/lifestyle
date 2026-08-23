using Lifestyle.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Identity.Persistence;

/// <summary>
/// Everything Identity is allowed to see. Implemented by <c>AppDbContext</c>. A handler in this
/// module cannot query another module's tables — the compiler stops it (docs/04 §4.2).
/// </summary>
internal interface IIdentityDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
