using CoachDB.SharedKernel.Domain;

namespace CoachDB.Tenants.Domain.Members;

/// <summary>Roles a team member can hold in a business. Clients are not members; they live in the Clients module.</summary>
public enum MemberRole
{
    Owner,
    Coach,
    Assistant,
}

public enum MemberStatus
{
    Active,
    Suspended,
}

/// <summary>
/// A user's membership of one business (<c>tenant_members</c>). Tenant resolution reads it on every tenant-scoped
/// request, which is why it lives in this module; the Team module adds invites and changes members through it.
/// </summary>
public sealed class TenantMember : Entity<Guid>, ITenantOwned, IHasTimestamps
{
    private TenantMember() { } // EF Core

    private TenantMember(Guid tenantId, Guid userId, MemberRole role, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        Status = MemberStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public MemberRole Role { get; private set; }
    public MemberStatus Status { get; private set; }
    public string? Bio { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status == MemberStatus.Active;

    /// <summary>Owners and coaches count towards the plan's coach limit.</summary>
    public bool CountsAsCoach => Role is MemberRole.Owner or MemberRole.Coach;

    public static TenantMember CreateOwner(Guid tenantId, Guid userId, DateTimeOffset now) =>
        new(tenantId, userId, MemberRole.Owner, now);
}
