using Audorix.KnockTwice.Api.Identity;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Conversations;
using Audorix.KnockTwice.Application.Organizations;
using Audorix.KnockTwice.Application.Widgets;
using Audorix.KnockTwice.Contracts.V1.Business;
using Audorix.KnockTwice.Domain.Conversations;
using Audorix.KnockTwice.Domain.Organizations;
using static Audorix.KnockTwice.Api.Endpoints.OrganizationEndpoints;

namespace Audorix.KnockTwice.Api.Endpoints;

/// <summary>
/// Business communication (Phase 11, ADR-026): channels inside workspaces, teams, guest access and
/// the support inbox. Messages, history, read state and attachments of channels use the regular
/// <c>/api/v1/conversations/{id}</c> routes (a channel is a conversation).
/// </summary>
internal static class BusinessEndpoints
{
    public static RouteGroupBuilder MapBusinessEndpoints(this RouteGroupBuilder v1)
    {
        MapChannels(v1);
        MapTeams(v1);
        MapGuests(v1);
        MapSupport(v1);
        return v1;
    }

    private static void MapChannels(RouteGroupBuilder v1)
    {
        var workspaceChannels = v1.MapGroup("/organizations/{organizationId:guid}/workspaces/{workspaceId:guid}/channels").RequireAuthorization().WithTags("Channels");
        workspaceChannels.MapGet(string.Empty, async (Guid organizationId, Guid workspaceId, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListChannelsAsync(http.User.RequireUserId(), organizationId, workspaceId, ct)).Select(ToResponse).ToList()))
            .WithSummary("Public channels of the workspace, the caller's private ones, and every channel for administrators.");
        workspaceChannels.MapPost(string.Empty, async (
            Guid organizationId, Guid workspaceId, CreateChannelRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            var created = await service.CreateChannelAsync(http.User.RequireUserId(), organizationId, workspaceId, request.Name, request.Description,
                ParseVisibility(request.Visibility) ?? ChannelVisibility.Public, request.UserIds, request.TeamIds, ct);
            var response = ToResponse(created);
            return TypedResults.Created($"/api/v1/channels/{response.Channel.Id}", response);
        }).WithSummary("Creates a channel; the creator becomes its manager. Optional people and teams (linked: later team members join too).");

