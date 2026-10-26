using CoachDB.Tenants.Domain.Tenants;
using FluentValidation;

namespace CoachDB.Tenants.Application.Validation;

/// <summary>Field rules shared by the Tenants validators. Messages are error codes; null means "not sent" and passes.</summary>
internal static class TenantRules
{
    public const int MaxUrlLength = 500;
    public const int MaxShortTextLength = 200;

    public static IRuleBuilderOptions<T, string?> Currency<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(c => c is null || (c.Length == 3 && c.All(char.IsAsciiLetter))).WithMessage("invalid_currency");

    /// <summary>An IANA zone such as <c>Africa/Cairo</c> (or <c>UTC</c>), known to the server's time zone database.</summary>
    public static IRuleBuilderOptions<T, string?> Timezone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(tz => tz is null || IsIanaTimezone(tz)).WithMessage("invalid_timezone");

    public static IRuleBuilderOptions<T, string?> Locale<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(l => l is null || Tenant.SupportedLocales.Contains(l)).WithMessage("unsupported_locale");

    public static IRuleBuilderOptions<T, string?> HttpsUrl<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(u => u is null || (u.Length <= MaxUrlLength && Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("invalid_url");

    public static IRuleBuilderOptions<T, string?> HexColor<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(c => c is null || (c.Length == 7 && c[0] == '#' && c[1..].All(char.IsAsciiHexDigit))).WithMessage("invalid_color");

    public static IRuleBuilderOptions<T, string?> ShortText<T>(this IRuleBuilder<T, string?> rule, int max = MaxShortTextLength) =>
        rule.Must(t => t is null || t.Length <= max).WithMessage("too_long");

    /// <summary>Adds the slug's own error code (<c>slug_invalid</c>, <c>slug_reserved</c>) to the field.</summary>
    public static IRuleBuilderOptionsConditions<T, string> ValidSlug<T>(this IRuleBuilder<T, string> rule) =>
        rule.Custom((value, context) =>
        {
            var slug = Slug.Create(value);
            if (slug.IsFailure)
                context.AddFailure(slug.Error.Code);
        });

    private static bool IsIanaTimezone(string tz) =>
        (tz == "UTC" || tz.Contains('/', StringComparison.Ordinal))
        && TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _);
}
