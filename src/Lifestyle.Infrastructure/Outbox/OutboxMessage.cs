using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lifestyle.Infrastructure.Persistence;

/// <summary>
/// One integration event awaiting delivery. Written in the same transaction as the state change
/// that produced it (FRD §21.1) — that is what makes "order paid → send email + index it + post to
/// the ledger" reliable without distributed transactions.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public Guid Id { get; private set; }

    /// <summary>Assembly-qualified type name, used to deserialise on the way out.</summary>
    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create(string type, string payload, DateTimeOffset occurredAt) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = type,
        Payload = payload,
        OccurredAt = occurredAt,
        NextAttemptAt = occurredAt
    };

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LastError = null;
        NextAttemptAt = null;
    }

    /// <summary>
    /// Records a failure and schedules a retry with exponential backoff, capped at ten minutes.
    /// After <paramref name="maxAttempts"/> the message stops being picked up and is left for the
    /// dead-letter alert to surface — silent infinite retry hides real breakage.
    /// </summary>
    public void MarkFailed(string error, DateTimeOffset now, int maxAttempts)
    {
        AttemptCount++;
        LastError = error.Length > 4000 ? error[..4000] : error;

        NextAttemptAt = AttemptCount >= maxAttempts
            ? null
            : now.AddSeconds(Math.Min(600, Math.Pow(2, AttemptCount)));
    }

    public bool IsDeadLettered(int maxAttempts) => ProcessedAt is null && AttemptCount >= maxAttempts;
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages", "platform");
        b.HasKey(m => m.Id);

        b.Property(m => m.Type).HasMaxLength(500).IsRequired();
        b.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(m => m.LastError).HasMaxLength(4000);

        // The dispatcher's only query: unprocessed and due, oldest first.
        b.HasIndex(m => m.NextAttemptAt).HasFilter("processed_at IS NULL");
    }
}