        var channels = v1.MapGroup("/channels/{channelId:guid}").RequireAuthorization().WithTags("Channels");
        channels.MapGet(string.Empty, async (Guid channelId, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.GetChannelAsync(http.User.RequireUserId(), channelId, ct))));
        channels.MapPut(string.Empty, async (Guid channelId, UpdateChannelRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.UpdateChannelAsync(http.User.RequireUserId(), channelId, request.Name, request.Description,
                ParseVisibility(request.Visibility), ct))))
            .WithSummary("Renames, describes or changes the visibility of a channel (managers).");
        channels.MapPut("/archived", async (Guid channelId, SetChannelArchivedRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.SetChannelArchivedAsync(http.User.RequireUserId(), channelId, request.Archived, ct))))
            .WithSummary("Archives a channel (read-only: history stays, nobody can post, react, join or be added) or restores it (managers).");
        channels.MapPost("/join", async (Guid channelId, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.JoinChannelAsync(http.User.RequireUserId(), channelId, ct))))
            .WithSummary("Joins a public channel (workspace members). History starts now.");
        channels.MapPost("/leave", async (Guid channelId, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            await service.LeaveChannelAsync(http.User.RequireUserId(), channelId, ct);
            return TypedResults.NoContent();
        });
        channels.MapPost("/members", async (Guid channelId, AddChannelMembersRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.AddChannelMembersAsync(http.User.RequireUserId(), channelId, request.UserIds, request.TeamIds, ct))))
            .WithSummary("Adds workspace members and teams (managers). People already in the channel are skipped.");
        channels.MapDelete("/members/{userId:guid}", async (Guid channelId, Guid userId, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            await service.RemoveChannelMemberAsync(http.User.RequireUserId(), channelId, userId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Removes someone (managers), or leaves.");
        channels.MapPut("/members/{userId:guid}", async (
            Guid channelId, Guid userId, SetChannelManagerRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.SetChannelManagerAsync(http.User.RequireUserId(), channelId, userId, request.Manager, ct))))
            .WithSummary("Makes someone a channel manager or a plain member (managers). Guests cannot manage.");
        channels.MapDelete("/teams/{teamId:guid}", async (Guid channelId, Guid teamId, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.UnlinkTeamAsync(http.User.RequireUserId(), channelId, teamId, ct))))
            .WithSummary("Unlinks a team; people it added stay in the channel.");
    }

    private static void MapTeams(RouteGroupBuilder v1)
    {
        var teams = v1.MapGroup("/organizations/{organizationId:guid}/workspaces/{workspaceId:guid}/teams").RequireAuthorization().WithTags("Teams");
        teams.MapGet(string.Empty, async (Guid organizationId, Guid workspaceId, HttpContext http, TeamService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListAsync(http.User.RequireUserId(), organizationId, workspaceId, ct)).Select(ToResponse).ToList()));
        teams.MapPost(string.Empty, async (Guid organizationId, Guid workspaceId, CreateTeamRequest request, HttpContext http, TeamService service, CancellationToken ct) =>
        {
            var created = ToResponse(await service.CreateAsync(http.User.RequireUserId(), organizationId, workspaceId, request.Name, request.Description, ct));
            return TypedResults.Created($"/api/v1/organizations/{organizationId}/workspaces/{workspaceId}/teams/{created.Id}", created);
        }).WithSummary("Creates a team (workspace or organization administrators).");
        teams.MapPut("/{teamId:guid}", async (
            Guid organizationId, Guid workspaceId, Guid teamId, UpdateTeamRequest request, HttpContext http, TeamService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.UpdateAsync(http.User.RequireUserId(), organizationId, workspaceId, teamId, request.Name, request.Description, ct))));
        teams.MapDelete("/{teamId:guid}", async (Guid organizationId, Guid workspaceId, Guid teamId, HttpContext http, TeamService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(http.User.RequireUserId(), organizationId, workspaceId, teamId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Deletes a team (administrators). Channel members it added stay.");
        teams.MapGet("/{teamId:guid}/members", async (Guid organizationId, Guid workspaceId, Guid teamId, HttpContext http, TeamService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListMembersAsync(http.User.RequireUserId(), organizationId, workspaceId, teamId, ct)).Select(ToResponse).ToList()));
        teams.MapPost("/{teamId:guid}/members", async (
            Guid organizationId, Guid workspaceId, Guid teamId, AddTeamMemberRequest request, HttpContext http, TeamService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.AddMemberAsync(http.User.RequireUserId(), organizationId, workspaceId, teamId, request.UserId,
                ParseTeamRole(request.Role) ?? TeamRole.Member, ct))))
            .WithSummary("Adds a workspace member (team managers and administrators); they join the team's linked channels.");
        teams.MapPut("/{teamId:guid}/members/{userId:guid}", async (
            Guid organizationId, Guid workspaceId, Guid teamId, Guid userId, ChangeTeamRoleRequest request, HttpContext http, TeamService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.ChangeRoleAsync(http.User.RequireUserId(), organizationId, workspaceId, teamId, userId,
                ParseTeamRole(request.Role) ?? throw new RequestValidationException("role", "role_invalid"), ct))));
        teams.MapDelete("/{teamId:guid}/members/{userId:guid}", async (
            Guid organizationId, Guid workspaceId, Guid teamId, Guid userId, HttpContext http, TeamService service, CancellationToken ct) =>
        {
            await service.RemoveMemberAsync(http.User.RequireUserId(), organizationId, workspaceId, teamId, userId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Removes someone from the team (managers), or leaves. Their channels stay.");
    }

    private static void MapGuests(RouteGroupBuilder v1)
    {
        var guests = v1.MapGroup("/organizations/{organizationId:guid}/guests").RequireAuthorization().WithTags("Guests");
        guests.MapGet(string.Empty, async (Guid organizationId, HttpContext http, GuestService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListGuestsAsync(http.User.RequireUserId(), organizationId, ct)).Select(g => new GuestResponse(
                ToUser(g.User), g.Guest.JoinedAtUtc, g.Guest.AccessExpiresAtUtc,
                g.Channels.Select(c => new GuestChannelResponse(c.Id, c.Title ?? string.Empty)).ToList())).ToList()))
            .WithSummary("Active guests and their channels (owners and administrators).");
        guests.MapDelete("/{userId:guid}", async (Guid organizationId, Guid userId, HttpContext http, GuestService service, CancellationToken ct) =>
        {
            await service.RemoveGuestAsync(http.User.RequireUserId(), organizationId, userId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Ends a guest's access and removes them from all channels of the organization.");
        guests.MapGet("/invitations", async (Guid organizationId, HttpContext http, GuestService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListInvitationsAsync(http.User.RequireUserId(), organizationId, ct)).Select(i => new GuestInvitationResponse(
                i.Invitation.Id, i.Invitation.Email, i.Invitation.ConversationId, i.ChannelName, i.InvitedBy is null ? null : ToUser(i.InvitedBy),
                i.Invitation.CreatedAtUtc, i.Invitation.ExpiresAtUtc)).ToList()));
        guests.MapPost("/invitations", async (Guid organizationId, CreateGuestInvitationRequest request, HttpContext http, GuestService service, CancellationToken ct) =>
        {
            var invitation = await service.InviteAsync(http.User.RequireUserId(), organizationId, request.ChannelId, request.Email, ct);
            return TypedResults.Created($"/api/v1/organizations/{organizationId}/guests/invitations/{invitation.Id}",
                new GuestInvitationResponse(invitation.Id, invitation.Email, invitation.ConversationId, string.Empty, null, invitation.CreatedAtUtc, invitation.ExpiresAtUtc));
        }).WithSummary("Invites an email address into one channel as a guest (owners and administrators). Does not reveal whether the address has an account.");
        guests.MapDelete("/invitations/{invitationId:guid}", async (Guid organizationId, Guid invitationId, HttpContext http, GuestService service, CancellationToken ct) =>
        {
            await service.RevokeInvitationAsync(http.User.RequireUserId(), organizationId, invitationId, ct);
            return TypedResults.NoContent();
        });

        var mine = v1.MapGroup("/guest-invitations").RequireAuthorization().WithTags("Guests");
        mine.MapGet(string.Empty, async (HttpContext http, GuestService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListMineAsync(http.User.RequireUserId(), ct)).Select(i => new MyGuestInvitationResponse(
                i.Invitation.Id, i.Invitation.OrganizationId, i.OrganizationName, i.Invitation.ConversationId, i.ChannelName,
                i.InvitedBy is null ? null : ToUser(i.InvitedBy), i.Invitation.ExpiresAtUtc)).ToList()))
            .WithSummary("Open guest invitations addressed to the caller's confirmed email address.");
        mine.MapPost("/{invitationId:guid}/accept", async (Guid invitationId, HttpContext http, GuestService service, CancellationToken ct) =>
            TypedResults.Ok(new AcceptGuestInvitationResponse(await service.AcceptAsync(http.User.RequireUserId(), invitationId, ct))));
        mine.MapPost("/{invitationId:guid}/decline", async (Guid invitationId, HttpContext http, GuestService service, CancellationToken ct) =>
        {
            await service.DeclineAsync(http.User.RequireUserId(), invitationId, ct);
            return TypedResults.NoContent();
        });
    }

    private static void MapSupport(RouteGroupBuilder v1)
    {
        var support = v1.MapGroup("/organizations/{organizationId:guid}/support").RequireAuthorization().WithTags("Support");
        support.MapGet(string.Empty, async (
            Guid organizationId, string? status, bool? mine, HttpContext http, ConversationService service, IWidgetStore widgets, CancellationToken ct) =>
        {
            var items = await service.ListInboxAsync(http.User.RequireUserId(), organizationId, ParseStatus(status), mine == true, ct);
            var names = (await widgets.ListForOrganizationAsync(organizationId, ct)).ToDictionary(w => w.Id, w => w.Name);
            return TypedResults.Ok(items.Select(i => ToResponse(i, names)).ToList());
        }).WithSummary("Website conversations: all of them for support agents and administrators, the caller's own for responders. Filter by status (open/pending/closed) and mine=true.");

        support.MapGet("/inbox", async (
            Guid organizationId, string? status, bool? mine, string? assignment, string? q, string? sort, int? skip, int? take,
            HttpContext http, ConversationService service, IWidgetStore widgets, CancellationToken ct) =>
        {
            var oldest = sort?.Trim().ToLowerInvariant() switch
            {
                null or "" or "oldest" => true,
                "newest" => false,
                _ => throw new RequestValidationException("sort", "sort_invalid"),
            };
            bool? assigned = assignment?.Trim().ToLowerInvariant() switch
            {
                null or "" or "anyone" => null,
                "assigned" => true,
                "unassigned" => false,
                _ => throw new RequestValidationException("assignment", "value_invalid"),
            };
            var page = await service.ListInboxPageAsync(http.User.RequireUserId(), organizationId,
                new SupportInboxQuery(ParseStatus(status), mine == true, q, oldest, skip ?? 0, take ?? 25, assigned), ct);
            var names = (await widgets.ListForOrganizationAsync(organizationId, ct)).ToDictionary(w => w.Id, w => w.Name);
            return TypedResults.Ok(new SupportInboxPageResponse(
                page.Items.Select(i => ToResponse(i, names)).ToList(),
                page.Total,
                new SupportInboxCountsResponse(page.Counts.Open, page.Counts.Pending, page.Counts.Closed, page.Counts.All, page.Counts.UnassignedOpen)));
        }).WithSummary("One page of the support inbox: status (open/pending/closed/all), mine=true, assignment (anyone/assigned/unassigned), q (customer name or subject), sort (oldest: longest waiting first, or newest), skip and take (1-100). Includes totals per status and each conversation's newest message.");

        var conversation = v1.MapGroup("/support/conversations/{conversationId:guid}").RequireAuthorization().WithTags("Support");
        conversation.MapPost("/claim", async (Guid conversationId, HttpContext http, ConversationService service, IWidgetStore widgets, CancellationToken ct) =>
            TypedResults.Ok(await RespondAsync(await service.ClaimAsync(http.User.RequireUserId(), conversationId, ct), widgets, ct)))
            .WithSummary("Takes the conversation: assigns it to the caller, who joins it with the whole history.");
        conversation.MapPut("/assignee", async (
            Guid conversationId, AssignSupportConversationRequest request, HttpContext http, ConversationService service, IWidgetStore widgets, CancellationToken ct) =>
            TypedResults.Ok(await RespondAsync(await service.AssignAsync(http.User.RequireUserId(), conversationId, request.AssigneeId, ct), widgets, ct)))
            .WithSummary("Assigns to a support agent, administrator or taking-part responder, or unassigns with null (support agents and administrators).");
        conversation.MapPut("/status", async (
            Guid conversationId, SetSupportStatusRequest request, HttpContext http, ConversationService service, IWidgetStore widgets, CancellationToken ct) =>
            TypedResults.Ok(await RespondAsync(await service.SetSupportStatusAsync(http.User.RequireUserId(), conversationId,
                ParseStatus(request.Status) ?? throw new RequestValidationException("status", "value_invalid"), ct), widgets, ct)))
            .WithSummary("Sets the status: open, pending (waiting for the customer) or closed (resolved). A visitor's new message opens it again.");
        conversation.MapPut("/subject", async (
            Guid conversationId, SetSupportSubjectRequest request, HttpContext http, ConversationService service, IWidgetStore widgets, CancellationToken ct) =>
            TypedResults.Ok(await RespondAsync(await service.SetSupportSubjectAsync(http.User.RequireUserId(), conversationId, request.Subject, ct), widgets, ct)))
            .WithSummary("Sets or clears what the conversation is about (internal; the customer does not see it).");
        conversation.MapPost("/join", async (Guid conversationId, HttpContext http, ConversationService service, IWidgetStore widgets, CancellationToken ct) =>
            TypedResults.Ok(await RespondAsync(await service.JoinSupportAsync(http.User.RequireUserId(), conversationId, ct), widgets, ct)))
            .WithSummary("Joins the conversation (whole history) to reply, without changing who it is assigned to.");
        conversation.MapGet("/history", async (
            Guid conversationId, string? before, int? limit, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            var page = await service.GetSupportHistoryAsync(http.User.RequireUserId(), conversationId, ConversationEndpoints.DecodeCursor(before), limit, ct);
            return TypedResults.Ok(new Contracts.V1.Conversations.ConversationHistoryResponse(
                page.Items.Select(ConversationEndpoints.ToEntry).ToList(),
                page.NextBeforeSequence is { } next ? ConversationEndpoints.EncodeCursor(next) : null,
                page.HasMore));
        }).WithSummary("The whole history for support staff, whether or not they take part; same shape as the conversation history.");
        conversation.MapGet("/notes", async (Guid conversationId, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListSupportNotesAsync(http.User.RequireUserId(), conversationId, ct)).Select(ToResponse).ToList()))
            .WithSummary("Internal notes, oldest first. Never shown to the customer.");
        conversation.MapPost("/notes", async (
            Guid conversationId, CreateSupportNoteRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            var note = ToResponse(await service.AddSupportNoteAsync(http.User.RequireUserId(), conversationId, request.Body, ct));
            return TypedResults.Created($"/api/v1/support/conversations/{conversationId}/notes/{note.Id}", note);
        }).WithSummary("Adds an internal note for the support team.");
    }

    // ---- Mapping ---------------------------------------------------------------------------------

    private static ChannelResponse ToResponse(ChannelView view)
    {
        var c = view.Channel;
        return new ChannelResponse(c.Id, c.OrganizationId!.Value, c.WorkspaceId!.Value, view.WorkspaceName, c.Title ?? string.Empty, c.Description,
            c.Visibility == ChannelVisibility.Private ? "private" : "public", view.MemberCount, view.Me is not null, ChannelRole(view.Me), view.CanManage,
            view.Teams.Select(t => new TeamSummaryResponse(t.Id, t.Name)).ToList(), c.CreatedAtUtc, c.LastActivityAtUtc, c.ArchivedAtUtc);
    }

    private static ChannelDetailResponse ToResponse(ChannelDetailView view) => new(
        ToResponse(view.Channel),
        view.Participants.Select(p => new ChannelMemberResponse(ToUser(p.User), ChannelRole(p.Participant)!, p.Participant.JoinedAtUtc)).ToList());

    private static string? ChannelRole(ConversationParticipant? participant) => participant switch
    {
        null => null,
        { IsGuest: true } => "guest",
        { Role: >= ParticipantRole.Administrator } => "manager",
        _ => "member",
    };

    private static TeamResponse ToResponse(TeamView view) => new(
        view.Team.Id, view.Team.WorkspaceId, view.Team.Name, view.Team.Description, view.MemberCount, TeamRoleName(view.MyRole), view.CanManage, view.Team.CreatedAtUtc);

    private static TeamMemberResponse ToResponse(TeamMemberView view) => new(ToUser(view.User), TeamRoleName(view.Member.Role)!, view.Member.JoinedAtUtc);

    private static string? TeamRoleName(TeamRole? role) => role switch
    {
        TeamRole.Manager => "manager",
        TeamRole.Member => "member",
        _ => null,
    };

    private static SupportConversationResponse ToResponse(SupportItemView item, IReadOnlyDictionary<Guid, string> widgetNames)
    {
        var c = item.Conversation;
        return new SupportConversationResponse(c.Id, c.Title ?? string.Empty, c.WidgetId,
            c.WidgetId is { } w ? widgetNames.GetValueOrDefault(w) : null,
            StatusName(c.SupportStatus),
            item.Assignee is null ? null : ToUser(item.Assignee), item.Participating, item.UnreadCount, c.CreatedAtUtc, c.LastActivityAtUtc, c.Subject);
    }

    private static string StatusName(SupportStatus? status) => status switch
    {
        SupportStatus.Closed => "closed",
        SupportStatus.Pending => "pending",
        _ => "open",
    };

    private const int TitleLength = 120;

    private static SupportInboxItemResponse ToResponse(SupportInboxItem item, IReadOnlyDictionary<Guid, string> widgetNames)
    {
        var conversation = item.Item.Conversation;
        var title = conversation.Subject ?? FirstLine(item.FirstMessage?.Body);
        return new SupportInboxItemResponse(ToResponse(item.Item, widgetNames), title,
            item.LastMessage is { } last
                ? new SupportPreviewResponse(last.Body ?? string.Empty, last.ActorUserId == conversation.CreatedByUserId, last.CreatedAtUtc)
                : null);
    }

    private static string? FirstLine(string? text)
    {
        var line = text?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(line) ? null : line.Length <= TitleLength ? line : line[..(TitleLength - 1)] + "…";
    }

    private static SupportNoteResponse ToResponse(SupportNoteView view) =>
        new(view.Note.Id, view.Note.ConversationId, view.Author is null ? null : ToUser(view.Author), view.Note.Body, view.Note.CreatedAtUtc);

    private static async Task<SupportConversationResponse> RespondAsync(SupportItemView item, IWidgetStore widgets, CancellationToken ct)
    {
        var names = item.Conversation.OrganizationId is { } org
            ? (await widgets.ListForOrganizationAsync(org, ct)).ToDictionary(w => w.Id, w => w.Name)
            : new Dictionary<Guid, string>();
        return ToResponse(item, names);
    }

    private static ChannelVisibility? ParseVisibility(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "public" => ChannelVisibility.Public,
        "private" => ChannelVisibility.Private,
        _ => throw new RequestValidationException("visibility", "value_invalid"),
    };

    private static TeamRole? ParseTeamRole(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "manager" => TeamRole.Manager,
        "member" => TeamRole.Member,
        _ => throw new RequestValidationException("role", "role_invalid"),
    };

    private static SupportStatus? ParseStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "open" => SupportStatus.Open,
        "pending" => SupportStatus.Pending,
        "closed" or "resolved" => SupportStatus.Closed,
        _ => throw new RequestValidationException("status", "value_invalid"),
    };
}
