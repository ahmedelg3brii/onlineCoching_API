using System.Net.Mail;
using CoachDB.SharedKernel.Results;

namespace CoachDB.Identity.Domain.Users;

/// <summary>A normalised (trimmed, lower-case) email address, max 255 characters to match <c>users.email</c>.</summary>
public sealed record Email
{
    public const int MaxLength = 255;

    private Email(string value) => Value = value;

    public string Value { get; }

    public static Result<Email> Create(string? input)
    {
        var value = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            return IdentityErrors.InvalidEmail;

        // MailAddress accepts display-name forms ("Bob <b@x.com>"); require the bare address.
        if (!MailAddress.TryCreate(value, out var parsed) || parsed.Address != value || !parsed.Host.Contains('.'))
            return IdentityErrors.InvalidEmail;

        return new Email(value);
    }

    public override string ToString() => Value;
}
