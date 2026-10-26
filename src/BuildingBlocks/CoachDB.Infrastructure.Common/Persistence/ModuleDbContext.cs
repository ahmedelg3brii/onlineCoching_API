using System.Linq.Expressions;
using System.Text.Json;
using CoachDB.Application.Abstractions.Context;
using CoachDB.Infrastructure.Common.Outbox;
using CoachDB.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoachDB.Infrastructure.Common.Persistence;

/// <summary>Services every module DbContext needs, bundled so module constructors stay one line.</summary>
public sealed class ModuleDbContextDependencies(ITenantContext tenant, DbContextScope scope)
{
    public ITenantContext Tenant { get; } = tenant;
    public DbContextScope Scope { get; } = scope;
}

/// <summary>
/// Base for each module's DbContext. Gives it its own Postgres schema, outbox and inbox tables, and two
/// global query filters: the current tenant (for <see cref="ITenantOwned"/>) and not-deleted
/// (for <see cref="ISoftDeletable"/>). Postgres RLS is the second, independent layer.
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    public const string TenantFilter = "tenant";
    public const string SoftDeleteFilter = "soft_delete";

    private readonly ITenantContext _tenant;

    protected ModuleDbContext(DbContextOptions options, ModuleDbContextDependencies dependencies) : base(options)
    {
        _tenant = dependencies.Tenant;
        dependencies.Scope.Track(this);
    }

    /// <summary>Postgres schema owned by the module, e.g. <c>payments</c>.</summary>
    public abstract string Schema { get; }

    /// <summary>Read by the tenant filter on every query. No tenant → <see cref="Guid.Empty"/> → no rows.</summary>
    protected Guid CurrentTenantId => _tenant.HasTenant ? _tenant.TenantId : Guid.Empty;

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null && !t.IsOwned()))
        {
            var clr = entityType.ClrType;
            if (typeof(ITenantOwned).IsAssignableFrom(clr))
                entityType.SetQueryFilter(TenantFilter, TenantFilterFor(clr));
            if (typeof(ISoftDeletable).IsAssignableFrom(clr))
                entityType.SetQueryFilter(SoftDeleteFilter, SoftDeleteFilterFor(clr));
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        WriteDomainEventsToOutbox();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        WriteDomainEventsToOutbox();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>Events become outbox rows in the same transaction as the state change: both commit or neither does.</summary>
    private void WriteDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<IHasDomainEvents>()
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    Type = domainEvent.GetType().AssemblyQualifiedName!,
                    Content = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), OutboxMessage.JsonOptions),
                    OccurredAt = domainEvent.OccurredAt,
                    TenantId = _tenant.HasTenant ? _tenant.TenantId : null,
                });
            }

            aggregate.ClearDomainEvents();
        }
    }

    // e => e.TenantId == this.CurrentTenantId   (EF parameterises the context property per query)
    private LambdaExpression TenantFilterFor(Type clr)
    {
        var e = Expression.Parameter(clr, "e");
        var tenantId = Expression.Property(Expression.Convert(e, typeof(ITenantOwned)), nameof(ITenantOwned.TenantId));
        var current = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
        return Expression.Lambda(Expression.Equal(tenantId, current), e);
    }

    // e => e.DeletedAt == null
    private static LambdaExpression SoftDeleteFilterFor(Type clr)
    {
        var e = Expression.Parameter(clr, "e");
        var deletedAt = Expression.Property(Expression.Convert(e, typeof(ISoftDeletable)), nameof(ISoftDeletable.DeletedAt));
        return Expression.Lambda(Expression.Equal(deletedAt, Expression.Constant(null, typeof(DateTimeOffset?))), e);
    }
}
