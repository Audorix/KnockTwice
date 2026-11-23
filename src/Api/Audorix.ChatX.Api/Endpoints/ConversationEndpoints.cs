using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Conversations;
using Audorix.ChatX.Contracts.V1.Conversations;
using Audorix.ChatX.Domain.Conversations;
using Microsoft.AspNetCore.Http.HttpResults;
using static Audorix.ChatX.Api.Endpoints.OrganizationEndpoints;

namespace Audorix.ChatX.Api.Endpoints;

/// <summary><c>/api/v1/conversations</c>: direct and group conversations, participants, history and read state (Phase 5).</summary>
internal static class ConversationEndpoints
{
    private const string CursorPrefix = "s1:";

    public static RouteGroupBuilder MapConversationEndpoints(this RouteGroupBuilder v1)
    {
        var conversations = v1.MapGroup("/conversations").RequireAuthorization().WithTags("Conversations");

        conversations.MapGet(string.Empty, async (HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListAsync(http.User.RequireUserId(), ct)).Select(ToSummary).ToList()))
            .WithSummary("The caller's conversations, most recent activity first, with unread counts.");

        conversations.MapPost("/direct", async Task<Results<Ok<ConversationSummaryResponse>, Created<ConversationSummaryResponse>>> (
            OpenDirectConversationRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            var (view, created) = await service.OpenDirectAsync(http.User.RequireUserId(), request.UserId, request.OrganizationId, ct);
            var response = ToSummary(view);
            return created
                ? TypedResults.Created($"/api/v1/conversations/{response.Id}", response)
                : TypedResults.Ok(response);
        }).WithSummary("Opens the direct conversation with a contact (or, with organizationId, an organization member). Returns the existing one if it exists.");

        conversations.MapPost("/groups", async (CreateGroupConversationRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            var view = await service.CreateGroupAsync(http.User.RequireUserId(), request.Title, request.OrganizationId, request.UserIds, ct);
            return TypedResults.Created($"/api/v1/conversations/{view.Conversation.Id}", ToSummary(view));
        }).WithSummary("Creates a group. Personal groups may include the caller's contacts; organization groups its active members.");

        conversations.MapGet("/{conversationId:guid}", async (Guid conversationId, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToDetail(await service.GetAsync(http.User.RequireUserId(), conversationId, ct))));

        conversations.MapPut("/{conversationId:guid}", async (
            Guid conversationId, UpdateConversationRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToDetail(await service.UpdateAsync(http.User.RequireUserId(), conversationId, request.Title, request.Description, ct))))
            .WithSummary("Renames a group or changes its description (owners and administrators).");

        conversations.MapGet("/{conversationId:guid}/history", async (
            Guid conversationId, string? before, int? limit, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            var page = await service.GetHistoryAsync(http.User.RequireUserId(), conversationId, DecodeCursor(before), limit, ct);
            return TypedResults.Ok(new ConversationHistoryResponse(
                page.Items.Select(ToEntry).ToList(),
                page.NextBeforeSequence is { } next ? EncodeCursor(next) : null,
                page.HasMore));
        }).WithSummary("History visible to the caller (from their join), newest page first; items ascending. Pass nextCursor as 'before' for older entries.");

        conversations.MapPost("/{conversationId:guid}/read", async (
            Guid conversationId, MarkConversationReadRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            var unread = await service.MarkReadAsync(http.User.RequireUserId(), conversationId, request.Sequence, ct);
            var (_, me) = await service.RequireAccessAsync(http.User.RequireUserId(), conversationId, ct);
            return TypedResults.Ok(new ConversationReadResponse(me.LastReadSequence, unread));
        }).WithSummary("Moves the caller's read position forward (it never moves back).");

        conversations.MapPost("/{conversationId:guid}/delivered", async (
            Guid conversationId, MarkConversationDeliveredRequest request, HttpContext http, Application.Messaging.MessagingService messaging, CancellationToken ct) =>
            TypedResults.Ok(new ConversationDeliveredResponse(await messaging.MarkDeliveredAsync(http.User.RequireUserId(), conversationId, request.Sequence, ct))))
            .WithSummary("Delivery acknowledgement (ADR-023): a device of the caller received everything up to the sequence.");

