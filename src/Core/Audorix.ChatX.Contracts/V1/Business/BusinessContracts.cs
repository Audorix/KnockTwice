using Audorix.ChatX.Contracts.V1.Users;

namespace Audorix.ChatX.Contracts.V1.Business;

// Phase 11 business communication: channels, teams, guests and the support inbox.

public sealed record TeamSummaryResponse(Guid Id, string Name);

/// <param name="Visibility"><c>public</c> or <c>private</c>.</param>
/// <param name="MyRole"><c>manager</c>, <c>member</c>, <c>guest</c>, or null when the caller does not take part.</param>
/// <param name="CanManage">Channel managers, workspace administrators and organization owners/administrators.</param>
public sealed record ChannelResponse(
    Guid Id,
    Guid OrganizationId,
    Guid WorkspaceId,
    string WorkspaceName,
    string Name,
    string? Description,
    string Visibility,
    int MemberCount,
    bool Joined,
    string? MyRole,
    bool CanManage,
    IReadOnlyList<TeamSummaryResponse> Teams,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastActivityAtUtc);

/// <param name="Role"><c>manager</c>, <c>member</c> or <c>guest</c>.</param>
public sealed record ChannelMemberResponse(UserSummaryResponse User, string Role, DateTimeOffset JoinedAtUtc);

public sealed record ChannelDetailResponse(ChannelResponse Channel, IReadOnlyList<ChannelMemberResponse> Members);

public sealed record CreateChannelRequest(string? Name, string? Description, string? Visibility, IReadOnlyList<Guid>? UserIds, IReadOnlyList<Guid>? TeamIds);

/// <summary>Null fields stay unchanged.</summary>
public sealed record UpdateChannelRequest(string? Name, string? Description, string? Visibility);

public sealed record AddChannelMembersRequest(IReadOnlyList<Guid>? UserIds, IReadOnlyList<Guid>? TeamIds);

public sealed record SetChannelManagerRequest(bool Manager);

/// <param name="MyRole"><c>manager</c>, <c>member</c>, or null.</param>
public sealed record TeamResponse(Guid Id, Guid WorkspaceId, string Name, string? Description, int MemberCount, string? MyRole, bool CanManage, DateTimeOffset CreatedAtUtc);

public sealed record CreateTeamRequest(string? Name, string? Description);

public sealed record UpdateTeamRequest(string? Name, string? Description);

/// <param name="Role"><c>manager</c> or <c>member</c>.</param>
public sealed record TeamMemberResponse(UserSummaryResponse User, string Role, DateTimeOffset JoinedAtUtc);

public sealed record AddTeamMemberRequest(Guid UserId, string? Role);

public sealed record ChangeTeamRoleRequest(string? Role);

public sealed record CreateGuestInvitationRequest(Guid ChannelId, string? Email);

public sealed record GuestInvitationResponse(Guid Id, string Email, Guid ChannelId, string ChannelName, UserSummaryResponse? InvitedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

/// <summary>A guest invitation addressed to the signed-in user's confirmed email address.</summary>
public sealed record MyGuestInvitationResponse(
    Guid Id, Guid OrganizationId, string OrganizationName, Guid ChannelId, string ChannelName, UserSummaryResponse? InvitedBy, DateTimeOffset ExpiresAtUtc);

public sealed record GuestChannelResponse(Guid Id, string Name);

public sealed record GuestResponse(UserSummaryResponse User, DateTimeOffset JoinedAtUtc, IReadOnlyList<GuestChannelResponse> Channels);

public sealed record AcceptGuestInvitationResponse(Guid ConversationId);

/// <param name="Status"><c>open</c> or <c>closed</c>.</param>
public sealed record SupportConversationResponse(
    Guid ConversationId,
    string VisitorName,
    Guid? WidgetId,
    string? WidgetName,
    string Status,
    UserSummaryResponse? AssignedTo,
    bool Participating,
    int UnreadCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastActivityAtUtc);

/// <param name="AssigneeId">Null unassigns.</param>
public sealed record AssignSupportConversationRequest(Guid? AssigneeId);

/// <param name="Status"><c>open</c> or <c>closed</c>.</param>
public sealed record SetSupportStatusRequest(string? Status);
