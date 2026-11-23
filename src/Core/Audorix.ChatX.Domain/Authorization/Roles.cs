namespace Audorix.ChatX.Domain.Authorization;

/// <summary>
/// Installation-level role and permission vocabulary (Architecture §10). Roles are collections of
/// permissions, not substitutes for resource-level authorization: no installation role grants
/// access to message content (Architecture §16). Organization and conversation roles live with
/// their modules.
/// </summary>
public static class Roles
{
    public const string InstallationAdministrator = "InstallationAdministrator";

    /// <summary>Read-only oversight (Phase 9, owner decision): users, organizations, settings and the audit log.</summary>
    public const string Auditor = "Auditor";

    /// <summary>Roles that may be assigned in this build.</summary>
    public static IReadOnlyList<string> Active { get; } = [InstallationAdministrator, Auditor];
}

public static class Permissions
{
    /// <summary>Read installation-level metadata and the effective configuration (secrets masked).</summary>
    public const string InstallationRead = "installation.read";

    /// <summary>Change live settings and provider configuration.</summary>
    public const string InstallationManage = "installation.manage";

    /// <summary>Populate demonstration data (only where demo data is enabled).</summary>
    public const string DemoDataManage = "installation.demo_data";

    /// <summary>List and view accounts (never message content).</summary>
    public const string UsersRead = "users.read";

    /// <summary>Disable/enable accounts, end sessions, confirm email, assign installation roles.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>List and view all organizations and their members.</summary>
    public const string OrganizationsRead = "organizations.read";

    /// <summary>Rename organizations, change member roles, remove members.</summary>
    public const string OrganizationsManage = "organizations.manage";

    public const string AuditRead = "audit.read";

    private static readonly Dictionary<string, string[]> RolePermissions = new(StringComparer.Ordinal)
    {
        [Roles.InstallationAdministrator] =
        [
            InstallationRead, InstallationManage, DemoDataManage, UsersRead, UsersManage, OrganizationsRead, OrganizationsManage, AuditRead,
        ],
        [Roles.Auditor] = [InstallationRead, UsersRead, OrganizationsRead, AuditRead],
    };

    public static IReadOnlyList<string> All { get; } =
        [InstallationRead, InstallationManage, DemoDataManage, UsersRead, UsersManage, OrganizationsRead, OrganizationsManage, AuditRead];

    /// <summary>Permissions granted by a role; unknown or inactive roles grant nothing.</summary>
    public static IReadOnlyList<string> ForRole(string role) =>
        RolePermissions.TryGetValue(role, out var permissions) ? permissions : [];

    public static bool IsGrantedBy(IEnumerable<string> roles, string permission) =>
        roles.Any(role => ForRole(role).Contains(permission, StringComparer.Ordinal));
}
