using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Conversations;
using Audorix.ChatX.Application.Messaging;
using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Conversations;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Planning;

/// <summary>Drafts, scheduled messages and reminders (Phase 20) bound from the <c>Planning</c> section.</summary>
public sealed class PlanningSettings
{
    public const string SectionName = "Planning";

    /// <summary>How often due scheduled messages and reminders are looked for.</summary>
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(15);

    public IReadOnlyList<string> Validate() =>
        DispatchInterval < TimeSpan.FromMilliseconds(100) || DispatchInterval > TimeSpan.FromMinutes(10)
            ? ["Planning:DispatchInterval must be between 100 ms and 10 minutes."]
            : [];
}

/// <summary>Persistence of drafts, scheduled messages and reminders (implemented by Infrastructure).</summary>
public interface IPlanningStore
{
    Task<MessageDraft?> FindDraftAsync(Guid userId, Guid conversationId, Guid threadKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<MessageDraft>> ListDraftsAsync(Guid userId, CancellationToken cancellationToken);

    Task DeleteDraftAsync(Guid userId, Guid conversationId, Guid threadKey, CancellationToken cancellationToken);

    Task<int> DeleteDraftsBeforeAsync(long updatedTicks, CancellationToken cancellationToken);

    void Add(MessageDraft draft);

    void Remove(MessageDraft draft);

    void Add(ScheduledMessage scheduled);

    void Add(ScheduledMessageFile file);

    void Remove(ScheduledMessage scheduled);

    Task<ScheduledMessage?> FindScheduledAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduledMessage>> ListScheduledAsync(Guid userId, CancellationToken cancellationToken);

    Task<int> CountPendingScheduledAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListDueScheduledAsync(long nowTicks, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListScheduledFilesAsync(Guid scheduledId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> CountScheduledFilesAsync(IReadOnlyCollection<Guid> scheduledIds, CancellationToken cancellationToken);

    void Add(Reminder reminder);

    void Remove(Reminder reminder);

    Task<Reminder?> FindReminderAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Reminder>> ListRemindersAsync(Guid userId, CancellationToken cancellationToken);

    Task<int> CountPendingRemindersAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListDueRemindersAsync(long nowTicks, int limit, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record DraftView(Guid ConversationId, Guid? ThreadRootId, string Body, Guid? ReplyToEntryId, DateTimeOffset UpdatedAtUtc);

public sealed record ScheduledView(ScheduledMessage Message, Conversation? Conversation, int FileCount);

public sealed record ReminderView(Reminder Reminder, Conversation Conversation, HistoryEntryView Entry);

/// <summary>Your drafts, scheduled messages or reminders changed (other devices refresh).</summary>
/// <param name="Kind"><c>drafts</c>, <c>scheduled</c> or <c>reminders</c>.</param>
public sealed record PlanningChanged(string Kind) : IRealtimeEvent;

/// <summary>A reminder came due.</summary>
public sealed record ReminderDue(Guid ReminderId, Guid ConversationId, Guid MessageId, Guid? ThreadRootId, string? AuthorName, string? Excerpt) : IRealtimeEvent;

/// <summary>
/// Drafts, scheduled messages and reminders (Phase 20, ADR-034; owner decisions of 2026-11-23):
/// drafts keep text and reply target per conversation and thread on the server; scheduled messages
/// go out through the normal send path at their time and, if that fails, stay listed with the
/// reason; reminders notify in the app, by push and by email.
/// </summary>
public sealed class PlanningService(
    IPlanningStore store,
    IConversationStore conversationStore,
    ConversationService conversations,
    MessagingService messaging,
    Attachments.AttachmentService attachments,
    Notifications.NotificationService notifications,
    IRealtimePublisher publisher,
    TimeProvider time,
    IOptionsSnapshot<MessagingSettings> messagingSettings)
{
    // ---- Drafts ----------------------------------------------------------------------------------

    /// <summary>Saves the caller's draft for a conversation (or one of its threads); an empty draft is removed.</summary>
    public async Task SaveDraftAsync(Guid userId, Guid conversationId, Guid? threadRootId, string? body, Guid? replyToEntryId, CancellationToken cancellationToken)
    {
        await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var key = threadRootId ?? Guid.Empty;
        var draft = await store.FindDraftAsync(userId, conversationId, key, cancellationToken).ConfigureAwait(false);
        var text = body ?? string.Empty;
        if (text.Trim().Length == 0 && replyToEntryId is null)
        {
            if (draft is not null)
            {
                store.Remove(draft);
                await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (draft is null)
        {
            draft = MessageDraft.Start(Guid.CreateVersion7(time.GetUtcNow()), userId, conversationId, threadRootId);
            store.Add(draft);
        }

        draft.Update(text, replyToEntryId, messagingSettings.Value.MaxLength, time.GetUtcNow());
        try
        {
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ApplicationRuleException)
        {
            // Two devices created the same draft at once: the other one won; the next save updates it.
            throw new ConflictException("conflict");
        }
    }

    public async Task<IReadOnlyList<DraftView>> ListDraftsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await store.ListDraftsAsync(userId, cancellationToken).ConfigureAwait(false))
            .Select(d => new DraftView(d.ConversationId, d.ThreadRootId, d.Body, d.ReplyToEntryId, new DateTimeOffset(d.UpdatedTicks, TimeSpan.Zero)))
            .ToList();

    // ---- Scheduled messages ----------------------------------------------------------------------

    public async Task<ScheduledView> ScheduleAsync(
        Guid userId, Guid conversationId, string? body, DateTimeOffset sendAtUtc, Guid? replyToEntryId, Guid? threadRootId, bool alsoInConversation,
        IReadOnlyList<Guid>? attachmentIds, IReadOnlyList<Guid>? mentionUserIds, CancellationToken cancellationToken)
    {
        var (conversation, _) = await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var files = attachmentIds ?? [];
        var text = ConversationEvent.NormalizeBody(body, messagingSettings.Value.MaxLength, allowEmpty: files.Count > 0);
        if (files.Count > 0)
        {
            await attachments.RequireLinkableAsync(userId, conversationId, files, cancellationToken).ConfigureAwait(false);
        }

        if (await store.CountPendingScheduledAsync(userId, cancellationToken).ConfigureAwait(false) >= ScheduledMessage.MaxPendingPerUser)
        {
            throw new ConflictException("too_many_scheduled");
        }

        var scheduled = ScheduledMessage.Create(Guid.CreateVersion7(time.GetUtcNow()), userId, conversationId, text, replyToEntryId, threadRootId, alsoInConversation,
            mentionUserIds ?? [], sendAtUtc.ToUniversalTime(), time.GetUtcNow());
        store.Add(scheduled);
        foreach (var file in files)
        {
            store.Add(ScheduledMessageFile.Of(scheduled, file));
        }

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ChangedAsync(userId, "scheduled", cancellationToken).ConfigureAwait(false);
        return new ScheduledView(scheduled, conversation, files.Count);
    }

    /// <summary>The caller's scheduled messages: waiting ones by time, then those that could not be sent.</summary>
    public async Task<IReadOnlyList<ScheduledView>> ListScheduledAsync(Guid userId, CancellationToken cancellationToken)
    {
        var items = await store.ListScheduledAsync(userId, cancellationToken).ConfigureAwait(false);
        var counts = await store.CountScheduledFilesAsync(items.Select(s => s.Id).ToList(), cancellationToken).ConfigureAwait(false);
        var views = new List<ScheduledView>();
        foreach (var item in items.OrderBy(s => s.Status).ThenBy(s => s.SendAtTicks))
        {
            views.Add(new ScheduledView(item, await conversationStore.FindAsync(item.ConversationId, cancellationToken).ConfigureAwait(false), counts.GetValueOrDefault(item.Id)));
        }

        return views;
    }

    public async Task<ScheduledView> UpdateScheduledAsync(Guid userId, Guid scheduledId, string? body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken)
    {
        var scheduled = await RequireOwnScheduledAsync(userId, scheduledId, cancellationToken).ConfigureAwait(false);
        var files = await store.ListScheduledFilesAsync(scheduled.Id, cancellationToken).ConfigureAwait(false);
        scheduled.Reschedule(ConversationEvent.NormalizeBody(body, messagingSettings.Value.MaxLength, allowEmpty: files.Count > 0), sendAtUtc.ToUniversalTime(), time.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ChangedAsync(userId, "scheduled", cancellationToken).ConfigureAwait(false);
        return new ScheduledView(scheduled, await conversationStore.FindAsync(scheduled.ConversationId, cancellationToken).ConfigureAwait(false), files.Count);
    }

    /// <summary>Cancels a waiting message or removes one that could not be sent. Its unsent files are cleaned up later.</summary>
    public async Task DeleteScheduledAsync(Guid userId, Guid scheduledId, CancellationToken cancellationToken)
    {
        store.Remove(await RequireOwnScheduledAsync(userId, scheduledId, cancellationToken).ConfigureAwait(false));
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ChangedAsync(userId, "scheduled", cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<Guid>> ListDueScheduledAsync(int limit, CancellationToken cancellationToken) =>
        store.ListDueScheduledAsync(time.GetUtcNow().UtcTicks, limit, cancellationToken);

    /// <summary>
    /// Sends one due scheduled message as its author through the normal send path. Returns null when
    /// sent (or already gone), otherwise the error code; the caller records a failure in a fresh scope.
    /// </summary>
    public async Task<string?> TrySendScheduledAsync(Guid scheduledId, CancellationToken cancellationToken)
    {
        var scheduled = await store.FindScheduledAsync(scheduledId, cancellationToken).ConfigureAwait(false);
        if (scheduled is not { Status: ScheduledMessageStatus.Pending } || scheduled.SendAtTicks > time.GetUtcNow().UtcTicks)
        {
            return null;
        }

        var files = await store.ListScheduledFilesAsync(scheduled.Id, cancellationToken).ConfigureAwait(false);
        try
        {
            await messaging.SendAsync(scheduled.UserId, scheduled.ConversationId, scheduled.ClientMessageId, scheduled.Body, scheduled.ReplyToEntryId, cancellationToken,
                files, scheduled.ThreadRootId, scheduled.AlsoInConversation, scheduled.MentionedUserIds).ConfigureAwait(false);
        }
        catch (ApplicationRuleException ex)
        {
            return ex.Code;
        }
        catch (DomainRuleException ex)
        {
            return ex.Code;
        }

        store.Remove(scheduled);
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ChangedAsync(scheduled.UserId, "scheduled", cancellationToken).ConfigureAwait(false);
        return null;
    }

    public async Task MarkScheduledFailedAsync(Guid scheduledId, string code, CancellationToken cancellationToken)
    {
        if (await store.FindScheduledAsync(scheduledId, cancellationToken).ConfigureAwait(false) is not { Status: ScheduledMessageStatus.Pending } scheduled)
        {
            return;
        }

        scheduled.Fail(code);
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ChangedAsync(scheduled.UserId, "scheduled", cancellationToken).ConfigureAwait(false);
    }

    private async Task<ScheduledMessage> RequireOwnScheduledAsync(Guid userId, Guid scheduledId, CancellationToken cancellationToken) =>
        await store.FindScheduledAsync(scheduledId, cancellationToken).ConfigureAwait(false) is { } scheduled && scheduled.UserId == userId
            ? scheduled
            : throw new ResourceNotFoundException("scheduled_not_found");

    // ---- Reminders -------------------------------------------------------------------------------

    public async Task<ReminderView> SetReminderAsync(Guid userId, Guid conversationId, Guid messageId, DateTimeOffset remindAtUtc, CancellationToken cancellationToken)
    {
        var (conversation, me) = await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var message = await conversationStore.FindEntryAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false);
        if (message is not { IsMessage: true, IsDeleted: false } || message.Sequence < me.VisibleFromSequence)
        {
            throw new ResourceNotFoundException("message_not_found");
        }

        if (await store.CountPendingRemindersAsync(userId, cancellationToken).ConfigureAwait(false) >= Reminder.MaxPendingPerUser)
        {
            throw new ConflictException("too_many_reminders");
        }

        var reminder = Reminder.Create(Guid.CreateVersion7(time.GetUtcNow()), message, userId, remindAtUtc.ToUniversalTime(), time.GetUtcNow());
        store.Add(reminder);
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ChangedAsync(userId, "reminders", cancellationToken).ConfigureAwait(false);
        var view = (await conversations.DescribeAsync([message], cancellationToken).ConfigureAwait(false))[0];
        return new ReminderView(reminder, conversation, view);
    }

    /// <summary>The caller's reminders on messages they can still read: due ones first, then by time.</summary>
    public async Task<IReadOnlyList<ReminderView>> ListRemindersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var reminders = await store.ListRemindersAsync(userId, cancellationToken).ConfigureAwait(false);
        var result = new List<ReminderView>();
        foreach (var reminder in reminders.OrderByDescending(r => r.Status).ThenBy(r => r.RemindAtTicks))
        {
            if (await ReadableAsync(userId, reminder, cancellationToken).ConfigureAwait(false) is { } readable)
            {
                result.Add(new ReminderView(reminder, readable.Conversation,
                    (await conversations.DescribeAsync([readable.Entry], cancellationToken).ConfigureAwait(false))[0]));
            }
        }

        return result;
    }

    /// <summary>Marks a reminder done (or cancels one not yet due).</summary>
    public async Task DeleteReminderAsync(Guid userId, Guid reminderId, CancellationToken cancellationToken)
    {
        if (await store.FindReminderAsync(reminderId, cancellationToken).ConfigureAwait(false) is not { } reminder || reminder.UserId != userId)
        {
            throw new ResourceNotFoundException("reminder_not_found");
        }

        store.Remove(reminder);
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ChangedAsync(userId, "reminders", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Notifies about reminders that came due; reminders on messages that can no longer be read are dropped.</summary>
    public async Task<int> DispatchDueRemindersAsync(int limit, CancellationToken cancellationToken)
    {
        var due = await store.ListDueRemindersAsync(time.GetUtcNow().UtcTicks, limit, cancellationToken).ConfigureAwait(false);
        var sent = 0;
        foreach (var id in due)
        {
            if (await store.FindReminderAsync(id, cancellationToken).ConfigureAwait(false) is not { Status: ReminderStatus.Pending } reminder)
            {
                continue;
            }

            var readable = await ReadableAsync(reminder.UserId, reminder, cancellationToken).ConfigureAwait(false);
            if (readable is null)
            {
                store.Remove(reminder);
                await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            reminder.BecameDue();
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var entry = readable.Entry;
            var author = (await conversations.DescribeAsync([entry], cancellationToken).ConfigureAwait(false))[0].Actor?.DisplayName;
            await publisher.PublishToUsersAsync([reminder.UserId],
                new ReminderDue(reminder.Id, reminder.ConversationId, entry.Id, entry.InMainTimeline ? null : entry.ThreadRootId, author, MessageText.Excerpt(entry.Body)),
                cancellationToken).ConfigureAwait(false);
            await notifications.NotifyReminderAsync(reminder.UserId, readable.Conversation, entry, cancellationToken).ConfigureAwait(false);
            sent++;
        }

        return sent;
    }

    public Task<int> RemoveOldDraftsAsync(CancellationToken cancellationToken) =>
        store.DeleteDraftsBeforeAsync(time.GetUtcNow().AddDays(-MessageDraft.MaxAgeDays).UtcTicks, cancellationToken);

    private sealed record Readable(Conversation Conversation, ConversationEvent Entry);

    private async Task<Readable?> ReadableAsync(Guid userId, Reminder reminder, CancellationToken cancellationToken)
    {
        try
        {
            var (conversation, me) = await conversations.RequireAccessAsync(userId, reminder.ConversationId, cancellationToken).ConfigureAwait(false);
            var entry = await conversationStore.FindEntryAsync(reminder.ConversationId, reminder.EntryId, cancellationToken).ConfigureAwait(false);
            return entry is { IsMessage: true, IsDeleted: false } && entry.Sequence >= me.VisibleFromSequence ? new Readable(conversation, entry) : null;
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }

    private Task ChangedAsync(Guid userId, string kind, CancellationToken cancellationToken) =>
        publisher.PublishToUsersAsync([userId], new PlanningChanged(kind), cancellationToken);
}
