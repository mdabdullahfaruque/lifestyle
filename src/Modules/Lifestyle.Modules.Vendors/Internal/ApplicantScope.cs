using Lifestyle.Modules.Vendors.Domain;
using Lifestyle.Modules.Vendors.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Lifestyle.Modules.Vendors.Internal;

/// <summary>
/// Resolves the vendor an applicant owns, from their <em>buyer</em> token.
/// <para>
/// Onboarding cannot use the seller surface: a seller token requires a vendor-scoped role, and that
/// role is only granted once the vendor is approved. An applicant who had to present a seller token
/// to upload their KYC documents could never get approved in the first place. So the application
/// flow authenticates as an ordinary signed-in user and finds the vendor through ownership.
/// </para>
/// </summary>
internal sealed class ApplicantScope(IVendorsDbContext db, ICurrentUser currentUser)
{
    /// <summary>
    /// Loads the vendor this caller owns, with documents and staff. Only the owner may act on an
    /// application — staff are added after approval, and never see the KYC documents.
    /// </summary>
    public async Task<Result<Vendor>> LoadOwnedApplicationAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthorized("identity.not_authenticated");

        var vendor = await db.Vendors
            .Include(v => v.Documents)
            .Include(v => v.Staff)
            .FirstOrDefaultAsync(v => v.Staff.Any(s => s.UserId == userId && s.Role == VendorStaffRole.Owner), ct);

        return vendor is null
            ? Error.NotFound("vendors.no_application", "You have not started a vendor application.")
            : vendor;
    }
}
