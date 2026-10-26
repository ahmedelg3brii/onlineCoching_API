using CoachDB.Application.Abstractions.Behaviors;
using CoachDB.Application.Abstractions.Context;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.SharedKernel.Results;
using CoachDB.Tenants.Application.Abstractions;
using CoachDB.Tenants.Application.Validation;
using CoachDB.Tenants.Domain;
using CoachDB.Tenants.Domain.Billing;
using CoachDB.Tenants.Domain.Members;
using CoachDB.Tenants.Domain.Tenants;
using FluentValidation;

namespace CoachDB.Tenants.Application.Tenants.Create;

/// <summary>
/// A signed-in user creates a coaching business: it starts a free trial of the configured plan and the user becomes
/// its owner. Runs outside any tenant; from then on the request runs as the new tenant.
/// </summary>
public sealed record CreateTenantCommand(
    string Name,
    string Slug,
    string? DefaultCurrency,
    string? Timezone,
    string? DefaultLocale) : ICommand<TenantResponse>;

internal sealed class CreateTenantValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("required")
            .MaximumLength(Tenant.MaxNameLength).WithMessage("too_long");
        RuleFor(x => x.Slug).NotEmpty().WithMessage("required").DependentRules(() =>
            RuleFor(x => x.Slug).ValidSlug());
        RuleFor(x => x.DefaultCurrency).Currency();
        RuleFor(x => x.Timezone).Timezone();
        RuleFor(x => x.DefaultLocale).Locale();
    }
}

internal sealed class CreateTenantHandler(
    IUserContext user,
    TenantContext tenantContext,
    ITenantRepository tenants,
    ITenantMemberRepository members,
    IPlatformPlanRepository plans,
    ITenantPlanSubscriptionRepository subscriptions,
    TenantsOptions settings,
    TimeProvider clock) : ICommandHandler<CreateTenantCommand, TenantResponse>
{
    public async ValueTask<Result<TenantResponse>> Handle(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated)
            return AuthorizationBehavior<CreateTenantCommand, Result<TenantResponse>>.AuthRequired;

        var slug = Slug.Create(command.Slug);
        if (slug.IsFailure)
            return slug.Error;

        if (await members.CountOwnedByUserAsync(user.UserId, cancellationToken) >= settings.MaxOwnedTenantsPerUser)
            return TenantsErrors.OwnedTenantLimitReached;

        if (await tenants.SlugExistsAsync(slug.Value.Value, cancellationToken))
            return TenantsErrors.SlugTaken;

        var trialPlan = await plans.GetByCodeAsync(settings.TrialPlanCode, cancellationToken);
        if (trialPlan is not { IsActive: true })
            return TenantsErrors.TrialPlanUnavailable;

        var now = clock.GetUtcNow();
        var tenant = Tenant.StartTrial(command.Name, slug.Value, user.UserId, trialPlan.Id, TimeSpan.FromDays(settings.TrialDays), now,
            command.DefaultCurrency, command.Timezone, command.DefaultLocale);
        tenants.Add(tenant);

        // The rest of this request runs as the new tenant: the member and subscription rows are stamped with it,
        // and RLS lets them be inserted.
        tenantContext.Set(tenant.Id, tenant.Slug);
        members.Add(TenantMember.CreateOwner(tenant.Id, user.UserId, now));
        subscriptions.Add(TenantPlanSubscription.StartTrial(tenant.Id, trialPlan.Id, now, tenant.TrialEndsAt!.Value));

        return TenantResponse.From(tenant);
    }
}
