namespace Audorix.KnockTwice.Contracts.V1.Admin;

/// <summary>An account as installation administrators and auditors see it. Never includes message content.</summary>
/// <param name="LockedOut">Temporarily locked after failed sign-ins (not the same as disabled).</param>
/// <param name="PhotoUrl">The person's profile photo (versioned), or null when they show initials.</param>
public sealed record AdminUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Initials,
    bool EmailConfirmed,
    bool Disabled,
    bool LockedOut,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSignInAtUtc,
    int ActiveSessions,
    int Organizations,
    string? PhotoUrl = null);

public sealed record AdminUserPageResponse(IReadOnlyList<AdminUserResponse> Items, int Total);

/// <param name="Role">An installation role: <c>InstallationAdministrator</c> or <c>Auditor</c>.</param>
public sealed record AdminRoleRequest(string? Role);

public sealed record AdminSessionsRevokedResponse(int Revoked);

/// <param name="PhotoUrl">The organization's picture for administrators (versioned), or null for its initials.</param>
public sealed record AdminOrganizationResponse(Guid Id, string Name, DateTimeOffset CreatedAtUtc, int Members, int Owners, string? PhotoUrl = null);

public sealed record AdminOrganizationPageResponse(IReadOnlyList<AdminOrganizationResponse> Items, int Total);

/// <param name="Role"><c>owner</c>, <c>administrator</c> or <c>member</c>.</param>
/// <param name="PhotoUrl">The member's profile photo (versioned), or null when they show initials.</param>
public sealed record AdminOrganizationMemberResponse(
    Guid UserId, string DisplayName, string Initials, string Email, string Role, DateTimeOffset JoinedAtUtc, string? PhotoUrl = null);

public sealed record AdminOrganizationDetailResponse(AdminOrganizationResponse Organization, IReadOnlyList<AdminOrganizationMemberResponse> Members);

public sealed record AdminRenameOrganizationRequest(string? Name);

public sealed record AdminMemberRoleRequest(string? Role);

/// <summary>An installation role and the permissions it grants (fixed in this release).</summary>
public sealed record AdminRoleResponse(string Name, IReadOnlyList<string> Permissions, int Members);

/// <summary>A stable permission key and the scope in which a future grant will be evaluated.</summary>
public sealed record AdminPermissionDefinitionResponse(string Key, string Area, string Scope, string State, string Description);

public sealed record AccessControlPermissionResponse(string Key, string Effect);
public sealed record AccessControlRoleResponse(Guid Id, string Name, string Description, string Scope, IReadOnlyList<AccessControlPermissionResponse> Permissions, int Groups, Guid? OrganizationId = null, Guid? WorkspaceId = null);
public sealed record AccessControlGroupResponse(Guid Id, string Name, string Description, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> RoleIds,
    string Scope = "installation", Guid? OrganizationId = null, Guid? WorkspaceId = null);
public sealed record EffectivePermissionsResponse(Guid UserId, IReadOnlyList<string> Permissions, IReadOnlyList<string> DeniedPermissions);
public sealed record EffectiveAccessReviewResponse(Guid UserId, string Scope, Guid? OrganizationId, Guid? WorkspaceId, IReadOnlyList<EffectiveAccessGroupResponse> Groups, IReadOnlyList<EffectiveAccessPermissionResponse> Permissions);
public sealed record EffectiveAccessReviewContextResponse(string Scope, Guid? OrganizationId, Guid? WorkspaceId, string Name);
public sealed record EffectiveAccessGroupResponse(Guid Id, string Name, string Scope, IReadOnlyList<string> Roles);
public sealed record EffectiveAccessPermissionResponse(string Key, string Outcome, string Source, IReadOnlyList<string> Decisions);
public sealed record AccessControlPermissionRequest(string? Key, string? Effect);
public sealed record CreateAccessControlRoleRequest(string? Name, string? Description, IReadOnlyList<AccessControlPermissionRequest>? Permissions);
public sealed record UpdateAccessControlRoleRequest(string? Name, string? Description, IReadOnlyList<AccessControlPermissionRequest>? Permissions);
public sealed record CreateAccessControlGroupRequest(string? Name, string? Description);
public sealed record UpdateAccessControlGroupRequest(string? Name, string? Description);

/// <summary>One audit record. Details hold short, non-sensitive facts only.</summary>
/// <param name="ActorPhotoUrl">The actor's profile photo (versioned), or null when they show initials or are gone.</param>
public sealed record AuditEntryResponse(
    Guid Id, DateTimeOffset OccurredAtUtc, string Action, bool Succeeded, Guid? ActorUserId, string? ActorName, string? TargetType, string? TargetId,
    string? IpAddress, string? Details, string? ActorPhotoUrl = null);

/// <param name="NextBefore">Pass as <c>before</c> for older entries; null at the end.</param>
/// <param name="Total">Redesign 2026-11: how many entries match the filters (for page numbers with <c>skip</c>).</param>
public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Items, DateTimeOffset? NextBefore, int? Total = null);

/// <summary>A live setting (database layer).</summary>
/// <param name="Kind"><c>boolean</c>, <c>integer</c>, <c>bytes</c>, <c>duration</c> (seconds), <c>choice</c>, <c>languages</c> or <c>extensions</c>.</param>
/// <param name="Source">Where the effective value comes from: <c>default</c>, <c>appsettings</c>, <c>file</c>, <c>database</c>, <c>environment</c>, <c>command_line</c> or <c>host</c>.</param>
/// <param name="Locked">Set by the environment or command line; it cannot be changed here.</param>
public sealed record RuntimeSettingResponse(
    string Key, string Group, string Kind, object? Value, string Source, bool Locked, long? Min, long? Max, IReadOnlyList<string>? Choices);

/// <summary>A provider setting (settings file; applies after a restart). Secret values are never returned.</summary>
/// <param name="Kind"><c>text</c>, <c>secret</c>, <c>integer</c>, <c>boolean</c> or <c>choice</c>.</param>
/// <param name="HasValue">Whether the setting has a value (the only thing shown for secrets).</param>
public sealed record ProviderFieldResponse(string Key, string Kind, object? Value, bool HasValue, string Source, bool Locked, IReadOnlyList<string>? Choices);

/// <param name="Name"><c>email</c>, <c>captcha</c>, <c>storage</c> or <c>scanning</c>.</param>
/// <param name="RestartRequired">Saved since the server started; restart it to apply.</param>
public sealed record ProviderGroupResponse(string Name, bool RestartRequired, IReadOnlyList<ProviderFieldResponse> Fields);

/// <param name="Outcome"><c>sent</c> (email), <c>ok</c>, <c>valid</c> (no live check possible), <c>disabled</c>, <c>unavailable</c> or <c>failed</c>.</param>
public sealed record ProviderTestResponse(string Outcome);
public sealed record StorageMigrationDryRunRequest(string? SourceProvider, string? DestinationProvider);

/// <summary>Installation overview for the administration home.</summary>
public sealed record AdminOverviewResponse(
    Guid? InstallationId, string Version, string Environment, int Users, int DisabledUsers, int Organizations, string DatabaseProvider, string StorageProvider,
    bool ScanningEnabled, string RegistrationMode, IReadOnlyList<string> RestartRequiredFor);
