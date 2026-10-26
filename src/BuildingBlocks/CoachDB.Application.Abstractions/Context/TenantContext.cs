namespace CoachDB.Application.Abstractions.Context;

/// <summary>Scoped holder set once per request by tenant resolution (or per iteration by a cross-tenant job).</summary>
public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public bool HasTenant => _tenantId.HasValue;

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException("No tenant has been resolved for this request.");

    public string? TenantSlug { get; private set; }

    public void Set(Guid tenantId, string slug)
    {
        if (_tenantId.HasValue && _tenantId != tenantId)
            throw new InvalidOperationException("The tenant cannot change within one scope.");

        _tenantId = tenantId;
        TenantSlug = slug;
    }
}
