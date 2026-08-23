using Lifestyle.Infrastructure.Persistence;
using Lifestyle.Infrastructure.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lifestyle.IntegrationTests;

/// <summary>
/// Hosts the real API against a real PostgreSQL. No in-memory provider and no mocked repositories:
/// the things most likely to break — citext indexes, jsonb round-trips, the filtered unique
/// indexes, snake_case mapping — only break against a real database.
/// <para>
/// Uses Testcontainers when a container runtime is available, and falls back to
/// <c>ConnectionStrings__Default</c> when one is not (CI service containers, or a developer with a
/// local server). If neither is present the fixture reports why rather than failing obscurely.
/// </para>
/// </summary>
public sealed class LifestyleApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = string.Empty;

    /// <summary>Set when neither a container runtime nor a configured server is reachable.</summary>
    public string? UnavailableReason { get; private set; }

    // Explicit interface implementation: xUnit's IAsyncLifetime uses Task, while
    // WebApplicationFactory.DisposeAsync returns ValueTask. Implementing both implicitly clashes.
    async Task IAsyncLifetime.InitializeAsync() => await StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    private async Task StartAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__Default");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            _connectionString = configured;
        }
        else
        {
            try
            {
                _container = new PostgreSqlBuilder()
                    .WithImage("postgres:17-alpine")
                    .WithDatabase("lifestyle_test")
                    .WithUsername("lifestyle")
                    .WithPassword("lifestyle")
                    .Build();

                await _container.StartAsync();
                _connectionString = _container.GetConnectionString();
            }
            catch (Exception ex)
            {
                UnavailableReason =
                    "No container runtime and no ConnectionStrings__Default. Start Docker, or set "
                    + $"ConnectionStrings__Default to a disposable PostgreSQL. ({ex.GetType().Name})";
                return;
            }
        }

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Migrate rather than EnsureCreated: this also proves the migration itself is valid,
        // which is the thing that actually runs in production.
        await db.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(Services);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["ConnectionStrings:Redis"] = string.Empty,
                ["Platform:Country"] = "MY",
                ["Platform:Currency"] = "MYR",
                ["Platform:RootDomain"] = "lifestyle.test",
                ["Platform:TimeZone"] = "Asia/Kuala_Lumpur",
                ["Identity:JwtIssuer"] = "https://api.lifestyle.test",
                ["Identity:JwtAudienceBase"] = "lifestyle",
                ["Identity:JwtSigningKey"] = "integration-test-signing-key-at-least-32-bytes-long",
                ["Catalog:Currency"] = "MYR",
                ["Storage:Provider"] = "local",
                ["Storage:LocalRoot"] = Path.Combine(Path.GetTempPath(), "lifestyle-tests"),
                // Seeded so the admin flows have a real account to act as.
                ["Seed:SuperAdmin:Email"] = TestUsers.AdminEmail,
                ["Seed:SuperAdmin:Password"] = TestUsers.AdminPassword,
                ["Seed:SuperAdmin:FullName"] = "Test Administrator"
            });
        });

        builder.ConfigureServices(services =>
        {
            // The outbox dispatcher polls on a timer; in tests we drive it explicitly so the
            // assertions are deterministic rather than racing a background loop.
            var dispatcher = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType?.Name == "OutboxDispatcher");

            if (dispatcher is not null) services.Remove(dispatcher);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public static class TestUsers
{
    public const string AdminEmail = "admin@lifestyle.test";
    public const string AdminPassword = "IntegrationTestAdmin!42";
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<LifestyleApiFactory>
{
    public const string Name = "api";
}
