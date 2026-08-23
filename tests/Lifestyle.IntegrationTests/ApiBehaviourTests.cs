using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Lifestyle.IntegrationTests;

/// <summary>
/// Cross-cutting API behaviour: tenancy, audience separation, seeded taxonomy, error shapes.
/// The full Phase 1 walk-through lives in PhaseOneExitCriterionTests.
///
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApiBehaviourTests(LifestyleApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// A storefront host may only ever serve its own vendor's catalogue, whatever the request says
    /// (FRD §19.4). This is the tenancy guarantee the whole storefront model rests on.
    /// </summary>
    [SkippableFact]
    public async Task An_unknown_storefront_host_resolves_to_no_vendor()
    {
        Skip.If(factory.UnavailableReason is not null, factory.UnavailableReason);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "does-not-exist.lifestyle.test";

        var response = await client.GetAsync("/v1/catalog/storefronts/current");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Read(response)).GetProperty("code").GetString()
            .ShouldBe("vendors.not_a_storefront_host");
    }

    [SkippableFact]
    public async Task The_category_tree_is_seeded_with_the_four_launch_categories()
    {
        Skip.If(factory.UnavailableReason is not null, factory.UnavailableReason);

        var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/catalog/categories");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(response));

        var roots = (await Read(response)).EnumerateArray().ToList();
        var leaves = roots.SelectMany(r => r.GetProperty("children").EnumerateArray()).ToList();

        var names = leaves.Select(c => c.GetProperty("name").GetString()).ToList();

        names.ShouldContain("Ladies' Dresses");
        names.ShouldContain("Ladies' Bags");
        names.ShouldContain("Shoes");
        names.ShouldContain("Mobile Accessories");

        // Every leaf must carry an attribute set, or products in it cannot be attributed.
        leaves.ShouldAllBe(c => c.GetProperty("attributeSetId").ValueKind != JsonValueKind.Null);
        leaves.ShouldAllBe(c => c.GetProperty("isLeaf").GetBoolean());
    }

    /// <summary>
    /// Shoes is the two-axis case (size × colour) that exercises variant generation — the seeded
    /// attribute set must actually express that, or Phase 1's exit criterion is untestable.
    /// </summary>
    [SkippableFact]
    public async Task The_shoes_attribute_set_has_two_variant_axes()
    {
        Skip.If(factory.UnavailableReason is not null, factory.UnavailableReason);

        var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/catalog/attribute-sets");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(response));

        var shoes = (await Read(response)).EnumerateArray()
            .First(s => s.GetProperty("code").GetString() == "shoes");

        var axes = shoes.GetProperty("attributes").EnumerateArray()
            .Where(a => a.GetProperty("isVariantAxis").GetBoolean())
            .Select(a => a.GetProperty("code").GetString())
            .ToList();

        axes.ShouldBe(["size_eu", "colour"], ignoreOrder: true);

        // A variant axis must be a Select with a fixed value list, or every typo makes a new SKU.
        shoes.GetProperty("attributes").EnumerateArray()
            .Where(a => a.GetProperty("isVariantAxis").GetBoolean())
            .ShouldAllBe(a => a.GetProperty("dataType").GetString() == "Select"
                              && a.GetProperty("allowedValues").GetArrayLength() > 0);
    }

    [SkippableFact]
    public async Task Anonymous_callers_cannot_reach_the_vendor_surface()
    {
        Skip.If(factory.UnavailableReason is not null, factory.UnavailableReason);

        var client = factory.CreateClient();

        (await client.GetAsync("/v1/vendor/products")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/v1/vendor/profile")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/v1/admin/vendors")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/v1/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A buyer token must be rejected by the seller and admin surfaces even though the same person
    /// holds the account — that is what audience separation buys (FRD §4.2).
    /// </summary>
    [SkippableFact]
    public async Task A_buyer_token_is_rejected_by_the_seller_and_admin_surfaces()
    {
        Skip.If(factory.UnavailableReason is not null, factory.UnavailableReason);

        var client = factory.CreateClient();
        var suffix = Guid.CreateVersion7().ToString("N")[..8];
        var email = $"buyer-{suffix}@example.com";

        await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email,
            password = "a-long-enough-password",
            fullName = "Buyer Person",
            phoneNumber = (string?)null
        });

        var token = await LoginAsync(client, email, "a-long-enough-password", "buyer");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Authenticated as a buyer, /v1/me works...
        (await client.GetAsync("/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // ...but the seller and admin surfaces reject the token outright.
        (await client.GetAsync("/v1/vendor/products")).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
        (await client.GetAsync("/v1/admin/vendors")).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Registering_the_same_email_twice_conflicts()
    {
        Skip.If(factory.UnavailableReason is not null, factory.UnavailableReason);

        var client = factory.CreateClient();
        var email = $"dup-{Guid.CreateVersion7():N}@example.com";

        var body = new { email, password = "a-long-enough-password", fullName = "Dup", phoneNumber = (string?)null };

        (await client.PostAsJsonAsync("/v1/auth/register", body)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("/v1/auth/register", body);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Read(second)).GetProperty("code").GetString().ShouldBe("identity.email_taken");
    }

    [SkippableFact]
    public async Task Validation_failures_return_field_level_problem_details()
    {
        Skip.If(factory.UnavailableReason is not null, factory.UnavailableReason);

        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email = "not-an-email",
            password = "short",
            fullName = "",
            phoneNumber = (string?)null
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await Read(response);
        var errors = problem.GetProperty("errors");

        // camelCase field names on the wire, matching the request shape (FRD §19.1).
        errors.TryGetProperty("email", out _).ShouldBeTrue();
        errors.TryGetProperty("password", out _).ShouldBeTrue();
        errors.TryGetProperty("fullName", out _).ShouldBeTrue();
    }

    [SkippableFact]
    public async Task Health_check_reports_healthy()
    {
        Skip.If(factory.UnavailableReason is not null, factory.UnavailableReason);

        var response = await factory.CreateClient().GetAsync("/v1/internal/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Every response echoes a correlation id, generated when the caller omits one.</summary>
    [SkippableFact]
    public async Task Responses_carry_a_correlation_id()
    {
        Skip.If(factory.UnavailableReason is not null, factory.UnavailableReason);

        var client = factory.CreateClient();

        var generated = await client.GetAsync("/v1/catalog/categories");
        generated.Headers.TryGetValues("X-Correlation-Id", out var values).ShouldBeTrue();
        values!.First().ShouldNotBeNullOrWhiteSpace();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/catalog/categories");
        request.Headers.Add("X-Correlation-Id", "my-trace-123");

        var echoed = await client.SendAsync(request);
        echoed.Headers.GetValues("X-Correlation-Id").First().ShouldBe("my-trace-123");
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password, string surface)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/login", new { email, password, surface });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Body(response));

        return (await Read(response)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json);

    private static async Task<string> Body(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();
}
