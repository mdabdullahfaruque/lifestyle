using Lifestyle.Modules.Vendors.Domain;
using Shouldly;
using Xunit;
using VendorEvents = Lifestyle.Modules.Vendors.Domain.Events;

namespace Lifestyle.UnitTests.Vendors;

/// <summary>
/// Vendor onboarding: apply → KYC → review → approved (Plan Phase 1). Nothing a vendor does is
/// visible or sellable until a human has approved the application.
/// </summary>
public sealed class VendorOnboardingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.CreateVersion7();
    private static readonly Guid AdminId = Guid.CreateVersion7();

    private static Vendor NewApplication() =>
        Vendor.Apply("Aisha Trading Sdn Bhd", "Aisha Boutique", "aisha-boutique",
            "aisha@example.com", "+60123456789", "202401012345", OwnerId, Now);

    private static Vendor ReadyToSubmit()
    {
        var vendor = NewApplication();
        vendor.AddDocument(VendorDocumentKind.BusinessRegistration, "media-ssm", "ssm.pdf", Now);
        vendor.AddDocument(VendorDocumentKind.OwnerIdentity, "media-ic", "ic.jpg", Now);
        return vendor;
    }

    [Fact]
    public void An_application_starts_as_a_draft_that_cannot_sell()
    {
        var vendor = NewApplication();

        vendor.Status.ShouldBe(VendorStatus.Draft);
        vendor.CanSell.ShouldBeFalse();
        vendor.OwnerUserId.ShouldBe(OwnerId);
        vendor.Staff.ShouldHaveSingleItem().Role.ShouldBe(VendorStaffRole.Owner);
    }

    [Fact]
    public void Cannot_be_submitted_without_a_business_registration_document()
    {
        var vendor = NewApplication();
        vendor.AddDocument(VendorDocumentKind.OwnerIdentity, "media-ic", "ic.jpg", Now);

        var result = vendor.SubmitForReview(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("vendors.registration_document_missing");
    }

    [Fact]
    public void Cannot_be_submitted_without_an_identity_document()
    {
        var vendor = NewApplication();
        vendor.AddDocument(VendorDocumentKind.BusinessRegistration, "media-ssm", "ssm.pdf", Now);

        var result = vendor.SubmitForReview(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("vendors.identity_document_missing");
    }

    /// <summary>
    /// A reviewer should see one current copy of each document type, not an accumulating pile of
    /// re-uploads.
    /// </summary>
    [Fact]
    public void Re_uploading_a_document_type_replaces_the_previous_one()
    {
        var vendor = NewApplication();
        vendor.AddDocument(VendorDocumentKind.BusinessRegistration, "media-old", "old.pdf", Now);
        vendor.AddDocument(VendorDocumentKind.BusinessRegistration, "media-new", "new.pdf", Now.AddMinutes(5));

        var documents = vendor.Documents.Where(d => d.Kind == VendorDocumentKind.BusinessRegistration).ToList();
        documents.ShouldHaveSingleItem().MediaId.ShouldBe("media-new");
    }

    [Fact]
    public void A_complete_application_can_be_submitted()
    {
        var vendor = ReadyToSubmit();

        vendor.SubmitForReview(Now).IsSuccess.ShouldBeTrue();

        vendor.Status.ShouldBe(VendorStatus.PendingReview);
        vendor.SubmittedAt.ShouldBe(Now);
        vendor.CanSell.ShouldBeFalse("submitted is not approved");
    }

    [Fact]
    public void Approval_lets_the_vendor_sell_and_raises_an_event()
    {
        var vendor = ReadyToSubmit();
        vendor.SubmitForReview(Now);

        vendor.Approve(AdminId, Now).IsSuccess.ShouldBeTrue();

        vendor.Status.ShouldBe(VendorStatus.Approved);
        vendor.CanSell.ShouldBeTrue();
        vendor.ApprovedBy.ShouldBe(AdminId);
        vendor.DomainEvents.ShouldContain(e => e is VendorEvents.VendorApproved);
    }

    [Fact]
    public void A_draft_cannot_be_approved_without_review()
    {
        var vendor = ReadyToSubmit();

        var result = vendor.Approve(AdminId, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("vendors.not_pending");
    }

    [Fact]
    public void A_rejected_application_can_be_corrected_and_resubmitted()
    {
        var vendor = ReadyToSubmit();
        vendor.SubmitForReview(Now);
        vendor.Reject(AdminId, "Registration document is illegible.", Now);

        vendor.Status.ShouldBe(VendorStatus.Rejected);
        vendor.StatusReason.ShouldBe("Registration document is illegible.");

        vendor.SubmitForReview(Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        vendor.Status.ShouldBe(VendorStatus.PendingReview);
    }

    /// <summary>
    /// Suspension must raise the event Catalog listens for, or a suspended vendor's products stay
    /// purchasable — the exact failure the integration event exists to prevent.
    /// </summary>
    [Fact]
    public void Suspension_stops_selling_and_raises_an_event()
    {
        var vendor = ReadyToSubmit();
        vendor.SubmitForReview(Now);
        vendor.Approve(AdminId, Now);

        vendor.Suspend(AdminId, "Counterfeit goods reported.", Now).IsSuccess.ShouldBeTrue();

        vendor.Status.ShouldBe(VendorStatus.Suspended);
        vendor.CanSell.ShouldBeFalse();
        vendor.DomainEvents.ShouldContain(e => e is VendorEvents.VendorSuspended);
    }

    [Fact]
    public void Reinstatement_restores_selling()
    {
        var vendor = ReadyToSubmit();
        vendor.SubmitForReview(Now);
        vendor.Approve(AdminId, Now);
        vendor.Suspend(AdminId, "Under investigation.", Now);

        vendor.Reinstate(Now.AddDays(3)).IsSuccess.ShouldBeTrue();

        vendor.Status.ShouldBe(VendorStatus.Approved);
        vendor.CanSell.ShouldBeTrue();
        vendor.StatusReason.ShouldBeNull();
    }

    [Fact]
    public void A_custom_domain_requires_an_approved_vendor()
    {
        var vendor = ReadyToSubmit();

        vendor.RequestCustomDomain("aisha.com", Now).Error.Code.ShouldBe("vendors.not_approved");

        vendor.SubmitForReview(Now);
        vendor.Approve(AdminId, Now);

        vendor.RequestCustomDomain("Aisha.COM", Now).IsSuccess.ShouldBeTrue();
        vendor.CustomDomain.ShouldBe("aisha.com", "hosts are case-insensitive");
        vendor.CustomDomainStatus.ShouldBe(CustomDomainStatus.PendingVerification);
    }

    [Fact]
    public void A_vendor_has_exactly_one_owner()
    {
        var vendor = ReadyToSubmit();

        var result = vendor.AddStaff(Guid.CreateVersion7(), VendorStaffRole.Owner, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("vendors.single_owner");
    }

    [Fact]
    public void The_owner_cannot_be_removed_from_staff()
    {
        var vendor = ReadyToSubmit();

        var result = vendor.RemoveStaff(OwnerId, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("vendors.cannot_remove_owner");
    }

    [Fact]
    public void Staff_can_be_added_and_removed()
    {
        var vendor = ReadyToSubmit();
        var staffId = Guid.CreateVersion7();

        vendor.AddStaff(staffId, VendorStaffRole.Manager, Now).IsSuccess.ShouldBeTrue();
        vendor.HasStaff(staffId).ShouldBeTrue();

        vendor.AddStaff(staffId, VendorStaffRole.Staff, Now).Error.Code.ShouldBe("vendors.staff_exists");

        vendor.RemoveStaff(staffId, Now).IsSuccess.ShouldBeTrue();
        vendor.HasStaff(staffId).ShouldBeFalse();
    }
}
