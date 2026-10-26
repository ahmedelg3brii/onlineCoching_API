using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using CoachDB.Application.Abstractions.Context;
using CoachDB.Application.Abstractions.Events;
using CoachDB.Infrastructure.Common.Persistence;
using CoachDB.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoachDB.Infrastructure.Common.Outbox;

/// <summary>Which module DbContexts have an outbox to drain. Filled by <c>AddModuleDbContext</c>.</summary>
public sealed class OutboxRegistry
{
    private readonly List<Type> _contextTypes = [];

    public IReadOnlyList<Type> ContextTypes => _contextTypes;

    internal void Add(Type contextType)
    {
        if (!_contextTypes.Contains(contextType))
            _contextTypes.Add(contextType);
    }
}

/// <summary>
/// Delivers outbox messages to every <see cref="IDomainEventHandler{TEvent}"/> for their type, every few
/// seconds. Rows are locked with <c>FOR UPDATE SKIP LOCKED</c> so several API instances can run it.
/// Each handler runs in its own scope with the message's tenant set (so RLS still applies), is recorded
/// in the inbox on success, and is skipped on re-delivery. Failures retry up to
/// <see cref="OutboxMessage.MaxAttempts"/> times, then stay as dead letters with their error.
/// </summary>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    OutboxRegistry registry,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<Type, MethodInfo> HandleMethods = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var contextType in registry.ContextTypes)
            {
                try
                {
                    await DispatchBatchAsync(contextType, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogBatchFailed(logger, contextType.Name, ex);
                }
            }
        }
    }

    public async Task DispatchBatchAsync(Type contextType, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(contextType);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var messages = await context.OutboxMessages
            .FromSqlRaw(LockBatchSql(context.Schema))
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await DeliverAsync(context, message, cancellationToken);
                message.ProcessedAt = clock.GetUtcNow();
                message.Error = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.Error = ex.ToString();
                LogDeliveryFailed(logger, message.Id, message.Attempts, ex);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task DeliverAsync(ModuleDbContext context, OutboxMessage message, CancellationToken cancellationToken)
    {
        var eventType = Type.GetType(message.Type, throwOnError: true)!;
        var domainEvent = (IDomainEvent)JsonSerializer.Deserialize(message.Content, eventType, OutboxMessage.JsonOptions)!;
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handle = HandleMethods.GetOrAdd(handlerType, t => t.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!);

        var done = await context.InboxMessages
            .Where(i => i.MessageId == message.Id)
            .Select(i => i.Handler)
            .ToListAsync(cancellationToken);

        await using var handlerScope = scopeFactory.CreateAsyncScope();
        if (message.TenantId is { } tenantId)
            handlerScope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId, slug: string.Empty);

        foreach (var handler in handlerScope.ServiceProvider.GetServices(handlerType))
        {
            var name = handler!.GetType().FullName!;
            if (done.Contains(name))
                continue;

            await (Task)handle.Invoke(handler, [domainEvent, cancellationToken])!;
            context.InboxMessages.Add(new InboxMessage { MessageId = message.Id, Handler = name, ProcessedAt = clock.GetUtcNow() });
        }
    }

    // A schema name cannot be a SQL parameter. It comes from code (ModuleDbContext.Schema) and must be a
    // plain lower-case identifier, so it is safe to inline.
    internal static string LockBatchSql(string schema)
    {
        if (schema.Length == 0 || !schema.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            throw new ArgumentException($"Invalid schema name '{schema}'.", nameof(schema));

        return $"""
            SELECT * FROM "{schema}".outbox_messages
            WHERE processed_at IS NULL AND attempts < {OutboxMessage.MaxAttempts}
            ORDER BY occurred_at
            LIMIT {BatchSize}
            FOR UPDATE SKIP LOCKED
            """;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox batch failed for {Context}")]
    private static partial void LogBatchFailed(ILogger logger, string context, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} failed (attempt {Attempt})")]
    private static partial void LogDeliveryFailed(ILogger logger, Guid messageId, int attempt, Exception exception);
}
