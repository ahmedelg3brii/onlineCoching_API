using CoachDB.Application.Abstractions.Authorization;
using CoachDB.Application.Abstractions.Context;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.SharedKernel.Results;
using CoachDB.Tenants.Application.Abstractions;
using CoachDB.Tenants.Application.Validation;
using CoachDB.Tenants.Domain;
using CoachDB.Tenants.Domain.Tenants;
using FluentValidation;

namespace CoachDB.Tenants.Application.Tenants.Update;

/// <summary>
/// <c>PATCH /tenant</c>: a field left out (null) is not changed. <see cref="Branding"/> and <see cref="Settings"/>, when
/// sent, replace the whole object (a field inside them that is null or empty is cleared). The slug can't be changed:
/// it is the business's address.
/// </summary>
[RequiresPermission(Permissions.Tenant.Manage)]
public sealed record UpdateTenantCommand(
    string? Name,
    string? DefaultCurrency,
    string? Timezone,
    string? DefaultLocale,
    TenantBrandingDto? Branding,
    TenantSettingsInput? Settings) : ICommand<TenantResponse>;

internal sealed class UpdateTenantValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantValidator()
    {
        When(x => x.Name is not null, () =>
            RuleFor(x => x.Name).NotEmpty().WithMessage("required")
                .MaximumLength(Tenant.MaxNameLength).WithMessage("too_long"));
        RuleFor(x => x.DefaultCurrency).Currency();
        RuleFor(x => x.Timezone).Timezone();
        RuleFor(x => x.DefaultLocale).Locale();

        When(x => x.Branding is not null, () =>
        {
            RuleFor(x => x.Branding!.LogoUrl).HttpsUrl();
            RuleFor(x => x.Branding!.CoverImageUrl).HttpsUrl();
            RuleFor(x => x.Branding!.PrimaryColor).HexColor();
            RuleFor(x => x.Branding!.AccentColor).HexColor();
            RuleFor(x => x.Branding!.Tagline).ShortText();
        });

        When(x => x.Settings is not null, () =>
        {
            RuleFor(x => x.Settings!.ContactEmail).EmailAddress().WithMessage("invalid_email").ShortText();
            RuleFor(x => x.Settings!.ContactPhone).ShortText(32);
            RuleFor(x => x.Settings!.WhatsApp).ShortText(32);
            RuleFor(x => x.Settings!.InstagramUrl).HttpsUrl();
            RuleFor(x => x.Settings!.WebsiteUrl).HttpsUrl();
        });
    }
}

internal sealed class UpdateTenantHandler(ITenantContext tenant, ITenantRepository tenants, TimeProvider clock)
    : ICommandHandler<UpdateTenantCommand, TenantResponse>
{
    public async ValueTask<Result<TenantResponse>> Handle(UpdateTenantCommand command, CancellationToken cancellationToken)
    {
        var current = await tenants.GetByIdAsync(tenant.TenantId, cancellationToken);
        if (current is null)
            return TenantsErrors.TenantNotFound;

        var now = clock.GetUtcNow();
        current.UpdateProfile(command.Name, command.DefaultCurrency, command.Timezone, command.DefaultLocale, now);
        if (command.Branding is not null)
            current.UpdateBranding(command.Branding.ToDomain(), now);
        if (command.Settings is not null)
            current.UpdateSettings(command.Settings.ToDomain(), now);

        return TenantResponse.From(current);
    }
}
