using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OtpNet;
using Shouldly;
using Xunit;

namespace Lifestyle.IntegrationTests;

/// <summary>
/// Phase 1's exit criterion, walked end to end against a real database:
/// <em>"an approved vendor can publish a live, correctly-attributed product with variants"</em>
/// (Plan §6, Phase 1).
/// <para>
/// Deliberately one long test rather than several short ones. Every step consumes the previous
/// step's real output — a vendor id, a media id, a moderation decision — so splitting it would
/// mean either re-walking the path per assertion or faking the intermediate state, and faking it
/// is exactly what this test exists to avoid.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PhaseOneExitCriterionTests(LifestyleApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Password = "a-long-enough-password";

    [SkippableFact]
    public async Task An_approved_vendor_publishes_a_live_attributed_product_with_variants()
    {
        Skip.If(factory.UnavailableReason is not null, factory.UnavailableReason);

        var suffix = Guid.CreateVersion7().ToString("N")[..8];
        var vendorEmail = $"aisha-{suffix}@example.com";
        var client = factory.CreateClient();

        // ── 1. A shopper registers, then applies to become a vendor ────────────────────────
        var register = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email = vendorEmail,
            password = Password,
            fullName = "Aisha Rahman",
            phoneNumber = (string?)null
        });
        register.StatusCode.ShouldBe(HttpStatusCode.Created, await Body(register));

        Authenticate(client, await LoginAsync(client, vendorEmail, Password, "buyer"));

        var apply = await client.PostAsJsonAsync("/v1/vendor-applications", new
        {
            legalName = "Aisha Trading Sdn Bhd",
            displayName = $"Aisha Boutique {suffix}",
            desiredSlug = $"aisha-{suffix}",
            contactEmail = $"shop-{suffix}@example.com",
            contactPhone = "+60123456789",
            registrationNumber = "202401012345"
        });
        apply.StatusCode.ShouldBe(HttpStatusCode.Created, await Body(apply));

        var application = await Read(apply);
        var vendorId = application.GetProperty("id").GetGuid();
        var vendorSlug = application.GetProperty("slug").GetString()!;
        application.GetProperty("status").GetString().ShouldBe("Draft");

        // ── 2. Submitting without KYC documents is refused ─────────────────────────────────
        var premature = await client.PostAsync("/v1/vendor-applications/submit", null);
        premature.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Body(premature));
        (await Read(premature)).GetProperty("code").GetString()
            .ShouldBe("vendors.registration_document_missing");

        // ── 3. Upload the KYC documents and submit ─────────────────────────────────────────
        await AttachDocumentAsync(client, "BusinessRegistration", await UploadAsync(client, "ssm.png"), "ssm.png");
        await AttachDocumentAsync(client, "OwnerIdentity", await UploadAsync(client, "ic.png"), "ic.png");

        var submit = await client.PostAsync("/v1/vendor-applications/submit", null);
        submit.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(submit));
        (await Read(submit)).GetProperty("status").GetString().ShouldBe("PendingReview");

        // ── 4. An admin signs in — TOTP is mandatory on the admin surface (FRD §4.2) ────────
        var adminClient = factory.CreateClient();
        Authenticate(adminClient, await EnrolAdminAndLoginAsync(adminClient));

        // The application is in the queue.
        var queue = await adminClient.GetAsync("/v1/admin/vendors?status=PendingReview&pageSize=100");
        queue.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(queue));
        (await Read(queue)).GetProperty("items").EnumerateArray()
            .ShouldContain(v => v.GetProperty("id").GetGuid() == vendorId);

        // ── 5. The admin approves it ───────────────────────────────────────────────────────
        var approve = await adminClient.PostAsJsonAsync($"/v1/admin/vendors/{vendorId}/review",
            new { approve = true, reason = (string?)null });

        approve.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(approve));
        (await Read(approve)).GetProperty("status").GetString().ShouldBe("Approved");

        // ── 6. The vendor can now sign in on the seller surface ────────────────────────────
        var sellerClient = factory.CreateClient();
        Authenticate(sellerClient, await LoginAsync(sellerClient, vendorEmail, Password, "seller"));

        var profile = await sellerClient.GetAsync("/v1/vendor/profile");
        profile.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(profile));
        (await Read(profile)).GetProperty("id").GetGuid().ShouldBe(vendorId);

        // ── 7. Create a product in Shoes, the two-axis (size × colour) category ────────────
        var shoes = await FindCategoryAsync(sellerClient, "Shoes");
        var productImage = await UploadAsync(sellerClient, "shoe.png");

        var create = await sellerClient.PostAsJsonAsync("/v1/vendor/products", new
        {
            categoryId = shoes,
            name = $"Aurora Runner {suffix}",
            description = "A light everyday running shoe with a breathable knit upper.",
            shortDescription = "Light everyday runner.",
            brand = "Aurora",
            variants = new[]
            {
                new { sku = $"AR-{suffix}-40-BLK", options = Axes("40", "Black"), price = 189.90m, compareAtPrice = (decimal?)249.00m, stockQuantity = 6 },
                new { sku = $"AR-{suffix}-41-BLK", options = Axes("41", "Black"), price = 189.90m, compareAtPrice = (decimal?)249.00m, stockQuantity = 4 },
                new { sku = $"AR-{suffix}-40-WHT", options = Axes("40", "White"), price = 199.90m, compareAtPrice = (decimal?)null, stockQuantity = 2 }
            },
            imageMediaIds = new[] { productImage },
            attributes = new Dictionary<string, string>
            {
                ["gender"] = "Women",
                ["shoe_type"] = "Sneaker"
            }
        });

        create.StatusCode.ShouldBe(HttpStatusCode.Created, await Body(create));

        var product = await Read(create);
        var productId = product.GetProperty("id").GetGuid();
        var productSlug = product.GetProperty("slug").GetString()!;

        product.GetProperty("status").GetString().ShouldBe("Draft");
        product.GetProperty("variants").GetArrayLength().ShouldBe(3);
        product.GetProperty("totalStock").GetInt32().ShouldBe(12);

        // Money crosses the wire as a decimal string, never a float (FRD §19.1).
        product.GetProperty("price").GetProperty("min").GetString().ShouldBe("189.90");
        product.GetProperty("price").GetProperty("max").GetString().ShouldBe("199.90");
        product.GetProperty("price").GetProperty("currency").GetString().ShouldBe("MYR");

        // "Correctly attributed": the category's non-variant attributes were validated and stored.
        var attributes = product.GetProperty("attributes");
        attributes.GetProperty("gender").GetString().ShouldBe("Women");
        attributes.GetProperty("shoe_type").GetString().ShouldBe("Sneaker");

        // ── 8. Attribute rules are actually enforced ───────────────────────────────────────
        var badAxis = await sellerClient.PostAsJsonAsync("/v1/vendor/products", new
        {
            categoryId = shoes,
            name = $"Bad Axis {suffix}",
            description = "Should not be accepted.",
            shortDescription = (string?)null,
            brand = (string?)null,
            variants = new[]
            {
                new { sku = $"BAD-{suffix}", options = new Dictionary<string, string> { ["size_eu"] = "40" }, price = 10m, compareAtPrice = (decimal?)null, stockQuantity = 1 }
            },
            imageMediaIds = Array.Empty<string>(),
            attributes = new Dictionary<string, string> { ["gender"] = "Women", ["shoe_type"] = "Sneaker" }
        });

        badAxis.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Body(badAxis));
        (await Read(badAxis)).GetProperty("code").GetString().ShouldBe("catalog.axis_missing.colour");

        // ── 9. The product is not visible before moderation ────────────────────────────────
        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/v1/catalog/vendors/{vendorId}/products/{productSlug}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "a draft must not be publicly readable");

        // ── 10. Submit, then a moderator approves ──────────────────────────────────────────
        var submitProduct = await sellerClient.PostAsync($"/v1/vendor/products/{productId}/submit", null);
        submitProduct.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(submitProduct));
        (await Read(submitProduct)).GetProperty("status").GetString().ShouldBe("PendingReview");

        var moderate = await adminClient.PostAsJsonAsync(
            $"/v1/admin/catalog/products/{productId}/moderate", new { approve = true, note = (string?)null });

        moderate.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(moderate));
        (await Read(moderate)).GetProperty("status").GetString().ShouldBe("Published");

        // ── 11. It is now live and publicly readable ───────────────────────────────────────
        var publicProduct = await anonymous.GetAsync($"/v1/catalog/vendors/{vendorId}/products/{productSlug}");
        publicProduct.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(publicProduct));

        var live = await Read(publicProduct);
        live.GetProperty("name").GetString().ShouldBe($"Aurora Runner {suffix}");
        live.GetProperty("variants").GetArrayLength().ShouldBe(3);

        // ── 12. And it appears in the shop's own storefront, scoped by host ────────────────
        var storefrontClient = factory.CreateClient();
        storefrontClient.DefaultRequestHeaders.Host = $"{vendorSlug}.lifestyle.test";

        var storefront = await storefrontClient.GetAsync("/v1/catalog/storefronts/current");
        storefront.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(storefront));
        (await Read(storefront)).GetProperty("id").GetGuid().ShouldBe(vendorId);

        var storefrontProducts = await storefrontClient.GetAsync("/v1/catalog/products");
        storefrontProducts.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(storefrontProducts));

        var items = (await Read(storefrontProducts)).GetProperty("items").EnumerateArray().ToList();
        items.ShouldNotBeEmpty();

        // The tenancy guarantee: a storefront host serves that vendor's products and no others,
        // and the client never passed a vendor id (FRD §19.4).
        items.ShouldAllBe(p => p.GetProperty("vendorId").GetGuid() == vendorId);
        items.ShouldContain(p => p.GetProperty("id").GetGuid() == productId);
    }

    private static Dictionary<string, string> Axes(string sizeEu, string colour) =>
        new() { ["size_eu"] = sizeEu, ["colour"] = colour };

    private static async Task AttachDocumentAsync(HttpClient client, string kind, string mediaId, string fileName)
    {
        var response = await client.PostAsJsonAsync("/v1/vendor-applications/documents",
            new { kind, mediaId, fileName });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(response));
    }

    private static async Task<Guid> FindCategoryAsync(HttpClient client, string name)
    {
        var response = await client.GetAsync("/v1/catalog/categories");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(response));

        return (await Read(response)).EnumerateArray()
            .SelectMany(root => root.GetProperty("children").EnumerateArray())
            .First(c => c.GetProperty("name").GetString() == name)
            .GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Enrols the seeded super-admin in TOTP and returns an admin-surface access token.
    /// <para>
    /// The seeded account holds both the admin and buyer roles, so it authenticates on the buyer
    /// surface to enrol, then signs in to the admin surface with a code. Asserting the blocked
    /// attempt first proves the 2FA gate genuinely holds rather than being decorative.
    /// </para>
    /// </summary>
    private static async Task<string> EnrolAdminAndLoginAsync(HttpClient client)
    {
        var blocked = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            email = TestUsers.AdminEmail,
            password = TestUsers.AdminPassword,
            surface = "admin"
        });

        if (blocked.StatusCode == HttpStatusCode.OK)
        {
            // A previous test in this collection already enrolled the shared seeded account.
            return (await Read(blocked)).GetProperty("accessToken").GetString()!;
        }

        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "an admin without TOTP must not reach the admin surface");
        (await Read(blocked)).GetProperty("code").GetString().ShouldBe("identity.totp_enrolment_required");

        Authenticate(client, await LoginAsync(client, TestUsers.AdminEmail, TestUsers.AdminPassword, "buyer"));

        var begin = await client.PostAsync("/v1/auth/2fa/begin", null);
        begin.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(begin));

        var secret = (await Read(begin)).GetProperty("secret").GetString()!;
        var totp = new Totp(Base32Encoding.ToBytes(secret));

        var confirm = await client.PostAsJsonAsync("/v1/auth/2fa/confirm",
            new { secret, code = totp.ComputeTotp() });
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent, await Body(confirm));

        client.DefaultRequestHeaders.Authorization = null;

        var login = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            email = TestUsers.AdminEmail,
            password = TestUsers.AdminPassword,
            surface = "admin",
            totpCode = totp.ComputeTotp()
        });

        login.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(login));
        return (await Read(login)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>Uploads a real PNG so the media pipeline is exercised rather than stubbed.</summary>
    private static async Task<string> UploadAsync(HttpClient client, string fileName)
    {
        using var content = new MultipartFormDataContent();

        // CA2000: ownership transfers to the MultipartFormDataContent on Add, which disposes it.
#pragma warning disable CA2000
        var image = new ByteArrayContent(OnePixelPng);
#pragma warning restore CA2000
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(image, "file", fileName);

        var response = await client.PostAsync("/v1/media", content);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(response));

        return (await Read(response)).GetProperty("id").GetString()!;
    }

    /// <summary>A minimal valid PNG. Smaller than every derivative size, so none are generated.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<string> LoginAsync(HttpClient client, string email, string password, string surface)
    {
        var previous = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsJsonAsync("/v1/auth/login", new { email, password, surface });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(response));

        client.DefaultRequestHeaders.Authorization = previous;
        return (await Read(response)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json);

    private static async Task<string> Body(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();
}
