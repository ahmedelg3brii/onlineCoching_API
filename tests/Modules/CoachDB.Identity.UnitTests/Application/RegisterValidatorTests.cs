using CoachDB.Identity.Application.Auth.Register;

namespace CoachDB.Identity.UnitTests.Application;

public sealed class RegisterValidatorTests
{
    private static readonly RegisterValidator Validator = new();

    private static readonly RegisterCommand Valid = new("coach@example.com", "Secret123!", "Ahmed", "en");

    private static Dictionary<string, string[]> Errors(RegisterCommand command) =>
        Validator.Validate(command).Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

    [Fact]
    public void Valid_command_has_no_errors() => Errors(Valid).ShouldBeEmpty();

    [Fact]
    public void Null_locale_is_allowed() => Errors(Valid with { Locale = null }).ShouldBeEmpty();

    [Fact]
    public void Empty_fields_are_required()
    {
        var errors = Errors(new RegisterCommand("", "", "", null));

        errors["Email"].ShouldContain("required");
        errors["Password"].ShouldContain("required");
        errors["FullName"].ShouldContain("required");
    }

    [Fact]
    public void Short_password_is_too_short() =>
        Errors(Valid with { Password = new string('x', RegisterValidator.MinPasswordLength - 1) })["Password"].ShouldBe(["too_short"]);

    [Fact]
    public void Long_values_are_too_long()
    {
        Errors(Valid with { Password = new string('x', RegisterValidator.MaxPasswordLength + 1) })["Password"].ShouldBe(["too_long"]);
        Errors(Valid with { FullName = new string('x', 201) })["FullName"].ShouldBe(["too_long"]);
        Errors(Valid with { Email = new string('a', 250) + "@example.com" })["Email"].ShouldContain("too_long");
    }

    [Fact]
    public void Bad_email_is_invalid_email() =>
        Errors(Valid with { Email = "not-an-email" })["Email"].ShouldBe(["invalid_email"]);

    [Fact]
    public void Unknown_locale_is_unsupported() =>
        Errors(Valid with { Locale = "fr" })["Locale"].ShouldBe(["unsupported_locale"]);
}
