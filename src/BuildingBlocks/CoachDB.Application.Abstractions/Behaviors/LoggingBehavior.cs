using System.Diagnostics;
using CoachDB.SharedKernel.Results;
using Mediator;
using Microsoft.Extensions.Logging;

namespace CoachDB.Application.Abstractions.Behaviors;

/// <summary>Pipeline step 1: logs every use case with its duration and, on failure, its error code.</summary>
public sealed partial class LoggingBehavior<TMessage, TResponse>(ILogger<LoggingBehavior<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var name = typeof(TMessage).Name;
        var start = Stopwatch.GetTimestamp();

        var response = await next(message, cancellationToken);

        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (response is Result { IsFailure: true } failed)
            LogFailed(logger, name, failed.Error.Code, elapsedMs);
        else
            LogHandled(logger, name, elapsedMs);

        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {UseCase} in {ElapsedMs:0.0} ms")]
    private static partial void LogHandled(ILogger logger, string useCase, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{UseCase} failed with {ErrorCode} in {ElapsedMs:0.0} ms")]
    private static partial void LogFailed(ILogger logger, string useCase, string errorCode, double elapsedMs);
}
