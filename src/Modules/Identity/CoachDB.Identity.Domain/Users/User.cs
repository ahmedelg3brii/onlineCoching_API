using CoachDB.Identity.Contracts.Events;
using CoachDB.SharedKernel.Domain;

namespace CoachDB.Identity.Domain.Users;

/// <summary>A person with one login. Roles live per tenant (Team, Clients), not on the user.</summary>
public sealed class User : AggregateRoot<UserId>, IHasTimestamps, ISoftDeletable
{
    public static readonly string[] SupportedLocales = ["ar", "en"];
    public const string DefaultLocale = "ar";

    private User() { } // EF Core

    private User(UserId id, Email email, string fullName, string locale, string passwordHash, DateTimeOffset now) : base(id)
    {
        Email = email;
        FullName = fullName;
        Locale = locale;
        PasswordHash = passwordHash;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Email Email { get; private set; } = null!;
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    public string? Phone { get; private set; }
    public string? PasswordHash { get; private set; } // null for Google-only accounts
    public string FullName { get; private set; } = string.Empty;
    public string? AvatarUrl { get; private set; }
    public string Locale { get; private set; } = DefaultLocale;
    public bool IsPlatformAdmin { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Creates an email + password account. The caller has already checked the email is free.</summary>
    public static User Register(Email email, string fullName, string? locale, string passwordHash, DateTimeOffset now)
    {
        var user = new User(
            UserId.New(),
            email,
            fullName.Trim(),
            locale is not null && SupportedLocales.Contains(locale) ? locale : DefaultLocale,
            passwordHash,
            now);

        user.Raise(new UserRegistered(user.Id.Value, email.Value, user.FullName, user.Locale));
        return user;
    }

    public void RecordLogin(DateTimeOffset at) => LastLoginAt = at;

    /// <summary>Used when the stored hash was made with older settings and has just been re-hashed.</summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;
}
