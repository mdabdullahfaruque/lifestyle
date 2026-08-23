using System.Text.Json;
using Lifestyle.Infrastructure.Persistence;
using Lifestyle.SharedKernel.Abstractions;
using Lifestyle.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lifestyle.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollSeconds { get; init; } = 5;
    public int BatchSize { get; init; } = 50;
    public int MaxAttempts { get; init; } = 8;
}

/// <summary>
/// Polls the outbox and invokes every registered handler for each message.
/// <para>
/// Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so running several application instances is
/// safe: each picks up a disjoint batch instead of fighting over the same rows.
/// </para>
/// <para>
/// Delivery is at-least-once. A handler that runs twice must not do damage twice — that is the
/// handler's responsibility, and it is why they are all written to be idempotent.
/// </para>
/// </summary>
internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.PollSeconds);
        using var timer = new PeriodicTimer(interval);

        logger.LogInformation("Outbox dispatcher started; polling every {Seconds}s.", _options.PollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop must survive anything a batch throws, or one poison message stops all
                // event delivery until the next deployment.
                logger.LogError(ex, "Outbox batch failed. Continuing.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) break;
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var messages = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM platform.outbox_messages
                WHERE processed_at IS NULL
                  AND next_attempt_at IS NOT NULL
                  AND next_attempt_at <= {now}
                ORDER BY occurred_at
                LIMIT {_options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        if (messages.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return;
        }

        foreach (var message in messages)
        {
            try
            {
                await DispatchAsync(scope.ServiceProvider, message, ct);
                message.MarkProcessed(clock.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed(ex.ToString(), clock.UtcNow, _options.MaxAttempts);

                if (message.IsDeadLettered(_options.MaxAttempts))
                {
                    logger.LogError(ex,
                        "Outbox message {MessageId} of type {Type} dead-lettered after {Attempts} attempts.",
                        message.Id, message.Type, message.AttemptCount);
                }
                else
                {
                    logger.LogWarning(ex,
                        "Outbox message {MessageId} failed (attempt {Attempt}); will retry.",
                        message.Id, message.AttemptCount);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task DispatchAsync(IServiceProvider services, OutboxMessage message, CancellationToken ct)
    {
        var eventType = Type.GetType(message.Type)
            ?? throw new InvalidOperationException($"Unknown integration event type '{message.Type}'.");

        var integrationEvent = JsonSerializer.Deserialize(message.Payload, eventType, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialise outbox message {message.Id}.");

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var handlers = services.GetServices(handlerType).Where(h => h is not null).ToList();

        // No subscriber is a normal state, not an error: an event exists whether or not anything
        // currently listens for it.
        foreach (var handler in handlers)
        {
            var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.Handle))!;
            await (Task)method.Invoke(handler, [integrationEvent, ct])!;
        }
    }
}
