using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Domain;
using CoachDB.Identity.Domain.Users;
using CoachDB.SharedKernel.Results;
using FluentValidation;

namespace CoachDB.Identity.Application.Auth.Register;

/// <summary>Creates an email + password account and signs it in.</summary>
public sealed record RegisterCommand(string Email, string Password, string FullName, string? Locale) : ICommand<AuthResult>;

internal sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("required")
            .MaximumLength(Domain.Users.Email.MaxLength).WithMessage("too_long")
            .EmailAddress().WithMessage("invalid_email");
        RuleFor(x => x.Password).NotEmpty().WithMessage("required")
            .MinimumLength(MinPasswordLength).WithMessage("too_short")
            .MaximumLength(MaxPasswordLength).WithMessage("too_long");
        RuleFor(x => x.FullName).NotEmpty().WithMessage("required")
            .MaximumLength(200).WithMessage("too_long");
        RuleFor(x => x.Locale).Must(l => l is null || User.SupportedLocales.Contains(l)).WithMessage("unsupported_locale");
    }
}

internal sealed class RegisterHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    SessionIssuer sessions,
    TimeProvider clock) : ICommandHandler<RegisterCommand, AuthResult>
{
    public async ValueTask<Result<AuthResult>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);
        if (email.IsFailure)
            return email.Error;

        if (await users.EmailExistsAsync(email.Value, cancellationToken))
            return IdentityErrors.EmailTaken;

        var user = User.Register(email.Value, command.FullName, command.Locale, passwordHasher.Hash(command.Password), clock.GetUtcNow());
        users.Add(user);

        return sessions.StartSession(user);
    }
}
