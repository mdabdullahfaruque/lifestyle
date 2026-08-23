using Lifestyle.Modules.Catalog.Domain;
using Shouldly;
using Xunit;

namespace Lifestyle.UnitTests.Catalog;

public sealed class ProductVariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    private static Product NewProduct() =>
        Product.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Runner", "runner",
            "A shoe.", null, null, "MYR", Now);

    private static Dictionary<string, string> Axes(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// {colour:Red, size:M} and {size:M, colour:Red} describe the same shoe. Without a canonical
    /// signature the same variant could be created twice and the catalogue would be wrong.
    /// </summary>
    [Fact]
    public void Axis_signature_ignores_key_order_and_casing()
    {
        var a = ProductVariant.BuildSignature(Axes(("colour", "Red"), ("size", "M")));
        var b = ProductVariant.BuildSignature(Axes(("size", "m"), ("Colour", "RED")));

        a.ShouldBe(b);
    }

    [Fact]
    public void Rejects_a_duplicate_option_combination()
    {
        var product = NewProduct();
        product.AddVariant("SKU-RED-M", Axes(("colour", "Red"), ("size", "M")), 100m, null, 5, Now);

        var result = product.AddVariant("SKU-DIFFERENT", Axes(("size", "M"), ("colour", "red")), 120m, null, 3, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.variant_duplicate");
    }

    [Fact]
    public void Rejects_a_duplicate_sku()
    {
        var product = NewProduct();
        product.AddVariant("SKU-1", Axes(("colour", "Red")), 100m, null, 5, Now);

        var result = product.AddVariant("sku-1", Axes(("colour", "Blue")), 100m, null, 5, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.sku_duplicate");
    }

    [Fact]
    public void Rejects_a_non_positive_price()
    {
        var product = NewProduct();

        product.AddVariant("SKU-1", Axes(("colour", "Red")), 0m, null, 5, Now)
            .Error.Code.ShouldBe("catalog.price_invalid");

        product.AddVariant("SKU-2", Axes(("colour", "Blue")), -5m, null, 5, Now)
            .Error.Code.ShouldBe("catalog.price_invalid");
    }

    /// <summary>A "was" price below the selling price is dark-pattern pricing, not a discount.</summary>
    [Fact]
    public void Rejects_a_compare_at_price_that_is_not_higher()
    {
        var product = NewProduct();

        var result = product.AddVariant("SKU-1", Axes(("colour", "Red")), 100m, 90m, 5, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.compare_price_invalid");
    }

    [Fact]
    public void Price_range_and_total_stock_track_the_active_variants()
    {
        var product = NewProduct();
        product.AddVariant("S", Axes(("size", "S")), 80m, null, 3, Now);
        product.AddVariant("M", Axes(("size", "M")), 100m, null, 5, Now);
        product.AddVariant("L", Axes(("size", "L")), 120m, null, 2, Now);

        product.MinPrice.ShouldBe(80m);
        product.MaxPrice.ShouldBe(120m);
        product.TotalStock.ShouldBe(10);
    }

    [Fact]
    public void A_deactivated_variant_leaves_the_price_range_and_stock()
    {
        var product = NewProduct();
        product.AddVariant("S", Axes(("size", "S")), 80m, null, 3, Now);
        product.AddVariant("M", Axes(("size", "M")), 100m, null, 5, Now);

        var cheap = product.Variants.First(v => v.Sku == "S");
        product.UpdateVariant(cheap.Id, 80m, null, 3, isActive: false, Now);

        product.MinPrice.ShouldBe(100m, "an inactive variant is not for sale, so it cannot set the 'from' price");
        product.TotalStock.ShouldBe(5);
    }

    [Fact]
    public void A_product_must_keep_at_least_one_variant()
    {
        var product = NewProduct();
        product.AddVariant("ONLY", Axes(("size", "M")), 100m, null, 5, Now);

        var result = product.RemoveVariant(product.Variants.First().Id, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.last_variant");
    }

    [Fact]
    public void Stock_adjustments_apply_as_deltas()
    {
        var product = NewProduct();
        product.AddVariant("SKU-1", Axes(("size", "M")), 100m, null, 10, Now);
        var variantId = product.Variants.First().Id;

        product.AdjustStock(variantId, -3, Now).IsSuccess.ShouldBeTrue();
        product.Variants.First().StockQuantity.ShouldBe(7);

        product.AdjustStock(variantId, +5, Now).IsSuccess.ShouldBeTrue();
        product.Variants.First().StockQuantity.ShouldBe(12);
        product.TotalStock.ShouldBe(12);
    }

    /// <summary>
    /// v1 stock is vendor-maintained and advisory (Plan §6.2). Silently clamping at zero would hide
    /// the vendor's counting error; refusing surfaces it.
    /// </summary>
    [Fact]
    public void Stock_cannot_be_driven_negative()
    {
        var product = NewProduct();
        product.AddVariant("SKU-1", Axes(("size", "M")), 100m, null, 2, Now);
        var variantId = product.Variants.First().Id;

        var result = product.AdjustStock(variantId, -5, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.stock_negative");
        product.Variants.First().StockQuantity.ShouldBe(2, "a rejected adjustment must change nothing");
    }

    [Fact]
    public void Images_keep_a_contiguous_display_order()
    {
        var product = NewProduct();
        product.AddImage("a", null, Now);
        product.AddImage("b", null, Now);
        product.AddImage("c", null, Now);

        product.ReorderImages(["c", "a", "b"], Now);

        product.Images.OrderBy(i => i.Position).Select(i => i.MediaId).ShouldBe(["c", "a", "b"]);

        product.RemoveImage("a", Now);
        product.Images.Select(i => i.Position).OrderBy(p => p).ShouldBe([0, 1]);
    }
}
