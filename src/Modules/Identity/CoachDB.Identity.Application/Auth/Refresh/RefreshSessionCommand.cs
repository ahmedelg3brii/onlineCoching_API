using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Domain;
using CoachDB.Identity.Domain.Sessions;
using CoachDB.SharedKernel.Results;

namespace CoachDB.Identity.Application.Auth.Refresh;

/// <summary>
/// Exchanges a refresh token for a new access token and a new refresh token (rotation).
/// Re-using an already-rotated token revokes the whole session.
/// </summary>
public sealed record RefreshSessionCommand(string? RefreshToken) : ICommand<AuthResult>;

internal sealed class RefreshSessionHandler(
    IRefreshTokenRepository tokens,
    IRefreshTokenGenerator generator,
    IUserRepository users,
    SessionIssuer sessions,
    TimeProvider clock) : ICommandHandler<RefreshSessionCommand, AuthResult>
{
    public async ValueTask<Result<AuthResult>> Handle(RefreshSessionCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return IdentityErrors.RefreshTokenInvalid;

        var now = clock.GetUtcNow();
        var current = await tokens.GetByHashAsync(generator.Hash(command.RefreshToken), cancellationToken);
        if (current is null || current.IsExpired(now))
            return IdentityErrors.RefreshTokenInvalid;

        if (current.IsRevoked)
        {
            if (!current.WasRotated)
                return IdentityErrors.RefreshTokenInvalid; // logged out, or already ended by reuse handling

            // A rotated token came back: someone else holds this session. End it for everyone.
            await tokens.RevokeFamilyAsync(current.FamilyId, RefreshToken.ReuseDetectedReason, now, cancellationToken);
            return IdentityErrors.RefreshTokenReused;
        }

        var user = await users.GetByIdAsync(current.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.RefreshTokenInvalid;

        return sessions.Continue(user, current);
    }
}
