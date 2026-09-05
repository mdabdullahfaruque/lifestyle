using System.Security.Cryptography;
using Lifestyle.Infrastructure.Persistence;
using Lifestyle.Infrastructure.Seeding;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Identity.Domain;
using Lifestyle.Modules.Identity.Internal;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OtpNet;
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
        await CreateRunScopedAdminsAsync(scope.ServiceProvider, db);
    }

    /// <summary>
    /// Creates this run's own admin accounts, rather than reusing the seeded one.
    /// <para>
    /// Enrolling in TOTP is a one-way change, so a test that asserts "an admin without TOTP is
    /// blocked" passes on a fresh database and then fails on every re-run against the same one.
    /// Minting a fresh pair per run — one enrolled with a secret we know, one deliberately not —
    /// makes the suite repeatable against a long-lived database, which is what CI and a developer's
    /// local server both are.
    /// </para>
    /// </summary>
    private async Task CreateRunScopedAdminsAsync(IServiceProvider services, AppDbContext db)
    {
        var passwords = services.GetRequiredService<IPasswordService>();
        var clock = services.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        var runId = UniqueSuffix();
        AdminEmail = $"admin-{runId}@lifestyle.test";
        UnenrolledAdminEmail = $"admin-noTotp-{runId}@lifestyle.test";
        AdminTotpSecret = Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));

        var enrolled = User.Register(AdminEmail, passwords.Hash(AdminPassword), "Enrolled Admin", null, now);
        enrolled.VerifyEmail(now);
        enrolled.EnableTwoFactor(AdminTotpSecret, now);
        enrolled.AssignRole(SystemRoles.SuperAdminId, null, now);
        enrolled.AssignRole(SystemRoles.BuyerId, null, now);

        var unenrolled = User.Register(UnenrolledAdminEmail, passwords.Hash(AdminPassword), "Unenrolled Admin", null, now);
        unenrolled.VerifyEmail(now);
        unenrolled.AssignRole(SystemRoles.SuperAdminId, null, now);
        unenrolled.AssignRole(SystemRoles.BuyerId, null, now);

        db.Users.AddRange(enrolled, unenrolled);
        await db.SaveChangesAsync();
    }

    public const string AdminPassword = "IntegrationTestAdmin!42";

    /// <summary>
    /// A suffix that makes test data unique across runs against a long-lived database.
    /// <para>
    /// Deliberately <em>not</em> the leading characters of a UUIDv7. Those encode the millisecond
    /// timestamp, so the first 32 bits only change about once a minute — two runs a few seconds
    /// apart produce the same "unique" value and the second collides on the email unique index.
    /// Random bits are what this needs, so it uses a v4.
    /// </para>
    /// </summary>
    public static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..10];

    /// <summary>An admin enrolled in TOTP, with a secret the tests can compute codes from.</summary>
    public string AdminEmail { get; private set; } = string.Empty;

    public string AdminTotpSecret { get; private set; } = string.Empty;

    /// <summary>An admin deliberately left without TOTP, to assert the enrolment gate holds.</summary>
    public string UnenrolledAdminEmail { get; private set; } = string.Empty;

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
                // The suite logs in dozens of times from one IP; production defaults would 429 it.
                ["RateLimits:AuthPerMinute"] = "1000",
                ["RateLimits:UploadsPerMinute"] = "1000",
                // Seeded so the admin flows have a real account to act as.
                ["Seed:SuperAdmin:Email"] = "seeded-admin@lifestyle.test",
                ["Seed:SuperAdmin:Password"] = AdminPassword,
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

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<LifestyleApiFactory>
{
    public const string Name = "api";
}
