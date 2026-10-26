namespace CoachDB.Application.Abstractions.Authorization;

/// <summary>
/// Named permissions. Code checks permissions, never role names; the role → permission map is data
/// (Team module), so changing what a role can do needs no code change.
/// </summary>
public static class Permissions
{
    public static class Tenant
    {
        public const string Manage = "tenant.manage";
        public const string Billing = "tenant.billing";
    }

    public static class Team
    {
        public const string Read = "team.read";
        public const string Manage = "team.manage";
    }

    public static class Clients
    {
        public const string Read = "clients.read";
        public const string ReadAll = "clients.read_all";
        public const string Write = "clients.write";
    }

    public static class Packages
    {
        public const string Write = "packages.write";
    }

    public static class Questionnaires
    {
        public const string Write = "questionnaires.write";
    }

    public static class Plans
    {
        public const string Write = "plans.write";
        public const string WriteDraft = "plans.write_draft";
    }

    public static class CheckIns
    {
        public const string Review = "checkins.review";
    }

    public static class Chat
    {
        public const string Use = "chat.use";
    }

    public static class Revenue
    {
        public const string ReadAll = "revenue.read_all";
        public const string ReadOwn = "revenue.read_own";
    }
}

/// <summary>
/// Declares the permission a command or query needs in the current tenant. Checked by <c>AuthorizationBehavior</c>
/// before the handler runs; implies <see cref="RequiresTenantAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

/// <summary>
/// The message runs inside a tenant: the caller is signed in and a member of the tenant chosen by
/// tenant resolution (<c>X-Tenant</c> or subdomain). Use it when any member may call, with no specific permission.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresTenantAttribute : Attribute;

/// <summary>Only platform administrators (the <c>/admin</c> API) may send this message.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresPlatformAdminAttribute : Attribute;
