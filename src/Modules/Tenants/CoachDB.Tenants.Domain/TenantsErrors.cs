using CoachDB.SharedKernel.Results;

namespace CoachDB.Tenants.Domain;

/// <summary>Every expected failure of the Tenants module, with its stable code.</summary>
public static class TenantsErrors
{
    public static readonly Error SlugInvalid =
        Error.Validation("slug_invalid", "Use 3-63 lower-case letters, digits or single hyphens, starting and ending with a letter or digit.");

    public static readonly Error SlugReserved =
        Error.Validation("slug_reserved", "This address is reserved by the platform.");

    public static readonly Error SlugTaken =
        Error.Conflict("slug_taken", "This address is already used by another business.");

    public static readonly Error TenantNotFound =
        Error.NotFound("tenant_not_found", "The business was not found.");

    public static readonly Error OwnedTenantLimitReached =
        Error.Forbidden("tenant_limit_reached", "You already own the maximum number of businesses.");

    public static readonly Error TrialPlanUnavailable =
        Error.Failure("trial_plan_unavailable", "The trial plan is not configured. Contact support.");

    public static readonly Error PlanNotFound =
        Error.NotFound("plan_not_found", "The plan was not found.");

    public static readonly Error PlanCodeTaken =
        Error.Conflict("plan_code_taken", "A plan with this code already exists.");

    public static readonly Error PlanCodeInvalid =
        Error.Validation("plan_code_invalid", "Use 2-50 lower-case letters, digits, '-' or '_'.");

    public static readonly Error PaymentAccountNotFound =
        Error.NotFound("payment_account_not_found", "The payout account was not found.");

    public static readonly Error PaymentAccountExists =
        Error.Conflict("payment_account_exists", "A payout account for this provider is already connected.");

    public static readonly Error PaymentAccountDisabled =
        Error.Conflict("payment_account_disabled", "A disabled payout account cannot be the default.");
}
