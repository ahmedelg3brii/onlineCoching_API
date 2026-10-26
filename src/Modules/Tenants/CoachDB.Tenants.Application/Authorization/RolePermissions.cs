using CoachDB.Application.Abstractions.Authorization;
using CoachDB.Tenants.Domain.Members;

namespace CoachDB.Tenants.Application.Authorization;

/// <summary>
/// Default role → permission map, from the PRD's roles table. Code checks permissions, never roles, so this map is
/// the only place a role's abilities are defined. The Team module will make it data (custom roles per tenant).
/// </summary>
public static class RolePermissions
{
    private static readonly Dictionary<MemberRole, string[]> Map = new()
    {
        [MemberRole.Owner] =
        [
            Permissions.Tenant.Manage, Permissions.Tenant.Billing,
            Permissions.Team.Read, Permissions.Team.Manage,
            Permissions.Clients.Read, Permissions.Clients.ReadAll, Permissions.Clients.Write,
            Permissions.Packages.Write, Permissions.Questionnaires.Write,
            Permissions.Plans.Write, Permissions.Plans.WriteDraft,
            Permissions.CheckIns.Review, Permissions.Chat.Use,
            Permissions.Revenue.ReadAll, Permissions.Revenue.ReadOwn,
        ],
        [MemberRole.Coach] =
        [
            Permissions.Team.Read,
            Permissions.Clients.Read, Permissions.Clients.Write,
            Permissions.Packages.Write, Permissions.Questionnaires.Write,
            Permissions.Plans.Write, Permissions.Plans.WriteDraft,
            Permissions.CheckIns.Review, Permissions.Chat.Use,
            Permissions.Revenue.ReadOwn,
        ],
        [MemberRole.Assistant] =
        [
            Permissions.Team.Read,
            Permissions.Clients.Read,
            Permissions.Questionnaires.Write,
            Permissions.Plans.WriteDraft,
            Permissions.Chat.Use,
        ],
    };

    public static IReadOnlyCollection<string> For(MemberRole role) => Map[role];
}
