namespace CoachDB.SharedKernel.Domain;

/// <summary>Base class for entities. State changes only through methods that enforce the entity's rules.</summary>
public abstract class Entity<TId>
    where TId : struct
{
    protected Entity(TId id) => Id = id;

    // Needed by EF Core materialisation.
    protected Entity() { }

    public TId Id { get; protected init; }
}

/// <summary>Non-generic view used by infrastructure to collect events from any aggregate on save.</summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

/// <summary>An entity that is the consistency boundary for its children and raises domain events.</summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id) { }

    protected AggregateRoot() { }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
