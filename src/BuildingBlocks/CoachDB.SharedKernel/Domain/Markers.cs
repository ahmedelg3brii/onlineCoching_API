namespace CoachDB.SharedKernel.Domain;

/// <summary>Row belongs to one tenant. Infrastructure adds a global query filter and stamps <see cref="TenantId"/> on insert.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}

/// <summary>Row is hidden instead of deleted (<c>deleted_at</c>).</summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }
}

/// <summary>Infrastructure sets <c>created_at</c> / <c>updated_at</c> on save.</summary>
public interface IHasTimestamps
{
    DateTimeOffset CreatedAt { get; }
    DateTimeOffset UpdatedAt { get; }
}
