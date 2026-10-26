using System.Data.Common;
using CoachDB.Application.Abstractions.Context;
using CoachDB.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoachDB.Infrastructure.Common.Persistence;

/// <summary>
/// Sets <c>app.tenant_id</c> and <c>app.user_id</c> on every connection the API opens; RLS policies compare
/// against them (see <see cref="RlsSql"/>). No tenant → empty → every tenant policy denies. <c>app.user_id</c> lets a
/// user read their own rows across tenants (e.g. "which businesses am I a member of") before a tenant is chosen.
/// The settings are session-level, and Npgsql resets session state when a connection returns to the pool,
/// so they never leak to the next request. EF opens a connection per operation, so a tenant set mid-request
/// (tenant resolution, or creating a new tenant) applies from the next query on.
/// </summary>
public sealed class TenantRlsConnectionInterceptor(ITenantContext tenant, IUserContext? user = null) : DbConnectionInterceptor
{
    private const string Sql = "SELECT set_config('app.tenant_id', @tenant_id, false), set_config('app.user_id', @user_id, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = Sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant_id";
        parameter.Value = tenant.HasTenant ? tenant.TenantId.ToString() : string.Empty;
        command.Parameters.Add(parameter);

        var userParameter = command.CreateParameter();
        userParameter.ParameterName = "user_id";
        userParameter.Value = user is { IsAuthenticated: true } ? user.UserId.ToString() : string.Empty;
        command.Parameters.Add(userParameter);
        return command;
    }
}

/// <summary>
/// Stamps <see cref="ITenantOwned.TenantId"/> on new rows and refuses to write a row of another tenant.
/// A mismatch is a bug, so it throws rather than returning a Result.
/// </summary>
public sealed class TenantStampingInterceptor(ITenantContext tenant) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (!tenant.HasTenant)
                throw new InvalidOperationException($"Cannot write {entry.Metadata.ClrType.Name} without a tenant.");

            var property = entry.Property(nameof(ITenantOwned.TenantId));
            var current = (Guid)property.CurrentValue!;

            if (entry.State == EntityState.Added && current == Guid.Empty)
                property.CurrentValue = tenant.TenantId;
            else if (current != tenant.TenantId)
                throw new InvalidOperationException($"Cross-tenant write blocked on {entry.Metadata.ClrType.Name}.");
        }
    }
}

/// <summary>Sets <c>created_at</c> on insert and <c>updated_at</c> on every save for <see cref="IHasTimestamps"/>.</summary>
public sealed class TimestampsInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
            return;

        var now = clock.GetUtcNow();
        foreach (EntityEntry<IHasTimestamps> entry in context.ChangeTracker.Entries<IHasTimestamps>())
        {
            if (entry.State == EntityState.Added)
            {
                // Keep a value the domain already set, so the response built before save matches the row.
                SetIfUnset(entry.Property(nameof(IHasTimestamps.CreatedAt)), now);
                SetIfUnset(entry.Property(nameof(IHasTimestamps.UpdatedAt)), now);
            }
            else if (entry.State == EntityState.Modified)
            {
                // Same rule on update: a domain method that set UpdatedAt already returned it in the response.
                var updatedAt = entry.Property(nameof(IHasTimestamps.UpdatedAt));
                if (!updatedAt.IsModified)
                    updatedAt.CurrentValue = now;
            }
        }
    }

    private static void SetIfUnset(PropertyEntry property, DateTimeOffset now)
    {
        if (property.CurrentValue is not DateTimeOffset value || value == default)
            property.CurrentValue = now;
    }
}
