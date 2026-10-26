using CoachDB.SharedKernel.Domain;

namespace CoachDB.Tenants.Contracts.Events;

/// <summary>A coach created a business; they are its owner. Published through the outbox.</summary>
public sealed record TenantCreated(Guid TenantId, string Slug, string Name, Guid OwnerUserId, DateTimeOffset TrialEndsAt) : DomainEvent;

/// <summary>A platform admin suspended the business: its members and clients can no longer use it.</summary>
public sealed record TenantSuspended(Guid TenantId) : DomainEvent;

public sealed record TenantReactivated(Guid TenantId) : DomainEvent;
