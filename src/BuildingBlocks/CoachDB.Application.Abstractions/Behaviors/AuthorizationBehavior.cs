using System.Collections.Concurrent;
using System.Reflection;
using CoachDB.Application.Abstractions.Authorization;
using CoachDB.Application.Abstractions.Context;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.SharedKernel.Results;
using Mediator;

namespace CoachDB.Application.Abstractions.Behaviors;

/// <summary>
/// Pipeline step 3: enforces <see cref="RequiresPermissionAttribute"/>, <see cref="RequiresTenantAttribute"/> and
/// <see cref="RequiresPlatformAdminAttribute"/> on the message type, in this order: 401 signed in → 400 tenant chosen
/// → 403 permission held. Permissions are per tenant, so a permission check implies a tenant.
/// Resource-level checks (e.g. "coach sees only assigned clients") stay in the handler's query.
/// </summary>
public sealed class AuthorizationBehavior<TMessage, TResponse>(IUserContext user, ITenantContext tenant)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public static readonly Error AuthRequired = Error.Unauthorized("auth_required", "Sign in to continue.");
    public static readonly Error PermissionDenied = Error.Forbidden("permission_denied", "You do not have permission for this action.");

    public ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var rules = AuthorizationRules.For(typeof(TMessage));
        if (rules.IsEmpty || !ResultFailure<TResponse>.IsSupported)
            return next(message, cancellationToken);

        var error = Check(rules);
        return error is null
            ? next(message, cancellationToken)
            : ValueTask.FromResult(ResultFailure<TResponse>.Create(error));
    }

    private Error? Check(AuthorizationRules rules)
    {
        if (!user.IsAuthenticated)
            return AuthRequired;

        if (rules.PlatformAdmin && !user.IsPlatformAdmin)
            return PermissionDenied;

        if (rules.Tenant && !tenant.HasTenant)
            return TenancyErrors.TenantRequired;

        foreach (var permission in rules.Permissions)
        {
            if (!user.HasPermission(permission))
                return PermissionDenied;
        }

        return null;
    }
}

internal sealed record AuthorizationRules(string[] Permissions, bool Tenant, bool PlatformAdmin)
{
    private static readonly ConcurrentDictionary<Type, AuthorizationRules> Cache = new();

    public bool IsEmpty => Permissions.Length == 0 && !Tenant && !PlatformAdmin;

    public static AuthorizationRules For(Type messageType) =>
        Cache.GetOrAdd(messageType, static t =>
        {
            var permissions = t.GetCustomAttributes<RequiresPermissionAttribute>().Select(a => a.Permission).ToArray();
            return new AuthorizationRules(
                permissions,
                Tenant: permissions.Length > 0 || t.IsDefined(typeof(RequiresTenantAttribute), inherit: false),
                PlatformAdmin: t.IsDefined(typeof(RequiresPlatformAdminAttribute), inherit: false));
        });
}
