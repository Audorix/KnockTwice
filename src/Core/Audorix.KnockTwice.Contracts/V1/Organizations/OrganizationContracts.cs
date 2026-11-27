using Audorix.KnockTwice.Contracts.V1.Users;

namespace Audorix.KnockTwice.Contracts.V1.Organizations;

/// <summary>Organization roles on the wire: <c>owner</c>, <c>administrator</c>, <c>member</c>.</summary>
public static class OrganizationRoles
{
    public const string Owner = "owner";
    public const string Administrator = "administrator";
    public const string Member = "member";
}

/// <summary>Workspace roles on the wire: <c>administrator</c>, <c>member</c>.</summary>
public static class WorkspaceRoles
{
    public const string Administrator = "administrator";
    public const string Member = "member";
}

/// <param name="SupportAgent">The caller holds the Support Agent role (Phase 11).</param>
/// <param name="GuestsEnabled">Organization setting: guests may be invited to channels.</param>
/// <param name="ChannelCreation"><c>members</c> or <c>administrators</c>.</param>
/// <param name="PhotoUrl">The organization's square picture (null: show its initials); the address changes with every new picture.</param>
/// <param name="BannerUrl">The wide background image of its header (null: none).</param>
public sealed record OrganizationResponse(
    Guid Id, string Name, string MyRole, DateTimeOffset CreatedAtUtc, IReadOnlyList<string> MyPermissions,
    bool SupportAgent = false, bool GuestsEnabled = true, string ChannelCreation = "members",
    string? PhotoUrl = null, string? BannerUrl = null, bool PrivateChatsRequireSharedTeam = false);

/// <summary>Organization-specific configuration (Phase 11). Null fields stay unchanged.</summary>
public sealed record UpdateOrganizationSettingsRequest(bool? GuestsEnabled, string? ChannelCreation, bool? PrivateChatsRequireSharedTeam = null);

public sealed record AddOrganizationDomainRequest(string? Domain);

public sealed record OrganizationDomainResponse(Guid Id, string Domain, string TxtRecordName, string TxtRecordValue, bool Verified, DateTimeOffset CreatedAtUtc, DateTimeOffset? VerifiedAtUtc);

public sealed record SetAutomaticEnrollmentRequest(bool Enabled);

public sealed record AutomaticEnrollmentResponse(bool Enabled);

public sealed record ConfigureOrganizationSsoRequest(string? Issuer, string? ClientId, string? ClientSecret);

public sealed record OrganizationSsoResponse(bool Configured, string? Issuer, string? ClientId, bool ClientSecretConfigured);

public sealed record SetSupportAgentRequest(bool SupportAgent);

public sealed record RenameWorkspaceRequest(string? Name);

public sealed record ChangeWorkspaceRoleRequest(string? Role);

public sealed record CreateOrganizationRequest(string? Name);

public sealed record UpdateOrganizationRequest(string? Name);

public sealed record OrganizationMemberResponse(UserSummaryResponse User, string Role, DateTimeOffset JoinedAtUtc, bool SupportAgent = false);

/// <summary>Invitations are only included for members allowed to manage membership.</summary>
public sealed record OrganizationMembersResponse(IReadOnlyList<OrganizationMemberResponse> Members, IReadOnlyList<InvitationResponse>? Invitations);

public sealed record InvitationResponse(Guid Id, string Email, string Role, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

public sealed record CreateInvitationRequest(string? Email, string? Role);

public sealed record ChangeMemberRoleRequest(string? Role);

/// <summary>An invitation addressed to the signed-in user's confirmed email address.</summary>
public sealed record MyInvitationResponse(
    Guid Id, Guid OrganizationId, string OrganizationName, string Role, UserSummaryResponse? InvitedBy, DateTimeOffset ExpiresAtUtc);

public sealed record WorkspaceResponse(Guid Id, Guid OrganizationId, string Name, string? MyRole, DateTimeOffset CreatedAtUtc);

public sealed record CreateWorkspaceRequest(string? Name);

public sealed record WorkspaceMemberResponse(UserSummaryResponse User, string Role, DateTimeOffset JoinedAtUtc);

public sealed record AddWorkspaceMemberRequest(Guid UserId, string? Role);

/// <summary>Confirms deleting an organization by repeating its name (Phase 13).</summary>
public sealed record DeleteOrganizationRequest(string? ConfirmName);
