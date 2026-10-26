using CoachDB.SharedKernel.Results;

namespace CoachDB.Application.Abstractions.Messaging;

// Every use case returns a Result, so expected failures never become exceptions.
// Commands change state (they run inside a unit of work); queries only read.

public interface ICommand : Mediator.ICommand<Result>;

public interface ICommand<TResponse> : Mediator.ICommand<Result<TResponse>>;

public interface IQuery<TResponse> : Mediator.IQuery<Result<TResponse>>;

public interface ICommandHandler<in TCommand> : Mediator.ICommandHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : Mediator.ICommandHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : Mediator.IQueryHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
