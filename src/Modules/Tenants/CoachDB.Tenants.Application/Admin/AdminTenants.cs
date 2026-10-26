using System.Globalization;
using CoachDB.Application.Abstractions.Authorization;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Application.Abstractions.Paging;
using CoachDB.SharedKernel.Results;
using CoachDB.Tenants.Application.Abstractions;
using CoachDB.Tenants.Application.Tenants;
using CoachDB.Tenants.Domain;
using CoachDB.Tenants.Domain.Tenants;

namespace CoachDB.Tenants.Application.Admin;

// Platform admin: see and suspend businesses (/admin/tenants).

/// <param name="Search">Matches the start of the slug or any part of the name, case-insensitive.</param>
[RequiresPlatformAdmin]
public sealed record ListTenantsQuery(TenantStatus? Status, string? Search, PageRequest Page) : IQuery<CursorPage<TenantResponse>>;

internal sealed class ListTenantsHandler(ITenantRepository tenants) : IQueryHandler<ListTenantsQuery, CursorPage<TenantResponse>>
{
    public async ValueTask<Result<CursorPage<TenantResponse>>> Handle(ListTenantsQuery query, CancellationToken cancellationToken)
    {
        var rows = await tenants.ListAsync(query.Status, query.Search?.Trim(), query.Page, cancellationToken);
        return CursorPage<TenantResponse>.From(rows, query.Page, TenantResponse.From, TenantPageCursor.For);
    }
}

/// <summary>
/// Keyset position for the admin list, which is sorted newest first, then by slug (slugs are unique, so the order is
/// total). The sort key holds both: <c>"{createdAt ticks}|{slug}"</c>.
/// </summary>
public static class TenantPageCursor
{
    public static Cursor For(Tenant tenant) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{tenant.CreatedAt.UtcTicks}|{tenant.Slug}"), tenant.Id);

    public static bool TryRead(Cursor cursor, out DateTimeOffset createdAt, out string slug)
    {
        createdAt = default;
        slug = string.Empty;
        var parts = cursor.SortKey.Split('|', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
            return false;

        createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
        slug = parts[1];
        return true;
    }
}

[RequiresPlatformAdmin]
public sealed record GetTenantQuery(Guid TenantId) : IQuery<TenantResponse>;

internal sealed class GetTenantHandler(ITenantRepository tenants) : IQueryHandler<GetTenantQuery, TenantResponse>
{
    public async ValueTask<Result<TenantResponse>> Handle(GetTenantQuery query, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(query.TenantId, cancellationToken);
        return tenant is null ? TenantsErrors.TenantNotFound : TenantResponse.From(tenant);
    }
}

/// <summary>Blocks a business: tenant resolution answers <c>403 tenant_suspended</c> for its members. Idempotent.</summary>
[RequiresPlatformAdmin]
public sealed record SuspendTenantCommand(Guid TenantId) : ICommand<TenantResponse>;

/// <summary>Lifts a suspension. Idempotent.</summary>
[RequiresPlatformAdmin]
public sealed record ReactivateTenantCommand(Guid TenantId) : ICommand<TenantResponse>;

internal sealed class SuspendTenantHandler(ITenantRepository tenants, TimeProvider clock) : ICommandHandler<SuspendTenantCommand, TenantResponse>
{
    public async ValueTask<Result<TenantResponse>> Handle(SuspendTenantCommand command, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
            return TenantsErrors.TenantNotFound;

        tenant.Suspend(clock.GetUtcNow());
        return TenantResponse.From(tenant);
    }
}

internal sealed class ReactivateTenantHandler(ITenantRepository tenants, TimeProvider clock) : ICommandHandler<ReactivateTenantCommand, TenantResponse>
{
    public async ValueTask<Result<TenantResponse>> Handle(ReactivateTenantCommand command, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);
        if (tenant is null)
            return TenantsErrors.TenantNotFound;

        tenant.Reactivate(clock.GetUtcNow());
        return TenantResponse.From(tenant);
    }
}
