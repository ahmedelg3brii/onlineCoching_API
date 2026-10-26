using CoachDB.SharedKernel.Domain;

namespace CoachDB.Identity.Contracts.Events;

/// <summary>Published after a new account is created. Other modules subscribe with <c>IDomainEventHandler&lt;UserRegistered&gt;</c>.</summary>
public sealed record UserRegistered(Guid UserId, string Email, string FullName, string Locale) : DomainEvent;
