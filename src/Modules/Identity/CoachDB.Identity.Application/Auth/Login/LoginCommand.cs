using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Domain;
using CoachDB.Identity.Domain.Users;
using CoachDB.SharedKernel.Results;
using FluentValidation;

namespace CoachDB.Identity.Application.Auth.Login;

/// <summary>Signs in with email + password and starts a new session.</summary>
public sealed record LoginCommand(string Email, string Password) : ICommand<AuthResult>;

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("required");
        RuleFor(x => x.Password).NotEmpty().WithMessage("required");
    }
}

internal sealed class LoginHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    SessionIssuer sessions,
    TimeProvider clock) : ICommandHandler<LoginCommand, AuthResult>
{
    public async ValueTask<Result<AuthResult>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);
        if (email.IsFailure)
            return IdentityErrors.InvalidCredentials;

        var user = await users.GetByEmailAsync(email.Value, cancellationToken);
        if (user?.PasswordHash is null)
            return IdentityErrors.InvalidCredentials;

        var check = passwordHasher.Verify(user.PasswordHash, command.Password);
        if (check == PasswordCheck.Failed)
            return IdentityErrors.InvalidCredentials;

        if (check == PasswordCheck.SuccessRehashNeeded)
            user.UpgradePasswordHash(passwordHasher.Hash(command.Password));

        user.RecordLogin(clock.GetUtcNow());
        return sessions.StartSession(user);
    }
}
