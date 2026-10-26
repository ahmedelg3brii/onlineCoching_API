using CoachDB.SharedKernel.Domain;

namespace CoachDB.Application.Abstractions.Events;

/// <summary>
/// Reacts to an event after the transaction that raised it has committed. Delivered from the outbox, so
/// it runs at least once: each handler is recorded in the inbox and skipped on re-delivery.
/// Adding a reaction = a new handler class; the code that raised the event never changes.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}
