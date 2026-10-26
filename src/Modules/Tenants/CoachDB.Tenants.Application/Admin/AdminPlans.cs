using CoachDB.Application.Abstractions.Authorization;
using CoachDB.Application.Abstractions.Dtos;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Application.Abstractions.Paging;
using CoachDB.SharedKernel.Domain;
using CoachDB.SharedKernel.Results;
using CoachDB.Tenants.Application.Abstractions;
using CoachDB.Tenants.Application.Plans;
using CoachDB.Tenants.Domain;
using CoachDB.Tenants.Domain.Plans;
using FluentValidation;

namespace CoachDB.Tenants.Application.Admin;

// Platform admin: manage the SaaS plans coaches can buy (/admin/plans).

[RequiresPlatformAdmin]
public sealed record ListAllPlansQuery : IQuery<CursorPage<PlanResponse>>;

internal sealed class ListAllPlansHandler(IPlatformPlanRepository plans) : IQueryHandler<ListAllPlansQuery, CursorPage<PlanResponse>>
{
    public async ValueTask<Result<CursorPage<PlanResponse>>> Handle(ListAllPlansQuery query, CancellationToken cancellationToken)
    {
        var all = await plans.ListAsync(activeOnly: false, cancellationToken);
        return new CursorPage<PlanResponse>(all.Select(PlanResponse.From).ToList(), null, false);
    }
}

[RequiresPlatformAdmin]
public sealed record GetPlanQuery(Guid PlanId) : IQuery<PlanResponse>;

internal sealed class GetPlanHandler(IPlatformPlanRepository plans) : IQueryHandler<GetPlanQuery, PlanResponse>
{
    public async ValueTask<Result<PlanResponse>> Handle(GetPlanQuery query, CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(query.PlanId, cancellationToken);
        return plan is null ? TenantsErrors.PlanNotFound : PlanResponse.From(plan);
    }
}

/// <summary>The fields an admin sets on a plan, shared by create and update.</summary>
public interface IPlanFields
{
    string Name { get; }
    MoneyDto MonthlyPrice { get; }

    /// <summary>null = unlimited.</summary>
    int? MaxClients { get; }

    /// <summary>null = unlimited.</summary>
    int? MaxCoaches { get; }

    IReadOnlyDictionary<string, bool>? Features { get; }
}

[RequiresPlatformAdmin]
public sealed record CreatePlanCommand(
    string Code,
    string Name,
    MoneyDto MonthlyPrice,
    int? MaxClients,
    int? MaxCoaches,
    IReadOnlyDictionary<string, bool>? Features) : ICommand<PlanResponse>, IPlanFields;

/// <summary>Replaces a plan's name, price, limits and features (PUT). The code never changes.</summary>
[RequiresPlatformAdmin]
public sealed record UpdatePlanCommand(
    Guid PlanId,
    string Name,
    MoneyDto MonthlyPrice,
    int? MaxClients,
    int? MaxCoaches,
    IReadOnlyDictionary<string, bool>? Features) : ICommand<PlanResponse>, IPlanFields;

/// <summary>Hides a plan from the price list and from new trials, or shows it again. Tenants already on it keep it.</summary>
[RequiresPlatformAdmin]
public sealed record SetPlanActiveCommand(Guid PlanId, bool IsActive) : ICommand<PlanResponse>;

internal sealed class PlanFieldsValidator : AbstractValidator<IPlanFields>
{
    public PlanFieldsValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("required")
            .MaximumLength(PlatformPlan.MaxNameLength).WithMessage("too_long");
        RuleFor(x => x.MonthlyPrice).NotNull().WithMessage("required");
        When(x => x.MonthlyPrice is not null, () =>
        {
            RuleFor(x => x.MonthlyPrice.Amount).GreaterThanOrEqualTo(0).WithMessage("must_not_be_negative");
            RuleFor(x => x.MonthlyPrice.Currency).Must(c => c is { Length: 3 } && c.All(char.IsAsciiLetter)).WithMessage("invalid_currency");
        });
        RuleFor(x => x.MaxClients).GreaterThan(0).WithMessage("must_be_positive");
        RuleFor(x => x.MaxCoaches).GreaterThan(0).WithMessage("must_be_positive");
        RuleFor(x => x.Features)
            .Must(f => f is null || f.Count <= PlatformPlan.MaxFeatures).WithMessage("too_many")
            .Must(f => f is null || f.Keys.All(PlatformPlan.IsValidFeatureKey)).WithMessage("invalid_feature_key");
    }

    public static Money ToMoney(MoneyDto dto) => Money.Create(dto.Amount, dto.Currency).Value;
}

internal sealed class CreatePlanValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("required")
            .Must(c => PlatformPlan.IsValidCode(c.Trim().ToLowerInvariant())).WithMessage(TenantsErrors.PlanCodeInvalid.Code);
        Include(new PlanFieldsValidator());
    }
}

internal sealed class UpdatePlanValidator : AbstractValidator<UpdatePlanCommand>
{
    public UpdatePlanValidator() => Include(new PlanFieldsValidator());
}

internal sealed class CreatePlanHandler(IPlatformPlanRepository plans, TimeProvider clock) : ICommandHandler<CreatePlanCommand, PlanResponse>
{
    public async ValueTask<Result<PlanResponse>> Handle(CreatePlanCommand command, CancellationToken cancellationToken)
    {
        if (await plans.CodeExistsAsync(command.Code.Trim().ToLowerInvariant(), cancellationToken))
            return TenantsErrors.PlanCodeTaken;

        var plan = PlatformPlan.Create(command.Code, command.Name, PlanFieldsValidator.ToMoney(command.MonthlyPrice),
            command.MaxClients, command.MaxCoaches, command.Features ?? new Dictionary<string, bool>(), clock.GetUtcNow());
        if (plan.IsFailure)
            return plan.Error;

        plans.Add(plan.Value);
        return PlanResponse.From(plan.Value);
    }
}

internal sealed class UpdatePlanHandler(IPlatformPlanRepository plans, TimeProvider clock) : ICommandHandler<UpdatePlanCommand, PlanResponse>
{
    public async ValueTask<Result<PlanResponse>> Handle(UpdatePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(command.PlanId, cancellationToken);
        if (plan is null)
            return TenantsErrors.PlanNotFound;

        plan.Update(command.Name, PlanFieldsValidator.ToMoney(command.MonthlyPrice), command.MaxClients, command.MaxCoaches,
            command.Features ?? new Dictionary<string, bool>(), clock.GetUtcNow());
        return PlanResponse.From(plan);
    }
}

internal sealed class SetPlanActiveHandler(IPlatformPlanRepository plans, TimeProvider clock) : ICommandHandler<SetPlanActiveCommand, PlanResponse>
{
    public async ValueTask<Result<PlanResponse>> Handle(SetPlanActiveCommand command, CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(command.PlanId, cancellationToken);
        if (plan is null)
            return TenantsErrors.PlanNotFound;

        plan.SetActive(command.IsActive, clock.GetUtcNow());
        return PlanResponse.From(plan);
    }
}
