namespace Audorix.ChatX.Domain.Organizations;

// Phase 11 business communication (owner decisions of 2026-11-23, ADR-026):
// teams are named groups of workspace members with managers; guests are outside people admitted
// to specific channels of an organization without becoming members.

public enum TeamRole
{
    Member = 1,

    /// <summary>Manages the team's membership (Team Manager). Grants no workspace or channel administration.</summary>
    Manager = 2,
}

/// <summary>A named group of members of one workspace, used to bring whole groups into channels.</summary>
public sealed class Team
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 300;

    private Team(Guid id, Guid organizationId, Guid workspaceId, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        WorkspaceId = workspaceId;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Team Create(Guid id, Workspace workspace, string name, string? description, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var team = new Team(Require.Id(id), workspace.OrganizationId, workspace.Id, Require.Name(name, NameMaxLength, "team_name"), Require.Utc(nowUtc));
        team.Describe(description);
        return team;
    }

    public void Rename(string name) => Name = Require.Name(name, NameMaxLength, "team_name");

    public void Describe(string? description)
    {
        var text = description?.Trim() ?? string.Empty;
        Description = text.Length == 0 ? null
            : text.Length > DescriptionMaxLength ? throw new DomainRuleException("team_description_too_long", "The description is too long.")
            : text;
    }
}

/// <summary>A workspace member in a team. Removal deletes the row (teams keep no history).</summary>
public sealed class TeamMember
{
    private TeamMember(Guid id, Guid teamId, Guid workspaceId, Guid userId, TeamRole role, DateTimeOffset joinedAtUtc)
    {
        Id = id;
        TeamId = teamId;
        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TeamId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid UserId { get; private set; }

    public TeamRole Role { get; private set; }

    public DateTimeOffset JoinedAtUtc { get; private set; }

    /// <summary>Team members must be active members of the team's workspace.</summary>
    public static TeamMember Join(Guid id, Team team, WorkspaceMember workspaceMembership, TeamRole role, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(workspaceMembership);
        if (!workspaceMembership.IsActive || workspaceMembership.WorkspaceId != team.WorkspaceId)
        {
            throw new DomainRuleException("not_workspace_member", "Team members must be members of the team's workspace.");
        }

        return new TeamMember(Require.Id(id), team.Id, team.WorkspaceId, workspaceMembership.UserId, Require.Defined(role), Require.Utc(nowUtc));
    }

    public void ChangeRole(TeamRole role) => Role = Require.Defined(role);
}

/// <summary>A team linked to a channel: its members are added to the channel, now and when they join the team later.</summary>
public sealed class ChannelTeam
{
    private ChannelTeam(Guid conversationId, Guid teamId, DateTimeOffset linkedAtUtc)
    {
        ConversationId = conversationId;
        TeamId = teamId;
        LinkedAtUtc = linkedAtUtc;
    }

    public Guid ConversationId { get; private set; }

    public Guid TeamId { get; private set; }

    public DateTimeOffset LinkedAtUtc { get; private set; }

    public static ChannelTeam Link(Guid conversationId, Guid teamId, DateTimeOffset nowUtc) =>
        new(Require.Id(conversationId), Require.Id(teamId), Require.Utc(nowUtc));
}

/// <summary>
/// An outside person admitted to channels of an organization (Phase 11). Deliberately not an
/// <see cref="OrganizationMember"/>: every membership-based rule (directory, presence, direct
/// conversations, groups, workspaces, browsing channels) keeps excluding guests by construction.
/// Access also requires <see cref="Organization.GuestsEnabled"/>.
/// </summary>
public sealed class OrganizationGuest
{
    private OrganizationGuest(Guid id, Guid organizationId, Guid userId, Guid invitedByUserId, DateTimeOffset joinedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        UserId = userId;
        InvitedByUserId = invitedByUserId;
        Status = MembershipStatus.Active;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset JoinedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive => Status == MembershipStatus.Active;

    public static OrganizationGuest Join(Guid id, Guid organizationId, Guid userId, Guid invitedByUserId, DateTimeOffset nowUtc) =>
        new(Require.Id(id), Require.Id(organizationId), Require.Id(userId), Require.Id(invitedByUserId), Require.Utc(nowUtc));

    public void Reactivate(Guid invitedByUserId, DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        Status = MembershipStatus.Active;
        InvitedByUserId = Require.Id(invitedByUserId);
        JoinedAtUtc = Require.Utc(nowUtc);
        RevokedAtUtc = null;
    }

    public void Revoke(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        Status = MembershipStatus.Revoked;
        RevokedAtUtc = Require.Utc(nowUtc);
    }
}

/// <summary>
/// An invitation to join one channel as a guest. Like organization invitations it is addressed to
/// an email address (creating one reveals nothing about accounts) and needs the invitee's acceptance.
/// </summary>
public sealed class GuestInvitation
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private GuestInvitation(Guid id, Guid organizationId, Guid conversationId, string email, string normalizedEmail, Guid invitedByUserId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        ConversationId = conversationId;
        Email = email;
        NormalizedEmail = normalizedEmail;
        InvitedByUserId = invitedByUserId;
        Status = InvitationStatus.Pending;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = createdAtUtc + Lifetime;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The channel the guest is invited to.</summary>
    public Guid ConversationId { get; private set; }

    public string Email { get; private set; }

    public string NormalizedEmail { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public InvitationStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RespondedAtUtc { get; private set; }

    public static GuestInvitation Create(Guid id, Guid organizationId, Guid conversationId, string email, Guid invitedByUserId, DateTimeOffset nowUtc)
    {
        var trimmed = email?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > OrganizationInvitation.EmailMaxLength || !trimmed.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainRuleException("email_invalid", "A valid email address is required.");
        }

        return new GuestInvitation(Require.Id(id), Require.Id(organizationId), Require.Id(conversationId), trimmed, OrganizationInvitation.Normalize(trimmed),
            Require.Id(invitedByUserId), Require.Utc(nowUtc));
    }

    public bool IsOpen(DateTimeOffset nowUtc) => Status == InvitationStatus.Pending && nowUtc < ExpiresAtUtc;

    public void Accept(DateTimeOffset nowUtc) => Respond(InvitationStatus.Accepted, nowUtc);

    public void Decline(DateTimeOffset nowUtc) => Respond(InvitationStatus.Declined, nowUtc);

    public void Revoke(DateTimeOffset nowUtc) => Respond(InvitationStatus.Revoked, nowUtc);

    private void Respond(InvitationStatus status, DateTimeOffset nowUtc)
    {
        if (!IsOpen(Require.Utc(nowUtc)))
        {
            throw new DomainRuleException("invitation_not_open", "The invitation is no longer open.");
        }

        Status = status;
        RespondedAtUtc = nowUtc;
    }
}
