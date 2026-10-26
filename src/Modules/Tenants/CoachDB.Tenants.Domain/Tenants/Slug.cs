using CoachDB.SharedKernel.Results;

namespace CoachDB.Tenants.Domain.Tenants;

/// <summary>
/// The business's address: <c>fitwithahmed</c> in <c>fitwithahmed.coachdb.app</c> and <c>/public/fitwithahmed</c>.
/// A valid DNS label (3-63 chars, a-z, 0-9, single hyphens inside), stored lower-case, never one of <see cref="Reserved"/>.
/// </summary>
public sealed record Slug
{
    public const int MinLength = 3;
    public const int MaxLength = 63;

    /// <summary>Platform subdomains and routes a business can't take.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "www", "api", "app", "admin", "administrator", "auth", "login", "logout", "signup", "register",
        "account", "accounts", "billing", "dashboard", "help", "support", "status", "docs", "blog", "mail",
        "email", "smtp", "static", "assets", "cdn", "media", "files", "public", "private", "root", "system",
        "coachdb", "staging", "dev", "test",
    };

    private Slug(string value) => Value = value;

    public string Value { get; }

    public static Result<Slug> Create(string? value)
    {
        var slug = value?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!IsWellFormed(slug))
            return TenantsErrors.SlugInvalid;
        if (Reserved.Contains(slug))
            return TenantsErrors.SlugReserved;

        return new Slug(slug);
    }

    private static bool IsWellFormed(string slug) =>
        slug.Length is >= MinLength and <= MaxLength
        && slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
        && slug[0] != '-' && slug[^1] != '-'
        && !slug.Contains("--", StringComparison.Ordinal); // "xn--" punycode prefixes and look-alikes

    public override string ToString() => Value;
}
