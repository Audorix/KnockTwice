using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Domain.Authorization;
using Audorix.KnockTwice.Domain.Conversations;
using Audorix.KnockTwice.Domain.Organizations;

namespace Audorix.KnockTwice.Application.Conversations;

public sealed record ChannelView(
    Conversation Channel, string WorkspaceName, int MemberCount, ConversationParticipant? Me, bool CanManage, IReadOnlyList<Team> Teams);

public sealed record ChannelDetailView(ChannelView Channel, IReadOnlyList<ParticipantView> Participants);

/// <summary>
/// Channels (Phase 11, owner decisions of 2026-11-23, ADR-026). A channel belongs to one workspace.
/// Public channels can be found and joined by any member of the workspace; private ones only by
/// invitation. Members see history from their join, like groups. Channel managers (participants
/// with the administrator role), workspace administrators and organization owners/administrators
/// manage every channel; teams only grant membership. Guests take part only in channels they were
/// invited to and never manage, browse or invite.
/// </summary>
public sealed partial class ConversationService
{
    private const string ChannelNotFound = "channel_not_found";

    private sealed record WorkspaceContext(Organization Organization, OrganizationMember Membership, Workspace Workspace, WorkspaceMember? WorkspaceMembership, bool ManagesAll)
    {
        public bool IsWorkspaceMember => WorkspaceMembership is { IsActive: true };
    }

    private sealed record ChannelAccess(Conversation Channel, ConversationParticipant? Me, bool CanManage, bool IsWorkspaceMember);

    // ---- Browsing and reading ----------------------------------------------------------------

