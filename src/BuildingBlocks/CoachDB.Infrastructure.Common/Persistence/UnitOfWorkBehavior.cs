using CoachDB.SharedKernel.Results;
using Mediator;

namespace CoachDB.Infrastructure.Common.Persistence;

/// <summary>
/// Pipeline step 4 (commands only): if the handler succeeded, saves the module DbContext it changed —
/// entity changes and outbox rows commit together. A failed Result saves nothing.
/// A command may change only one module; reacting in another module is done with an event.
/// </summary>
public sealed class UnitOfWorkBehavior<TMessage, TResponse>(DbContextScope scope)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IBaseCommand
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(message, cancellationToken);

        if (response is Result { IsFailure: true })
            return response;

        var changed = scope.Contexts.Where(c => c.ChangeTracker.HasChanges()).ToList();
        if (changed.Count > 1)
        {
            throw new InvalidOperationException(
                $"{typeof(TMessage).Name} changed {changed.Count} modules ({string.Join(", ", changed.Select(c => c.Schema))}). " +
                "Change one module per command and react in others through domain events.");
        }

        if (changed.Count == 1)
            await changed[0].SaveChangesAsync(cancellationToken);

        return response;
    }
}
