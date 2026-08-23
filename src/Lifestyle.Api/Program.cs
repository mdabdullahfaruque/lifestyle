using System.Globalization;
using Lifestyle.Api.Composition;
using Lifestyle.Infrastructure.Seeding;
using Serilog;

// Bootstrap logger: captures failures that happen before configuration is even read.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName());

    builder.AddLifestyle();

    var app = builder.Build();

    // ── CLI modes ──
    // Migrations and seeding are explicit deploy steps, never a start-up side effect
    // (docs/04 §4.4). The process runs the command and exits.
    var command = args.FirstOrDefault();

    if (string.Equals(command, "migrate", StringComparison.OrdinalIgnoreCase))
    {
        await DatabaseSeeder.MigrateAsync(app.Services);
        return 0;
    }

    if (string.Equals(command, "seed", StringComparison.OrdinalIgnoreCase))
    {
        await DatabaseSeeder.SeedAsync(app.Services);
        return 0;
    }

    app.UseLifestyle();

    if (app.Environment.IsDevelopment())
        app.UseOpenApiDocument();

    await app.RunAsync();
    return 0;
}
catch (HostAbortedException)
{
    // How `dotnet ef` stops the host once it has the service provider. Expected, not a failure.
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Lifestyle API terminated unexpectedly.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the API in tests.</summary>
public partial class Program;
