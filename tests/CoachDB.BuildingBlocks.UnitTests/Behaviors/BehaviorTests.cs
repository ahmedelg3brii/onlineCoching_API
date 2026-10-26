using CoachDB.Application.Abstractions.Authorization;
using CoachDB.Application.Abstractions.Behaviors;
using CoachDB.Application.Abstractions.Context;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.SharedKernel.Results;
using FluentValidation;

namespace CoachDB.BuildingBlocks.UnitTests.Behaviors;

public sealed record PriceInput(decimal Amount, string Currency);

public sealed record CreateThingCommand(string Name, PriceInput Price) : ICommand<string>;

[RequiresPermission(Permissions.Packages.Write)]
public sealed record GuardedCommand : ICommand<string>;

[RequiresPermission(Permissions.Packages.Write)]
public sealed record GuardedPlainCommand : ICommand;

public sealed class CreateThingValidator : AbstractValidator<CreateThingCommand>
{
    public CreateThingValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("required");
        RuleFor(x => x.Price.Amount).GreaterThan(0).WithMessage("must_be_positive");
        RuleFor(x => x.Price.Currency).Length(3).WithMessage("invalid_currency");
    }
}

[RequiresTenant]
public sealed record AnyMemberQuery : IQuery<string>;

[RequiresPlatformAdmin]
public sealed record AdminCommand : ICommand<string>;

internal sealed class FakeUser(bool authenticated, params string[] permissions) : IUserContext
{
    public bool IsAuthenticated => authenticated;
    public Guid UserId => authenticated ? Guid.Empty : throw new InvalidOperationException();
    public bool IsPlatformAdmin { get; init; }
    public bool HasPermission(string permission) => permissions.Contains(permission);
}

internal static class Tenants
{
    public static TenantContext Chosen()
    {
        var tenant = new TenantContext();
        tenant.Set(Guid.CreateVersion7(), "fitwithahmed");
        return tenant;
    }

    public static TenantContext None() => new();
}

public sealed class ValidationBehaviorTests
{
    private static ValidationBehavior<CreateThingCommand, Result<string>> Behavior() =>
        new([new CreateThingValidator()]);

    [Fact]
    public async Task Failing_validation_returns_validation_error_with_camel_case_paths()
    {
        var called = false;
        var result = await Behavior().Handle(
            new CreateThingCommand("", new PriceInput(0, "EGPX")),
            (_, _) => { called = true; return ValueTask.FromResult<Result<string>>("ok"); },
            TestContext.Current.CancellationToken);

        called.ShouldBeFalse();
        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Errors["name"].ShouldBe(["required"]);
        error.Errors["price.amount"].ShouldBe(["must_be_positive"]);
        error.Errors["price.currency"].ShouldBe(["invalid_currency"]);
    }

    [Fact]
    public async Task Valid_message_calls_next()
    {
        var result = await Behavior().Handle(
            new CreateThingCommand("Cut 12 weeks", new PriceInput(1500, "EGP")),
            (_, _) => ValueTask.FromResult<Result<string>>("ok"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("ok");
    }

    [Fact]
    public async Task No_validators_calls_next()
    {
        var behavior = new ValidationBehavior<CreateThingCommand, Result<string>>([]);

        var result = await behavior.Handle(
            new CreateThingCommand("", new PriceInput(0, "")),
            (_, _) => ValueTask.FromResult<Result<string>>("ok"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }
}

public sealed class AuthorizationBehaviorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ValueTask<Result<string>> Next<T>(T _, CancellationToken __) =>
        ValueTask.FromResult<Result<string>>("ok");

    private static AuthorizationBehavior<T, Result<string>> Behavior<T>(IUserContext user, TenantContext? tenant = null)
        where T : Mediator.IMessage =>
        new(user, tenant ?? Tenants.Chosen());

    [Fact]
    public async Task Anonymous_user_gets_auth_required()
    {
        var result = await Behavior<GuardedCommand>(new FakeUser(authenticated: false)).Handle(new GuardedCommand(), Next, Ct);

        result.Error.Code.ShouldBe("auth_required");
        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Missing_permission_gets_permission_denied()
    {
        var result = await Behavior<GuardedCommand>(new FakeUser(true, Permissions.Clients.Read)).Handle(new GuardedCommand(), Next, Ct);

        result.Error.Code.ShouldBe("permission_denied");
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Holding_the_permission_calls_next()
    {
        var result = await Behavior<GuardedCommand>(new FakeUser(true, Permissions.Packages.Write)).Handle(new GuardedCommand(), Next, Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Permission_without_a_chosen_tenant_gets_tenant_required()
    {
        var behavior = Behavior<GuardedCommand>(new FakeUser(true, Permissions.Packages.Write), Tenants.None());

        var result = await behavior.Handle(new GuardedCommand(), Next, Ct);

        result.Error.Code.ShouldBe("tenant_required");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task Requires_tenant_passes_any_member_and_rejects_no_tenant()
    {
        (await Behavior<AnyMemberQuery>(new FakeUser(true)).Handle(new AnyMemberQuery(), Next, Ct)).IsSuccess.ShouldBeTrue();

        var none = await Behavior<AnyMemberQuery>(new FakeUser(true), Tenants.None()).Handle(new AnyMemberQuery(), Next, Ct);
        none.Error.Code.ShouldBe("tenant_required");

        var anonymous = await Behavior<AnyMemberQuery>(new FakeUser(false)).Handle(new AnyMemberQuery(), Next, Ct);
        anonymous.Error.Code.ShouldBe("auth_required");
    }

    [Fact]
    public async Task Platform_admin_rule_needs_the_admin_claim_but_no_tenant()
    {
        var notAdmin = await Behavior<AdminCommand>(new FakeUser(true), Tenants.None()).Handle(new AdminCommand(), Next, Ct);
        notAdmin.Error.Code.ShouldBe("permission_denied");

        var admin = await Behavior<AdminCommand>(new FakeUser(true) { IsPlatformAdmin = true }, Tenants.None()).Handle(new AdminCommand(), Next, Ct);
        admin.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Message_without_attribute_calls_next_even_when_anonymous()
    {
        var result = await Behavior<CreateThingCommand>(new FakeUser(authenticated: false), Tenants.None()).Handle(
            new CreateThingCommand("x", new PriceInput(1, "EGP")), Next, Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Non_generic_result_failure_is_supported()
    {
        var behavior = new AuthorizationBehavior<GuardedPlainCommand, Result>(new FakeUser(authenticated: false), Tenants.Chosen());

        var result = await behavior.Handle(new GuardedPlainCommand(), (_, _) => ValueTask.FromResult(Result.Success()), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("auth_required");
    }
}
