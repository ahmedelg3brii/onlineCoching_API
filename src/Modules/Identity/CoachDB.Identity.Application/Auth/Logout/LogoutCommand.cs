using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Domain.Sessions;
using CoachDB.SharedKernel.Results;

namespace CoachDB.Identity.Application.Auth.Logout;

/// <summary>Ends the session the refresh token belongs to. Always succeeds, so logout is idempotent.</summary>
public sealed record LogoutCommand(string? RefreshToken) : ICommand;

internal sealed class LogoutHandler(
    IRefreshTokenRepository tokens,
    IRefreshTokenGenerator generator,
    TimeProvider clock) : ICommandHandler<LogoutCommand>
{
    public async ValueTask<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return Result.Success();

        var current = await tokens.GetByHashAsync(generator.Hash(command.RefreshToken), cancellationToken);
        if (current is not null)
            await tokens.RevokeFamilyAsync(current.FamilyId, RefreshToken.LogoutReason, clock.GetUtcNow(), cancellationToken);

        return Result.Success();
    }
}
