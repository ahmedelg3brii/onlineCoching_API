namespace CoachDB.SharedKernel.Domain;

/// <summary>Something that happened inside a module. Named in the past tense, e.g. <c>CheckInSubmitted</c>.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
