using Lifestyle.Modules.Catalog.Domain;
using Lifestyle.SharedKernel.Results;
using Shouldly;
using Xunit;
using CatalogEvents = Lifestyle.Modules.Catalog.Domain.Events;

namespace Lifestyle.UnitTests.Catalog;

/// <summary>
/// The draft → review → published state machine (FRD §7.5). These rules decide what buyers can
/// see, so they are enforced on the aggregate rather than in a request validator.
/// </summary>
public sealed class ProductLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid VendorId = Guid.CreateVersion7();
    private static readonly Guid CategoryId = Guid.CreateVersion7();

    private static Product NewProduct(string? description = "A lovely thing.") =>
        Product.Create(VendorId, CategoryId, "Silk Scarf", "silk-scarf", description, null, "Aisha", "MYR", Now);

    private static Product ReadyToSubmit()
    {
        var product = NewProduct();
        product.AddVariant("SKU-1", Options("colour", "Red"), 49.90m, null, 10, Now);
        product.AddImage("media-1", "Front", Now);
        return product;
    }

    private static Dictionary<string, string> Options(string key, string value) =>
        new(StringComparer.OrdinalIgnoreCase) { [key] = value };

    [Fact]
    public void Starts_as_a_draft_and_is_not_visible()
    {
        var product = NewProduct();

        product.Status.ShouldBe(ProductStatus.Draft);
        product.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Cannot_be_submitted_without_a_variant()
    {
        var product = NewProduct();
        product.AddImage("media-1", null, Now);

        var result = product.SubmitForReview(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.no_variants");
    }

    [Fact]
    public void Cannot_be_submitted_without_an_image()
    {
        var product = NewProduct();
        product.AddVariant("SKU-1", Options("colour", "Red"), 10m, null, 1, Now);

        var result = product.SubmitForReview(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.no_images");
    }

    [Fact]
    public void Cannot_be_submitted_without_a_description()
    {
        var product = NewProduct(description: null);
        product.AddVariant("SKU-1", Options("colour", "Red"), 10m, null, 1, Now);
        product.AddImage("media-1", null, Now);

        var result = product.SubmitForReview(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.no_description");
    }

    [Fact]
    public void Submitting_a_complete_product_moves_it_to_pending_review()
    {
        var product = ReadyToSubmit();

        product.SubmitForReview(Now).IsSuccess.ShouldBeTrue();

        product.Status.ShouldBe(ProductStatus.PendingReview);
        product.SubmittedAt.ShouldBe(Now);
        product.IsVisible.ShouldBeFalse("a submitted product is not live until a moderator approves it");
    }

    [Fact]
    public void Approval_publishes_and_raises_an_event()
    {
        var product = ReadyToSubmit();
        product.SubmitForReview(Now);

        product.Approve(Now).IsSuccess.ShouldBeTrue();

        product.Status.ShouldBe(ProductStatus.Published);
        product.PublishedAt.ShouldBe(Now);
        product.IsVisible.ShouldBeTrue();
        product.DomainEvents.ShouldContain(e => e is CatalogEvents.ProductPublished);
    }

    [Fact]
    public void A_draft_cannot_be_approved_directly()
    {
        var product = ReadyToSubmit();

        var result = product.Approve(Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.not_pending");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Rejection_records_the_reason_the_vendor_will_read()
    {
        var product = ReadyToSubmit();
        product.SubmitForReview(Now);

        product.Reject("Images are too low resolution.", Now).IsSuccess.ShouldBeTrue();

        product.Status.ShouldBe(ProductStatus.Rejected);
        product.ModerationNote.ShouldBe("Images are too low resolution.");
    }

    [Fact]
    public void A_rejected_product_can_be_fixed_and_resubmitted()
    {
        var product = ReadyToSubmit();
        product.SubmitForReview(Now);
        product.Reject("Not good enough.", Now);

        product.SubmitForReview(Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        product.Status.ShouldBe(ProductStatus.PendingReview);
        product.ModerationNote.ShouldBeNull();
    }

    /// <summary>
    /// Without this, moderation is trivially defeated: publish something bland, then edit it into
    /// whatever you actually wanted to sell.
    /// </summary>
    [Fact]
    public void Editing_a_published_product_sends_it_back_for_review()
    {
        var product = ReadyToSubmit();
        product.SubmitForReview(Now);
        product.Approve(Now);

        product.UpdateDetails(CategoryId, "Silk Scarf (Deluxe)", "Now with added claims.", null, "Aisha", Now.AddDays(1));

        product.Status.ShouldBe(ProductStatus.PendingReview);
        product.PublishedAt.ShouldBeNull();
        product.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Unpublishing_takes_a_live_product_down()
    {
        var product = ReadyToSubmit();
        product.SubmitForReview(Now);
        product.Approve(Now);

        product.Unpublish("vendor_request", Now).IsSuccess.ShouldBeTrue();

        product.Status.ShouldBe(ProductStatus.Unpublished);
        product.IsVisible.ShouldBeFalse();
        product.DomainEvents.ShouldContain(e => e is CatalogEvents.ProductUnpublished);
    }

    [Fact]
    public void Restoring_an_unpublished_product_goes_back_through_review()
    {
        var product = ReadyToSubmit();
        product.SubmitForReview(Now);
        product.Approve(Now);
        product.Unpublish("policy_violation", Now);

        product.Republish(Now).IsSuccess.ShouldBeTrue();

        product.Status.ShouldBe(ProductStatus.PendingReview,
            "whatever caused the takedown must be confirmed resolved before it goes live again");
    }
}