    /// <summary>Public channels of the workspace, the caller's private ones, and every channel for administrators.</summary>
    public async Task<IReadOnlyList<ChannelView>> ListChannelsAsync(Guid userId, Guid organizationId, Guid workspaceId, CancellationToken cancellationToken)
    {
        var context = await RequireWorkspaceAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        var channels = await store.ListChannelsAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        var counts = await store.CountActiveParticipantsAsync(channels.Select(c => c.Id).ToList(), cancellationToken).ConfigureAwait(false);
        var views = new List<ChannelView>();
        foreach (var channel in channels)
        {
            var participant = await store.FindParticipantAsync(channel.Id, userId, cancellationToken).ConfigureAwait(false);
            var me = participant is { IsActive: true } && context.IsWorkspaceMember ? participant : null;
            var visible = me is not null || context.ManagesAll || (channel.Visibility == ChannelVisibility.Public && context.IsWorkspaceMember);
            if (visible)
            {
                views.Add(new ChannelView(channel, context.Workspace.Name, counts.GetValueOrDefault(channel.Id), me,
                    context.ManagesAll || me is { IsGuest: false, Role: >= ParticipantRole.Administrator }, []));
            }
        }

        return views.OrderBy(v => v.Channel.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<ChannelDetailView> GetChannelAsync(Guid userId, Guid channelId, CancellationToken cancellationToken)
    {
        var access = await RequireChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
        return await DetailAsync(access, cancellationToken).ConfigureAwait(false);
    }

    // ---- Creating, joining, leaving ------------------------------------------------------------

    public async Task<ChannelDetailView> CreateChannelAsync(
        Guid userId, Guid organizationId, Guid workspaceId, string? name, string? description, ChannelVisibility visibility,
        IReadOnlyCollection<Guid>? memberIds, IReadOnlyCollection<Guid>? teamIds, CancellationToken cancellationToken)
    {
        var context = await RequireWorkspaceAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        if (!context.IsWorkspaceMember)
        {
            throw new ForbiddenException("not_workspace_member");
        }

        var createPermissionConfigured = scopedAccess is not null
            && await scopedAccess.IsWorkspacePermissionConfiguredAsync(workspaceId, "channels.create", cancellationToken).ConfigureAwait(false);
        var canCreate = createPermissionConfigured
            ? await HasWorkspacePermissionAsync(userId, workspaceId, "channels.create", cancellationToken).ConfigureAwait(false)
            : context.Organization.ChannelCreation != ChannelCreationPolicy.Administrators || context.ManagesAll;
        if (!canCreate)
        {
            throw new ForbiddenException("channel_creation_restricted");
        }

        var teams = await RequireTeamsAsync(workspaceId, teamIds, cancellationToken).ConfigureAwait(false);
        var people = await ResolveChannelMembersAsync(userId, organizationId, workspaceId, memberIds, teams, cancellationToken).ConfigureAwait(false);

        var now = time.GetUtcNow();
        var channel = Conversation.CreateChannel(Guid.CreateVersion7(now), organizationId, workspaceId, name ?? string.Empty, visibility, userId, now);
        channel.Describe(description);
        var created = ConversationEvent.Record(Guid.CreateVersion7(now), channel, ConversationEventKind.Created, userId, null, channel.Title, now);
        store.Add(channel);
        store.Add(created);
        store.Add(ConversationParticipant.Join(Guid.CreateVersion7(now), channel.Id, userId, ParticipantRole.Administrator, created.Sequence, now));
        foreach (var person in people.Where(p => p != userId))
        {
            var added = ConversationEvent.Record(Guid.CreateVersion7(now), channel, ConversationEventKind.MemberAdded, userId, person, null, now);
            store.Add(added);
            store.Add(ConversationParticipant.Join(Guid.CreateVersion7(now), channel.Id, person, ParticipantRole.Member, added.Sequence, now));
        }

        foreach (var team in teams)
        {
            organizations.Add(ChannelTeam.Link(channel.Id, team.Id, now));
        }

        // Organization store and conversation store share the unit of work: one save.
        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("channel_name_taken");
        }

        await NotifyParticipantsAsync(channel, "created", [], cancellationToken).ConfigureAwait(false);
        return await GetChannelAsync(userId, channel.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Joins a public channel (any workspace member) or, for managers, any channel. History starts now.</summary>
    public async Task<ChannelDetailView> JoinChannelAsync(Guid userId, Guid channelId, CancellationToken cancellationToken)
    {
        var access = await RequireChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
        if (access.Me is not null)
        {
            return await DetailAsync(access, cancellationToken).ConfigureAwait(false);
        }

        if (!access.IsWorkspaceMember)
        {
            throw new ForbiddenException("not_workspace_member");
        }

        RequireNotArchived(access.Channel);
        if (access.Channel.Visibility != ChannelVisibility.Public && !access.CanManage)
        {
            throw new ResourceNotFoundException(ChannelNotFound);
        }

        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var channel = await RequireChannelEntityAsync(channelId, cancellationToken).ConfigureAwait(false);
            await AddChannelParticipantAsync(channel, userId, userId, asGuest: false, cancellationToken).ConfigureAwait(false);
            updated = channel;
        }, cancellationToken).ConfigureAwait(false);

        await NotifyParticipantsAsync(updated!, "members", [], cancellationToken).ConfigureAwait(false);
        return await GetChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
    }

    public async Task LeaveChannelAsync(Guid userId, Guid channelId, CancellationToken cancellationToken)
    {
        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var (channel, me) = await RequireAccessAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
            channel.RequireChannel();
            var now = time.GetUtcNow();
            me.Leave(now);
            store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), channel, ConversationEventKind.MemberLeft, userId, null, null, now));
            updated = channel;
        }, cancellationToken).ConfigureAwait(false);

        await EndAccessAsync(updated!, userId, cancellationToken).ConfigureAwait(false);
    }

    // ---- Management (channel managers, workspace and organization administrators) ---------------

    public async Task<ChannelDetailView> UpdateChannelAsync(
        Guid userId, Guid channelId, string? name, string? description, ChannelVisibility? visibility, CancellationToken cancellationToken)
    {
        await RequireManagedChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
        Conversation? updated = null;
        try
        {
            await MutateAsync(async () =>
            {
                var channel = await RequireChannelEntityAsync(channelId, cancellationToken).ConfigureAwait(false);
                var now = time.GetUtcNow();
                if (name is not null && name.Trim() != channel.Title)
                {
                    channel.Rename(name);
                    store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), channel, ConversationEventKind.Renamed, userId, null, channel.Title, now));
                }

                if (description is not null && (description.Trim().Length == 0 ? null : description.Trim()) != channel.Description)
                {
                    channel.Describe(description);
                    store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), channel, ConversationEventKind.DescriptionChanged, userId, null, channel.Description, now));
                }

                if (visibility is { } v && v != channel.Visibility)
                {
                    channel.SetVisibility(v);
                    store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), channel, ConversationEventKind.VisibilityChanged, userId, null,
                        v == ChannelVisibility.Public ? "public" : "private", now));
                }

                updated = channel;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (ConflictException) when (name is not null)
        {
            throw new ConflictException("channel_name_taken");
        }

        await NotifyParticipantsAsync(updated!, "updated", [], cancellationToken).ConfigureAwait(false);
        return await GetChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Archives a channel (read-only: history stays, nobody posts, edits, reacts, joins or is added)
    /// or restores it (managers; redesign 2026-11). Repeating the current state changes nothing.
    /// </summary>
    public async Task<ChannelDetailView> SetChannelArchivedAsync(Guid userId, Guid channelId, bool archived, CancellationToken cancellationToken)
    {
        await RequireManagedChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var channel = await RequireChannelEntityAsync(channelId, cancellationToken).ConfigureAwait(false);
            if (channel.IsArchived == archived)
            {
                return;
            }

            var now = time.GetUtcNow();
            if (archived)
            {
                channel.Archive(now);
            }
            else
            {
                channel.Restore();
            }

            store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), channel, ConversationEventKind.Archived, userId, null, archived ? "archived" : "restored", now));
            updated = channel;
        }, cancellationToken).ConfigureAwait(false);

        if (updated is not null)
        {
            await NotifyParticipantsAsync(updated, "updated", [], cancellationToken).ConfigureAwait(false);
        }

        return await GetChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refuses changes to an archived channel's conversation (409 <c>channel_archived</c>).</summary>
    public static void RequireNotArchived(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (conversation.IsArchived)
        {
            throw new ConflictException("channel_archived");
        }
    }

    /// <summary>Adds workspace members and whole teams (linking them, so later team members follow). Already present people are skipped.</summary>
    public async Task<ChannelDetailView> AddChannelMembersAsync(
        Guid userId, Guid channelId, IReadOnlyCollection<Guid>? userIds, IReadOnlyCollection<Guid>? teamIds, CancellationToken cancellationToken)
    {
        var access = await RequireManagedChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
        var channel = access.Channel;
        RequireNotArchived(channel);
        if ((userIds?.Count ?? 0) + (teamIds?.Count ?? 0) == 0)
        {
            throw new RequestValidationException("userIds", "required");
        }

        var teams = await RequireTeamsAsync(channel.WorkspaceId!.Value, teamIds, cancellationToken).ConfigureAwait(false);
        var people = await ResolveChannelMembersAsync(userId, channel.OrganizationId!.Value, channel.WorkspaceId!.Value, userIds, teams, cancellationToken)
            .ConfigureAwait(false);
        var links = await organizations.ListChannelTeamsAsync(channelId, cancellationToken).ConfigureAwait(false);

        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var tracked = await RequireChannelEntityAsync(channelId, cancellationToken).ConfigureAwait(false);
            foreach (var person in people)
            {
                await AddChannelParticipantAsync(tracked, userId, person, asGuest: false, cancellationToken).ConfigureAwait(false);
            }

            foreach (var team in teams.Where(t => links.All(l => l.TeamId != t.Id)))
            {
                organizations.Add(ChannelTeam.Link(channelId, team.Id, time.GetUtcNow()));
            }

            updated = tracked;
        }, cancellationToken).ConfigureAwait(false);

        await NotifyParticipantsAsync(updated!, "members", [], cancellationToken).ConfigureAwait(false);
        return await GetChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes someone from the channel (managers); removing yourself is leaving.</summary>
    public async Task RemoveChannelMemberAsync(Guid userId, Guid channelId, Guid targetUserId, CancellationToken cancellationToken)
    {
        if (targetUserId == userId)
        {
            await LeaveChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
            return;
        }

        await RequireManagedChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var channel = await RequireChannelEntityAsync(channelId, cancellationToken).ConfigureAwait(false);
            var target = await RequireActiveParticipantAsync(channelId, targetUserId, cancellationToken).ConfigureAwait(false);
            var now = time.GetUtcNow();
            target.Remove(now);
            store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), channel, ConversationEventKind.MemberRemoved, userId, targetUserId, null, now));
            updated = channel;
        }, cancellationToken).ConfigureAwait(false);

        await EndAccessAsync(updated!, targetUserId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Makes a participant a channel manager or a plain member (managers). Guests cannot manage.</summary>
    public async Task<ChannelDetailView> SetChannelManagerAsync(Guid userId, Guid channelId, Guid targetUserId, bool manager, CancellationToken cancellationToken)
    {
        await RequireManagedChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var channel = await RequireChannelEntityAsync(channelId, cancellationToken).ConfigureAwait(false);
            var target = await RequireActiveParticipantAsync(channelId, targetUserId, cancellationToken).ConfigureAwait(false);
            var role = manager ? ParticipantRole.Administrator : ParticipantRole.Member;
            updated = channel;
            if (target.Role == role)
            {
                return;
            }

            var now = time.GetUtcNow();
            target.ChangeRole(role);
            store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), channel, ConversationEventKind.RoleChanged, userId, targetUserId,
                role.ToString().ToLowerInvariant(), now));
        }, cancellationToken).ConfigureAwait(false);

        await NotifyParticipantsAsync(updated!, "members", [], cancellationToken).ConfigureAwait(false);
        return await GetChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops a team from adding its future members. People already in the channel stay.</summary>
    public async Task<ChannelDetailView> UnlinkTeamAsync(Guid userId, Guid channelId, Guid teamId, CancellationToken cancellationToken)
    {
        await RequireManagedChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
        var link = (await organizations.ListChannelTeamsAsync(channelId, cancellationToken).ConfigureAwait(false)).FirstOrDefault(l => l.TeamId == teamId)
            ?? throw new ResourceNotFoundException("team_not_found");
        organizations.Remove(link);
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        return await GetChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
    }

    // ---- Called by teams, workspaces and guests ----------------------------------------------------

    /// <summary>A new team member joins every channel the team is linked to (history from now).</summary>
    public async Task AddToTeamChannelsAsync(Guid actorUserId, Guid teamId, Guid userId, CancellationToken cancellationToken)
    {
        foreach (var link in await organizations.ListTeamChannelsAsync(teamId, cancellationToken).ConfigureAwait(false))
        {
            Conversation? updated = null;
            await MutateAsync(async () =>
            {
                var channel = await store.FindAsync(link.ConversationId, cancellationToken).ConfigureAwait(false);
                if (channel is { IsArchived: false } && await AddChannelParticipantAsync(channel, actorUserId, userId, asGuest: false, cancellationToken).ConfigureAwait(false))
                {
                    updated = channel;
                }
            }, cancellationToken).ConfigureAwait(false);

            if (updated is not null)
            {
                await NotifyParticipantsAsync(updated, "members", [], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Someone who left a workspace leaves its channels too (access would end anyway).</summary>
    public async Task RemoveFromWorkspaceChannelsAsync(Guid actorUserId, Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        await RemoveFromConversationsAsync(actorUserId,
            await store.ListActiveChannelIdsInWorkspaceAsync(workspaceId, userId, cancellationToken).ConfigureAwait(false), userId, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Adds an accepted guest to the channel they were invited to (history from now).</summary>
    public async Task AddGuestToChannelAsync(Guid inviterUserId, Guid channelId, Guid guestUserId, CancellationToken cancellationToken)
    {
        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var channel = await RequireChannelEntityAsync(channelId, cancellationToken).ConfigureAwait(false);
            await AddChannelParticipantAsync(channel, inviterUserId, guestUserId, asGuest: true, cancellationToken).ConfigureAwait(false);
            updated = channel;
        }, cancellationToken).ConfigureAwait(false);

        await NotifyParticipantsAsync(updated!, "members", [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A channel of the organization (callers check their own permission first).</summary>
    public async Task<Conversation> RequireOrganizationChannelAsync(Guid organizationId, Guid channelId, CancellationToken cancellationToken)
    {
        var channel = await store.FindAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (channel is not { Type: ConversationType.Channel } || channel.OrganizationId != organizationId)
        {
            throw new ResourceNotFoundException(ChannelNotFound);
        }

        return channel;
    }

    // ---- Helpers --------------------------------------------------------------------------------------

    private async Task<WorkspaceContext> RequireWorkspaceAsync(Guid userId, Guid organizationId, Guid workspaceId, CancellationToken cancellationToken)
    {
        var membership = await organizations.FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
        var organization = membership is { IsActive: true } ? await organizations.FindOrganizationAsync(organizationId, cancellationToken).ConfigureAwait(false) : null;
        var workspace = organization is null ? null : await organizations.FindWorkspaceAsync(organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        var workspaceMembership = workspace is null ? null : await organizations.FindWorkspaceMemberAsync(workspace.Id, userId, cancellationToken).ConfigureAwait(false);
        var orgCanManageWorkspaces = organization is not null && membership is not null
            && await CanManageOrganizationWorkspacesAsync(userId, organizationId, membership, cancellationToken).ConfigureAwait(false);
        if (organization is null || workspace is null
            || (workspaceMembership is not { IsActive: true } && !orgCanManageWorkspaces))
        {
            throw new ResourceNotFoundException("workspace_not_found");
        }

        var managesAll = orgCanManageWorkspaces || workspaceMembership is { IsActive: true, Role: WorkspaceRole.Administrator };
        return new WorkspaceContext(organization, membership!, workspace, workspaceMembership, managesAll);
    }

    /// <summary>The channel as the caller may see it; anything they may not see is "channel not found".</summary>
    private async Task<ChannelAccess> RequireChannelAsync(Guid userId, Guid channelId, CancellationToken cancellationToken)
    {
        var channel = await store.FindAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (channel is not { Type: ConversationType.Channel })
        {
            throw new ResourceNotFoundException(ChannelNotFound);
        }

        var participant = await store.FindParticipantAsync(channelId, userId, cancellationToken).ConfigureAwait(false);
        var me = participant is { IsActive: true } && await MayUseAsync(channel, participant, cancellationToken).ConfigureAwait(false) ? participant : null;
        var membership = await organizations.FindMemberAsync(channel.OrganizationId!.Value, userId, cancellationToken).ConfigureAwait(false);
        var workspaceMembership = membership is { IsActive: true }
            ? await organizations.FindWorkspaceMemberAsync(channel.WorkspaceId!.Value, userId, cancellationToken).ConfigureAwait(false)
            : null;
        var isWorkspaceMember = workspaceMembership is { IsActive: true };
        var canManageOrganizationWorkspaces = membership is { IsActive: true }
            && await CanManageOrganizationWorkspacesAsync(userId, channel.OrganizationId!.Value, membership, cancellationToken).ConfigureAwait(false);
        var legacyManagesAll = membership is { IsActive: true } && (canManageOrganizationWorkspaces
            || workspaceMembership is { IsActive: true, Role: WorkspaceRole.Administrator });
        var managePermissionConfigured = scopedAccess is not null
            && await scopedAccess.IsWorkspacePermissionConfiguredAsync(channel.WorkspaceId!.Value, "channels.manage", cancellationToken).ConfigureAwait(false);
        var managesAll = managePermissionConfigured
            ? await HasWorkspacePermissionAsync(userId, channel.WorkspaceId!.Value, "channels.manage", cancellationToken).ConfigureAwait(false)
            : legacyManagesAll;
        var canManage = managePermissionConfigured
            ? managesAll
            : legacyManagesAll || me is { IsGuest: false, Role: >= ParticipantRole.Administrator };
        var visible = me is not null || managesAll || (channel.Visibility == ChannelVisibility.Public && isWorkspaceMember);
        return visible ? new ChannelAccess(channel, me, canManage, isWorkspaceMember) : throw new ResourceNotFoundException(ChannelNotFound);
    }

    private async Task<ChannelAccess> RequireManagedChannelAsync(Guid userId, Guid channelId, CancellationToken cancellationToken)
    {
        var access = await RequireChannelAsync(userId, channelId, cancellationToken).ConfigureAwait(false);
        return access.CanManage ? access : throw new ForbiddenException();
    }

    private async Task<bool> HasWorkspacePermissionAsync(Guid userId, Guid workspaceId, string permissionKey, CancellationToken cancellationToken)
    {
        if (scopedAccess is null) return false;
        var permissions = await scopedAccess.GetWorkspacePermissionsAsync(userId, workspaceId, cancellationToken).ConfigureAwait(false);
        return permissions.Allowed.Contains(permissionKey, StringComparer.Ordinal)
            && !permissions.Denied.Contains(permissionKey, StringComparer.Ordinal);
    }

    private async Task<bool> CanManageOrganizationWorkspacesAsync(Guid userId, Guid organizationId, OrganizationMember membership, CancellationToken cancellationToken)
    {
        if (scopedAccess is null || !await scopedAccess.IsPermissionConfiguredAsync(organizationId, "organization.workspaces.manage", cancellationToken).ConfigureAwait(false))
        {
            return membership.Can(OrganizationPermission.ManageWorkspaces);
        }

        var permissions = await scopedAccess.GetOrganizationPermissionsAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        return permissions.Allowed.Contains("organization.workspaces.manage", StringComparer.Ordinal)
            && !permissions.Denied.Contains("organization.workspaces.manage", StringComparer.Ordinal);
    }

    private async Task<Conversation> RequireChannelEntityAsync(Guid channelId, CancellationToken cancellationToken) =>
        await store.FindAsync(channelId, cancellationToken).ConfigureAwait(false) is { Type: ConversationType.Channel } channel
            ? channel
            : throw new ResourceNotFoundException(ChannelNotFound);

    private async Task<IReadOnlyList<Team>> RequireTeamsAsync(Guid workspaceId, IReadOnlyCollection<Guid>? teamIds, CancellationToken cancellationToken)
    {
        var teams = new List<Team>();
        foreach (var id in (teamIds ?? []).Distinct())
        {
            var team = await organizations.FindTeamAsync(id, cancellationToken).ConfigureAwait(false);
            teams.Add(team is not null && team.WorkspaceId == workspaceId ? team : throw new RequestValidationException("teamIds", "team_not_found"));
        }

        return teams;
    }

    /// <summary>
    /// Explicit people must be active members of the organization and workspace and not blocked
    /// either way (one code for every reason); team members who no longer qualify are skipped.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveChannelMembersAsync(
        Guid userId, Guid organizationId, Guid workspaceId, IReadOnlyCollection<Guid>? memberIds, IReadOnlyList<Team> teams, CancellationToken cancellationToken)
    {
        var blocked = await contacts.ListBlockPartnersAsync(userId, cancellationToken).ConfigureAwait(false);
        async Task<bool> QualifiesAsync(Guid id) =>
            !blocked.Contains(id)
            && await organizations.FindMemberAsync(organizationId, id, cancellationToken).ConfigureAwait(false) is { IsActive: true }
            && await organizations.FindWorkspaceMemberAsync(workspaceId, id, cancellationToken).ConfigureAwait(false) is { IsActive: true };

        var people = new List<Guid>();
        foreach (var id in (memberIds ?? []).Distinct().Where(id => id != userId))
        {
            people.Add(await QualifiesAsync(id).ConfigureAwait(false) ? id : throw new RequestValidationException("userIds", "participant_not_allowed"));
        }

        foreach (var team in teams)
        {
            foreach (var member in await organizations.ListTeamMembersAsync(team.Id, cancellationToken).ConfigureAwait(false))
            {
                if (member.UserId != userId && !people.Contains(member.UserId) && await QualifiesAsync(member.UserId).ConfigureAwait(false))
                {
                    people.Add(member.UserId);
                }
            }
        }

        return people;
    }

    /// <summary>Adds (or brings back) a participant with history from now. False when they already take part.</summary>
    private async Task<bool> AddChannelParticipantAsync(Conversation channel, Guid actorUserId, Guid userId, bool asGuest, CancellationToken cancellationToken)
    {
        var previous = await store.FindParticipantAsync(channel.Id, userId, cancellationToken).ConfigureAwait(false);
        if (previous is { IsActive: true })
        {
            return false;
        }

        var now = time.GetUtcNow();
        var added = ConversationEvent.Record(Guid.CreateVersion7(now), channel, ConversationEventKind.MemberAdded, actorUserId, userId, null, now);
        store.Add(added);
        if (previous is not null)
        {
            previous.Rejoin(ParticipantRole.Member, added.Sequence, now, asGuest);
        }
        else
        {
            store.Add(asGuest
                ? ConversationParticipant.JoinGuest(Guid.CreateVersion7(now), channel.Id, userId, added.Sequence, now)
                : ConversationParticipant.Join(Guid.CreateVersion7(now), channel.Id, userId, ParticipantRole.Member, added.Sequence, now));
        }

        return true;
    }

    private async Task<ChannelDetailView> DetailAsync(ChannelAccess access, CancellationToken cancellationToken)
    {
        var channel = access.Channel;
        var participants = await store.ListActiveParticipantsAsync(channel.Id, cancellationToken).ConfigureAwait(false);
        var users = await directory.GetSummariesAsync(participants.Select(p => p.UserId).ToList(), cancellationToken).ConfigureAwait(false);
        var teams = new List<Team>();
        foreach (var link in await organizations.ListChannelTeamsAsync(channel.Id, cancellationToken).ConfigureAwait(false))
        {
            if (await organizations.FindTeamAsync(link.TeamId, cancellationToken).ConfigureAwait(false) is { } team)
            {
                teams.Add(team);
            }
        }

        var workspace = await organizations.FindWorkspaceAsync(channel.OrganizationId!.Value, channel.WorkspaceId!.Value, cancellationToken).ConfigureAwait(false);
        var view = new ChannelView(channel, workspace?.Name ?? string.Empty, participants.Count, access.Me, access.CanManage,
            teams.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
        return new ChannelDetailView(view, participants
            .Where(p => users.ContainsKey(p.UserId))
            .Select(p => new ParticipantView(p, users[p.UserId]))
            .OrderByDescending(p => p.Participant.Role)
            .ThenBy(p => p.User.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private async Task RemoveFromConversationsAsync(Guid actorUserId, IReadOnlyList<Guid> conversationIds, Guid userId, CancellationToken cancellationToken)
    {
        foreach (var conversationId in conversationIds)
        {
            Conversation? updated = null;
            await MutateAsync(async () =>
            {
                var conversation = await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
                var participant = await store.FindParticipantAsync(conversationId, userId, cancellationToken).ConfigureAwait(false);
                if (conversation is null || participant is not { IsActive: true })
                {
                    return;
                }

                var now = time.GetUtcNow();
                participant.Remove(now);
                var kind = actorUserId == userId ? ConversationEventKind.MemberLeft : ConversationEventKind.MemberRemoved;
                store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), conversation, kind, actorUserId,
                    kind == ConversationEventKind.MemberRemoved ? userId : null, null, now));
                updated = conversation;
            }, cancellationToken).ConfigureAwait(false);

            if (updated is not null)
            {
                await EndAccessAsync(updated, userId, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