        var messages = conversations.MapGroup("/{conversationId:guid}/messages").WithTags("Messages");
        messages.MapPost(string.Empty, async Task<Results<Ok<ConversationEntryResponse>, Created<ConversationEntryResponse>>> (
            Guid conversationId, SendMessageRequest request, HttpContext http, Application.Messaging.MessagingService messaging, CancellationToken ct) =>
        {
            var (message, created) = await messaging.SendAsync(
                http.User.RequireUserId(), conversationId, request.ClientMessageId, request.Body, request.ReplyToId, ct, request.AttachmentIds,
                request.ThreadRootId, request.AlsoInConversation, request.MentionUserIds);
            var response = ToEntry(message);
            return created
                ? TypedResults.Created($"/api/v1/conversations/{conversationId}/messages/{response.Id}", response)
                : TypedResults.Ok(response);
        }).WithSummary("Sends a plain-text message (optionally a reply, optionally with uploaded attachments). Idempotent by clientMessageId: a retry returns the original with 200.");
        messages.MapPut("/{messageId:guid}", async (
            Guid conversationId, Guid messageId, EditMessageRequest request, HttpContext http, Application.Messaging.MessagingService messaging, CancellationToken ct) =>
            TypedResults.Ok(ToEntry(await messaging.EditAsync(http.User.RequireUserId(), conversationId, messageId, request.Body, ct))))
            .WithSummary("Edits the caller's own message within the edit window (15 minutes by default).");
        messages.MapDelete("/{messageId:guid}", async (
            Guid conversationId, Guid messageId, HttpContext http, Application.Messaging.MessagingService messaging, CancellationToken ct) =>
        {
            await messaging.DeleteAsync(http.User.RequireUserId(), conversationId, messageId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Deletes a message (own messages; group owners and administrators also others'). A tombstone stays in history.");
        messages.MapPut("/{messageId:guid}/reactions", async (
            Guid conversationId, Guid messageId, SetReactionRequest request, HttpContext http, Application.Messaging.MessagingService messaging, CancellationToken ct) =>
        {
            var reactions = await messaging.SetReactionAsync(http.User.RequireUserId(), conversationId, messageId, request.Emoji, request.Reacted, ct);
            return TypedResults.Ok(reactions.Select(ToReaction).ToList());
        }).WithSummary("Adds (reacted: true) or removes the caller's emoji reaction; returns the message's reactions. Not in website chats; at most 20 distinct emoji per message.");
        messages.MapGet("/{messageId:guid}/thread", async (
            Guid conversationId, Guid messageId, string? before, int? limit, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            var page = await service.GetThreadAsync(http.User.RequireUserId(), conversationId, messageId, DecodeCursor(before), limit, ct);
            return TypedResults.Ok(new ThreadResponse(
                ToEntry(page.Root), page.Items.Select(ToEntry).ToList(),
                page.NextBeforeSequence is { } next ? EncodeCursor(next) : null, page.HasMore, page.Following, page.LastReadSequence));
        }).WithSummary("A thread: its first message and the newest page of replies (ascending). Pass nextCursor as 'before' for older replies.");
        messages.MapPost("/{messageId:guid}/thread/read", async (
            Guid conversationId, Guid messageId, MarkConversationReadRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(new ThreadReadResponse(await service.MarkThreadReadAsync(http.User.RequireUserId(), conversationId, messageId, request.Sequence, ct))))
            .WithSummary("Moves the caller's read position in the thread forward; returns the replies still unread.");
        messages.MapPut("/{messageId:guid}/thread/follow", async (
            Guid conversationId, Guid messageId, ThreadFollowRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(new ThreadFollowRequest(await service.SetThreadFollowingAsync(http.User.RequireUserId(), conversationId, messageId, request.Following, ct))))
            .WithSummary("Follows or stops following a thread. Followers are notified about its replies.");

        conversations.MapPut("/{conversationId:guid}/settings", async (
            Guid conversationId, ConversationSettingsRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToSummary(await service.SetNotificationLevelAsync(http.User.RequireUserId(), conversationId, ParseLevel(request), ct))))
            .WithSummary("The caller's own settings for the conversation: notification level all, mentions or nothing (muted: true is mentions).");

        v1.MapGet("/mentions", async (string? before, int? limit, HttpContext http, Application.Conversations.MentionService mentions, CancellationToken ct) =>
        {
            var page = await mentions.ListAsync(http.User.RequireUserId(), DecodeCursor(before), limit, ct);
            return TypedResults.Ok(new MentionPageResponse(
                page.Items.Select(i => new MentionItemResponse(i.Conversation.Id, TypeName(i.Conversation.Type), i.Conversation.Title, ToEntry(i.Entry), i.Unread)).ToList(),
                page.NextBefore is { } next ? EncodeCursor(next) : null,
                page.HasMore));
        }).RequireAuthorization().WithTags("Mentions")
            .WithSummary("Messages that mention the caller (by name, @everyone or @here), newest first, with their unread state.");

        // ---- Message tools (Phase 18) ------------------------------------------------------------
        conversations.MapGet("/{conversationId:guid}/pins", async (Guid conversationId, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListPinsAsync(http.User.RequireUserId(), conversationId, ct))
                .Select(p => new PinnedMessageResponse(ToEntry(p.Entry), p.PinnedBy is null ? null : ToUser(p.PinnedBy), p.PinnedAtUtc)).ToList()))
            .WithSummary("The conversation's pinned messages, most recently pinned first.");
        messages.MapPut("/{messageId:guid}/pin", async (
            Guid conversationId, Guid messageId, SetPinnedRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            await service.SetPinnedAsync(http.User.RequireUserId(), conversationId, messageId, request.Pinned, ct);
            return TypedResults.NoContent();
        }).WithSummary("Pins or unpins a message: either person in direct conversations, owners and administrators in groups and channels; at most 50 per conversation.");
        messages.MapPut("/{messageId:guid}/saved", async (
            Guid conversationId, Guid messageId, SetSavedRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            await service.SetSavedAsync(http.User.RequireUserId(), conversationId, messageId, request.Saved, ct);
            return TypedResults.NoContent();
        }).WithSummary("Saves (bookmarks) or unsaves a message for the caller.");
        messages.MapPost("/{messageId:guid}/forward", async Task<Results<Ok<ConversationEntryResponse>, Created<ConversationEntryResponse>>> (
            Guid conversationId, Guid messageId, ForwardMessageRequest request, HttpContext http, Application.Messaging.MessagingService messaging, CancellationToken ct) =>
        {
            var (copy, created) = await messaging.ForwardAsync(
                http.User.RequireUserId(), conversationId, messageId, request.TargetConversationId, request.ClientMessageId, request.Comment, ct);
            var response = ToEntry(copy);
            return created
                ? TypedResults.Created($"/api/v1/conversations/{request.TargetConversationId}/messages/{response.Id}", response)
                : TypedResults.Ok(response);
        }).WithSummary("Forwards a copy of the message (text and files, credited to its author) to another conversation of the caller, optionally after a comment.");
        v1.MapGet("/saved", async (string? before, int? limit, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            var page = await service.ListSavedAsync(http.User.RequireUserId(), DecodeCursor(before), limit, ct);
            return TypedResults.Ok(new SavedPageResponse(
                page.Items.Select(i => new SavedItemResponse(i.Conversation.Id, TypeName(i.Conversation.Type), i.Conversation.Title, ToEntry(i.Entry), i.SavedAtUtc)).ToList(),
                page.NextBefore is { } next ? EncodeCursor(next) : null,
                page.HasMore));
        }).RequireAuthorization().WithTags("Saved")
            .WithSummary("The caller's saved messages they can still read, most recently saved first.");

        v1.MapGet("/mentions/unread", async (HttpContext http, Application.Conversations.MentionService mentions, CancellationToken ct) =>
            TypedResults.Ok(new MentionCountResponse(await mentions.CountUnreadAsync(http.User.RequireUserId(), ct))))
            .RequireAuthorization().WithTags("Mentions")
            .WithSummary("How many mentions of the caller are in unread messages.");

        conversations.MapPost("/{conversationId:guid}/participants", async (
            Guid conversationId, AddParticipantsRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToDetail(await service.AddParticipantsAsync(http.User.RequireUserId(), conversationId, request.UserIds, ct))))
            .WithSummary("Adds people to a group (owners and administrators). Their history starts now.");

        conversations.MapDelete("/{conversationId:guid}/participants/{userId:guid}", async (
            Guid conversationId, Guid userId, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            await service.RemoveParticipantAsync(http.User.RequireUserId(), conversationId, userId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Removes someone from a group (administrators remove members, owners anyone). Removing yourself leaves.");

        conversations.MapPut("/{conversationId:guid}/participants/{userId:guid}", async (
            Guid conversationId, Guid userId, ChangeParticipantRoleRequest request, HttpContext http, ConversationService service, CancellationToken ct) =>
            TypedResults.Ok(ToDetail(await service.ChangeRoleAsync(http.User.RequireUserId(), conversationId, userId, ParseRole(request.Role), ct))))
            .WithSummary("Changes a participant's role (owners). The last owner cannot be demoted.");

        conversations.MapPost("/{conversationId:guid}/leave", async (Guid conversationId, HttpContext http, ConversationService service, CancellationToken ct) =>
        {
            await service.LeaveAsync(http.User.RequireUserId(), conversationId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Leaves a group; access ends immediately. If the last owner leaves, the longest-standing member becomes owner.");

        return v1;
    }

    internal static ConversationSummaryResponse ToSummary(ConversationSummaryView view)
    {
        var c = view.Conversation;
        var title = c.Type == ConversationType.Direct ? view.Counterpart?.DisplayName ?? string.Empty : c.Title ?? string.Empty;
        return new ConversationSummaryResponse(
            c.Id,
            TypeName(c.Type),
            c.Scope == ConversationScope.Personal ? "personal" : "organization",
            c.OrganizationId,
            view.OrganizationName,
            title,
            view.Counterpart is null ? null : ToUser(view.Counterpart),
            view.Me.IsGuest ? "guest" : RoleName(view.Me.Role),
            view.Me.Muted,
            c.LastSequence,
            view.Me.LastReadSequence,
            view.UnreadCount,
            c.LastActivityAtUtc,
            c.WorkspaceId,
            c.Visibility switch { ChannelVisibility.Public => "public", ChannelVisibility.Private => "private", _ => null },
            c.SupportStatus switch { SupportStatus.Open => "open", SupportStatus.Closed => "closed", _ => null },
            NotificationLevel: LevelName(view.Me.NotificationLevel));
    }

    internal static string LevelName(NotificationLevel level) => level switch
    {
        NotificationLevel.Mentions => "mentions",
        NotificationLevel.Nothing => "nothing",
        _ => "all",
    };

    private static NotificationLevel ParseLevel(ConversationSettingsRequest request) => request.NotificationLevel?.Trim().ToUpperInvariant() switch
    {
        "ALL" => NotificationLevel.All,
        "MENTIONS" => NotificationLevel.Mentions,
        "NOTHING" => NotificationLevel.Nothing,
        null when request.Muted is { } muted => muted ? NotificationLevel.Mentions : NotificationLevel.All,
        _ => throw new RequestValidationException("notificationLevel", "notification_level_invalid"),
    };

    internal static string TypeName(ConversationType type) => type switch
    {
        ConversationType.Direct => "direct",
        ConversationType.Website => "website",
        ConversationType.Channel => "channel",
        _ => "group",
    };

    private static ConversationDetailResponse ToDetail(ConversationDetailView view) => new(
        ToSummary(view.Summary),
        view.Summary.Conversation.Description,
        view.Participants.Select(p => new ConversationParticipantResponse(
            ToUser(p.User), p.Participant.IsGuest ? "guest" : RoleName(p.Participant.Role), p.Participant.JoinedAtUtc,
            p.Participant.LastDeliveredSequence, p.Participant.LastReadSequence)).ToList());

    internal static ConversationEntryResponse ToEntry(HistoryEntryView view) => new(
        view.Entry.Id,
        view.Entry.Sequence,
        KindName(view.Entry.Kind),
        view.Actor is null ? null : ToUser(view.Actor),
        view.Subject is null ? null : ToUser(view.Subject),
        view.Entry.Text,
        view.Entry.CreatedAtUtc,
        view.Entry.Body,
        view.ReplyTo is { } r ? new ReplyPreviewResponse(r.Id, r.Sequence, r.Author is null ? null : ToUser(r.Author), r.Excerpt, r.Deleted) : null,
        view.Entry.ClientMessageId,
        view.Entry.Version,
        view.Entry.EditedAtUtc,
        view.Entry.DeletedAtUtc,
        view.Attachments is { Count: > 0 } files ? files.Select(AttachmentEndpoints.ToMessageAttachment).ToList() : null,
        view.Reactions is { Count: > 0 } reactions ? reactions.Select(ToReaction).ToList() : null,
        view.Entry.ThreadRootId,
        view.Entry.ShownInConversation,
        view.Thread is { } t ? new ThreadSummaryResponse(t.ReplyCount, t.LastReplyAtUtc, t.Following, t.Unread) : null,
        view.Mentions is { } m ? new MentionsResponse(m.UserIds, m.Everyone, m.Here) : null,
        view.Pinned,
        view.ForwardedFrom is null ? null : ToUser(view.ForwardedFrom),
        view.Saved);

    internal static ReactionResponse ToReaction(ReactionView view) => new(view.Emoji, view.UserIds.Count, view.UserIds);

    private static string RoleName(ParticipantRole role) => role switch
    {
        ParticipantRole.Owner => "owner",
        ParticipantRole.Administrator => "administrator",
        _ => "member",
    };

    private static ParticipantRole ParseRole(string? role) => role?.Trim().ToUpperInvariant() switch
    {
        "OWNER" => ParticipantRole.Owner,
        "ADMINISTRATOR" => ParticipantRole.Administrator,
        "MEMBER" => ParticipantRole.Member,
        _ => throw new RequestValidationException("role", "role_invalid"),
    };

    private static string KindName(ConversationEventKind kind) => kind switch
    {
        ConversationEventKind.Created => "created",
        ConversationEventKind.MemberAdded => "member_added",
        ConversationEventKind.MemberRemoved => "member_removed",
        ConversationEventKind.MemberLeft => "member_left",
        ConversationEventKind.Renamed => "renamed",
        ConversationEventKind.DescriptionChanged => "description_changed",
        ConversationEventKind.RoleChanged => "role_changed",
        ConversationEventKind.Message => "message",
        ConversationEventKind.Assigned => "assigned",
        ConversationEventKind.StatusChanged => "status_changed",
        ConversationEventKind.VisibilityChanged => "visibility_changed",
        ConversationEventKind.Pinned => "pinned",
        _ => "unknown",
    };

    // Opaque cursors: clients must not build or interpret them (ADR-022 sequence inside).
    internal static string EncodeCursor(long sequence) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(CursorPrefix + sequence.ToString(CultureInfo.InvariantCulture)));

    internal static long? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        try
        {
            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            if (text.StartsWith(CursorPrefix, StringComparison.Ordinal)
                && long.TryParse(text.AsSpan(CursorPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && sequence > 0)
            {
                return sequence;
            }
        }
        catch (FormatException)
        {
        }

        throw new RequestValidationException("before", "cursor_invalid");
    }
}
