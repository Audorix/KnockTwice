namespace Audorix.KnockTwice.Domain.Authorization;

/// <summary>Describes the boundary where a permission is evaluated.</summary>
public static class PermissionScopes
{
    public const string Installation = "installation";
    public const string Organization = "organization";
    public const string Workspace = "workspace";
}

/// <summary>
/// Stable permission vocabulary for policy authoring. Planned entries are documentation only and
/// are deliberately not accepted by the current authorization handler until their enforcement
/// slice ships.
/// </summary>
public sealed record PermissionDefinition(
    string Key,
    string Area,
    string Scope,
    string State,
    string Description,
    string? LegacyFallbackKey = null);

public static class PermissionCatalog
{
    public const string Implemented = "implemented";
    public const string Planned = "planned";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Permissions.InstallationRead, "installation", PermissionScopes.Installation, Implemented, "Read installation metadata and configuration with secrets masked."),
        new(Permissions.InstallationManage, "installation", PermissionScopes.Installation, Implemented, "Manage current installation settings and provider configuration."),
        new(Permissions.MessagesTranslate, "messages", PermissionScopes.Installation, Implemented, "Translate incoming messages using the configured translation provider."),
        new(Permissions.DemoDataManage, "installation", PermissionScopes.Installation, Implemented, "Load or clear demo data when that feature is enabled."),
        new(Permissions.UsersRead, "installation", PermissionScopes.Installation, Implemented, "Search and view user accounts."),
        new(Permissions.UsersManage, "installation", PermissionScopes.Installation, Implemented, "Manage accounts and installation access-control assignments."),
        new(Permissions.OrganizationsRead, "installation", PermissionScopes.Installation, Implemented, "View organizations and their members across the installation."),
        new(Permissions.OrganizationsManage, "installation", PermissionScopes.Installation, Implemented, "Manage organizations and membership across the installation."),
        new(Permissions.AuditRead, "installation", PermissionScopes.Installation, Implemented, "Read the installation audit log."),

        new("private_chats.use", "privateChats", PermissionScopes.Organization, Implemented, "Use private chats where the organization grants access."),
        new("support.access", "support", PermissionScopes.Organization, Implemented, "Open the organization's support workspace."),
        new("support.cases.read", "support", PermissionScopes.Organization, Implemented, "Read support cases in the organization."),
        new("support.cases.manage", "support", PermissionScopes.Organization, Implemented, "Assign, update, and resolve support cases."),
        new("organization.discover", "organizations", PermissionScopes.Organization, Implemented, "Discover and participate in the selected organization."),
        new("organization.manage", "organizations", PermissionScopes.Organization, Implemented, "Manage settings for the selected organization."),
        new("organization.members.manage", "organizations", PermissionScopes.Organization, Implemented, "Invite, remove, and update members in the selected organization."),
        new("organization.roles.manage", "organizations", PermissionScopes.Organization, Implemented, "Manage roles and groups in the selected organization."),
        new("organization.workspaces.manage", "organizations", PermissionScopes.Organization, Implemented, "Create and administer workspaces in the selected organization."),
        new("channels.create", "channels", PermissionScopes.Workspace, Implemented, "Create channels in the selected workspace."),
        new("channels.manage", "channels", PermissionScopes.Workspace, Implemented, "Administer channels in the selected workspace."),
        new("roles.read", "administration", PermissionScopes.Installation, Implemented, "Read role definitions and assignments.", Permissions.UsersRead),
        new("roles.manage", "administration", PermissionScopes.Installation, Implemented, "Create and update installation roles.", Permissions.UsersManage),
        new("permissions.read", "administration", PermissionScopes.Installation, Implemented, "Read the permission catalog and effective access.", Permissions.UsersRead),
        new("permissions.manage", "administration", PermissionScopes.Installation, Implemented, "Change permission grants and scoped assignments.", Permissions.UsersManage),
        new("groups.read", "administration", PermissionScopes.Installation, Implemented, "Read installation groups and membership.", Permissions.UsersRead),
        new("groups.manage", "administration", PermissionScopes.Installation, Implemented, "Manage installation groups and membership.", Permissions.UsersManage),
        new("providers.read", "administration", PermissionScopes.Installation, Implemented, "Read provider configuration with secrets masked.", Permissions.InstallationRead),
        new("providers.manage", "administration", PermissionScopes.Installation, Implemented, "Configure and test providers.", Permissions.InstallationManage),
        new("settings.read", "administration", PermissionScopes.Installation, Implemented, "Read installation settings.", Permissions.InstallationRead),
        new("settings.manage", "administration", PermissionScopes.Installation, Implemented, "Change installation settings.", Permissions.InstallationManage),
    ];

    public static IReadOnlyList<PermissionDefinition> Available { get; } = All.Where(p => p.State == Implemented && p.Scope == PermissionScopes.Installation).ToList();

    public static IReadOnlyList<PermissionDefinition> ForScope(string scope) =>
        All.Where(p => string.Equals(p.Scope, scope, StringComparison.Ordinal)).ToList();

    public static PermissionDefinition? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
}
