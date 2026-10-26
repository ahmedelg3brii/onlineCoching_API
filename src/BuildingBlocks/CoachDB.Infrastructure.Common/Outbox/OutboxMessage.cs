using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoachDB.Infrastructure.Common.Outbox;

/// <summary>A domain event waiting to be delivered. Written in the same transaction as the change that raised it.</summary>
public sealed class OutboxMessage
{
    public const int MaxAttempts = 10;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
    public required string Type { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
}

/// <summary>Records that a handler already processed a message, so a re-delivery has no effect.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }
    public required string Handler { get; init; }
    public DateTimeOffset ProcessedAt { get; init; }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).IsRequired();
        builder.Property(m => m.Content).HasColumnType("jsonb").IsRequired();

        // Dispatcher scans unprocessed rows in order.
        builder.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL");
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(m => new { m.MessageId, m.Handler });
    }
}
