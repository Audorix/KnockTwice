using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Contacts;
using Audorix.ChatX.Application.Conversations;
using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Conversations;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Messaging;

/// <summary>Messaging policy (owner decisions of 2026-11-22) bound from the <c>Messaging</c> section.</summary>
public sealed class MessagingSettings
{
    public const string SectionName = "Messaging";

    /// <summary>Authors may edit their messages for this long after sending.</summary>
    public TimeSpan EditWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Maximum message length in characters (plain text).</summary>
    public int MaxLength { get; set; } = 4000;

    /// <summary>How often the outbox dispatcher looks for undelivered notifications.</summary>
    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Notifications not published right after commit become due after this delay; also the base retry delay.</summary>
    public TimeSpan OutboxRetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    public int OutboxMaxAttempts { get; set; } = 10;

    /// <summary>
    /// Phase 17 (owner decision): in groups and channels with more participants than this, only
    /// owners and administrators may use <c>@everyone</c> and <c>@here</c>.
    /// </summary>
    public int EveryoneMentionLimit { get; set; } = 20;

    /// <summary>Most people one message may mention by name.</summary>
    public int MaxMentions { get; set; } = 50;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (EveryoneMentionLimit is < 0 or > 100000 || MaxMentions is < 1 or > 1000)
        {
            errors.Add("Messaging:EveryoneMentionLimit must be 0-100000 and Messaging:MaxMentions 1-1000.");
        }

        if (EditWindow < TimeSpan.Zero || EditWindow > TimeSpan.FromDays(7))
        {
            errors.Add("Messaging:EditWindow must be between 0 and 7 days.");
        }

        if (MaxLength is < 1 or > 20000)
        {
            errors.Add("Messaging:MaxLength must be between 1 and 20000.");
        }

        if (OutboxPollInterval < TimeSpan.FromMilliseconds(50) || OutboxRetryDelay < TimeSpan.FromMilliseconds(50) || OutboxMaxAttempts is < 1 or > 100)
        {
            errors.Add("Messaging:OutboxPollInterval and OutboxRetryDelay must be at least 50 ms and OutboxMaxAttempts 1-100.");
        }

        return errors;
    }
}

/// <summary>
/// Transactional outbox row (Architecture §7.3): written in the same transaction as the message
/// change, published afterwards, deleted once published. Publication is at least once; clients
/// deduplicate by <see cref="Id"/> (the event id) and keep the highest message version.
/// </summary>
public sealed class OutboxMessage
{
    public const string MessageCreated = "message.created";
    public const string MessageUpdated = "message.updated";
    public const string MessageDeleted = "message.deleted";

    private OutboxMessage(Guid id, Guid conversationId, Guid entryId, string type, DateTimeOffset createdAtUtc, DateTimeOffset nextAttemptAtUtc)
    {
        Id = id;
        ConversationId = conversationId;
        EntryId = entryId;
        Type = type;
        CreatedAtUtc = createdAtUtc;
        NextAttemptAtUtc = nextAttemptAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid EntryId { get; private set; }

    public string Type { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public int Attempts { get; private set; }

    public static OutboxMessage Create(Guid id, ConversationEvent entry, string type, DateTimeOffset nowUtc, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(id, entry.ConversationId, entry.Id, type, nowUtc, nowUtc + delay);
    }

    public void Failed(DateTimeOffset nowUtc, TimeSpan baseDelay)
    {
        Attempts++;
        NextAttemptAtUtc = nowUtc + TimeSpan.FromTicks(baseDelay.Ticks * Math.Min(1L << Math.Min(Attempts, 10), 64));
    }
}

/// <summary>A message was created, edited or deleted. <see cref="EventId"/> is stable across redeliveries.</summary>
public sealed record MessageChanged(Guid EventId, string Type, HistoryEntryView Message) : IRealtimeEvent;

/// <summary>A message's reactions changed (Phase 16); carries the full new set.</summary>
public sealed record ReactionChanged(Guid ConversationId, Guid MessageId, IReadOnlyList<ReactionView> Reactions) : IRealtimeEvent;

/// <summary>A participant's delivery or read watermark moved (ADR-023).</summary>
/// <param name="Kind"><c>delivered</c> or <c>read</c>.</param>
public sealed record ReceiptChanged(Guid ConversationId, Guid UserId, string Kind, long Sequence) : IRealtimeEvent;

/// <summary>
/// Messaging use cases (Phase 6). Every operation re-checks conversation access; messages are
/// committed together with their outbox row before anything is published (ADR-012). Owner
/// decisions of 2026-11-22: authors edit within <see cref="MessagingSettings.EditWindow"/> and delete
/// any time (tombstone), group owners/administrators delete others' messages; receipts follow
/// ADR-023; client message ids are unique per conversation and sender for ever; plain text up to
/// <see cref="MessagingSettings.MaxLength"/> characters, with replies.
/// </summary>
public sealed class MessagingService(
    IConversationStore store,
    ConversationService conversations,
    IContactStore contacts,
    IRealtimePublisher publisher,
    TimeProvider time,
    IOptionsSnapshot<MessagingSettings> settings,
    Attachments.AttachmentService attachments,
    Attachments.IAttachmentStore attachmentStore,
    Users.IUserDirectory directory,
    PresenceDirectory presence)
{
    /// <summary>Phase 23: more tries (was 3) with a random back-off, so busy groups rarely see a 409.</summary>
    internal const int SaveAttempts = 8;

    /// <summary>
    /// Sends a message, optionally with ready attachments the sender uploaded into the conversation
    /// (then the text may be empty). Retrying with the same client message id returns the original
    /// (Created = false).
    /// </summary>
    public async Task<(HistoryEntryView Message, bool Created)> SendAsync(
        Guid userId, Guid conversationId, string? clientMessageId, string? body, Guid? replyToId, CancellationToken cancellationToken,
        IReadOnlyList<Guid>? attachmentIds = null, Guid? threadRootId = null, bool alsoInConversation = false, IReadOnlyList<Guid>? mentionUserIds = null,
        Guid? forwardedFromUserId = null)
    {
        var options = settings.Value;
        var key = clientMessageId?.Trim() ?? string.Empty;
        var files = attachmentIds ?? [];
        for (var attempt = 1; ; attempt++)
        {
            var (conversation, me) = await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
            if (key.Length > 0 && await store.FindMessageByClientIdAsync(conversationId, userId, key, cancellationToken).ConfigureAwait(false) is { } original)
            {
                var originalFiles = (await attachmentStore.ListForMessageAsync(original.Id, cancellationToken).ConfigureAwait(false)).Select(a => a.Id).ToHashSet();
                return original.IsSameSend(body ?? string.Empty, replyToId, options.MaxLength, files.Count > 0) && originalFiles.SetEquals(files)
                    ? (await ViewAsync(original, cancellationToken).ConfigureAwait(false), false)
                    : throw new ConflictException("idempotency_conflict");
            }

            await RequireNotBlockedAsync(userId, conversation, cancellationToken).ConfigureAwait(false);
            var replyTo = replyToId is { } target ? await RequireVisibleMessageAsync(userId, conversationId, target, cancellationToken).ConfigureAwait(false) : null;
            if (files.Count > 0)
            {
                await attachments.RequireLinkableAsync(userId, conversationId, files, cancellationToken).ConfigureAwait(false);
            }

            // Threads (Phase 16): not in website conversations (the visitor's chat has no threads).
            ConversationEvent? root = null;
            if (threadRootId is { } rootId)
            {
                if (conversation.Type == ConversationType.Website)
                {
                    throw new RequestValidationException("threadRootId", "threads_unavailable");
                }

                root = await conversations.RequireThreadRootAsync(conversationId, rootId, me, cancellationToken).ConfigureAwait(false);
            }

            // Phase 24 (owner decision: strict, gap-free order): hold the conversation's row while
            // taking the next position, so concurrent sends wait for each other instead of failing and
            // repeating every check above. Other conversations are not affected.
            ConversationEvent entry;
            OutboxMessage outbox;
            List<MessageMention> mentions;
            var saved = false;
            await using (var appendLock = await store.LockForAppendAsync(conversation, cancellationToken).ConfigureAwait(false))
            {
                var now = time.GetUtcNow();
                entry = ConversationEvent.Post(Guid.CreateVersion7(now), conversation, userId, key, body ?? string.Empty, replyTo, options.MaxLength, now,
                    allowEmptyBody: files.Count > 0);
                mentions = await ResolveMentionsAsync(conversation, me, entry, mentionUserIds, cancellationToken).ConfigureAwait(false);
                if (forwardedFromUserId is { } originalAuthor)
                {
                    entry.MarkForwarded(originalAuthor);
                }
                if (root is not null)
                {
                    entry.PlaceInThread(root, alsoInConversation);
                    await FollowThreadAsync(root, userId, entry.Sequence, now, cancellationToken).ConfigureAwait(false);
                    if (root.ActorUserId != userId)
                    {
                        await FollowThreadAsync(root, root.ActorUserId, null, now, cancellationToken).ConfigureAwait(false);
                    }
                }

                outbox = OutboxMessage.Create(Guid.CreateVersion7(now), entry, OutboxMessage.MessageCreated, now, options.OutboxRetryDelay);
                store.Add(entry);
                store.Add(outbox);
                foreach (var mention in mentions)
                {
                    store.Add(mention);
                }
                if (files.Count > 0)
                {
                    await attachments.LinkAsync(userId, entry, files, cancellationToken).ConfigureAwait(false);
                }

                // The sender has obviously read their own message; a thread reply moves only the thread's read position.
                if (entry.InMainTimeline)
                {
                    me.MarkRead(entry.Sequence, conversation.LastSequence);
                }

                if (await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
                {
                    await appendLock.CommitAsync(cancellationToken).ConfigureAwait(false);
                    saved = true;
                }
            }

            if (saved)
            {
                // Phase 23: describe the new message once and publish that same view, instead of
                // re-reading the outbox row and the message and describing it twice.
                var view = await conversations.DescribeNewAsync(entry, files.Count > 0, mentions.Count > 0, cancellationToken).ConfigureAwait(false);
                await PublishNowAsync(outbox, entry, view, cancellationToken).ConfigureAwait(false);
                return (view, true);
            }

            if (attempt == SaveAttempts)
            {
                throw new ConflictException("conflict");
            }

            // With the row held this is rare: the same client id was saved concurrently (or another writer
            // without the lock moved the position); re-evaluate after a short random pause.
            await Task.Delay(ConflictBackoff(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Phase 23: up to <see cref="SaveAttempts"/> tries, waiting 5–20 ms, then 10–40 ms, … (capped at 200 ms).</summary>
    internal static TimeSpan ConflictBackoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(200, 5 * (1 << Math.Min(attempt - 1, 5))) * (1 + (3 * Random.Shared.NextDouble())));

    /// <summary>
    /// Publishes a just-committed message with its view already built (the outbox row stays as the
    /// safety net if publishing fails, exactly like <see cref="DispatchAsync"/>).
    /// </summary>
    private async Task PublishNowAsync(OutboxMessage row, ConversationEvent entry, HistoryEntryView view, CancellationToken cancellationToken)
    {
        try
        {
            var recipients = await store.ListRecipientsAsync(row.ConversationId, entry.Sequence, cancellationToken).ConfigureAwait(false);
            if (recipients.Count > 0)
            {
                await publisher.PublishToUsersAsync(recipients, new MessageChanged(row.Id, row.Type, view), cancellationToken).ConfigureAwait(false);
            }

            store.Remove(row);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            row.Failed(time.GetUtcNow(), settings.Value.OutboxRetryDelay); // the dispatcher retries it
        }

        // A concurrent dispatcher may have removed the row first; that is fine (at least once).
        await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Forwards a message (Phase 18, owner decision: a copy with credit, no link back): the caller
    /// sends a copy of its text and files to another conversation they take part in, optionally
    /// preceded by their own comment. Not from or to website conversations. Retrying with the same
    /// client message id returns the copy already made.
    /// </summary>
    public async Task<(HistoryEntryView Message, bool Created)> ForwardAsync(
        Guid userId, Guid sourceConversationId, Guid messageId, Guid targetConversationId, string? clientMessageId, string? comment, CancellationToken cancellationToken)
    {
        var key = clientMessageId?.Trim() ?? string.Empty;
        if (key.Length is 0 or > ConversationEvent.ClientMessageIdMaxLength - 2)
        {
            throw new RequestValidationException("clientMessageId", "client_message_id_invalid");
        }

        var (source, _) = await conversations.RequireAccessAsync(userId, sourceConversationId, cancellationToken).ConfigureAwait(false);
        var message = await RequireMessageAsync(userId, sourceConversationId, messageId, cancellationToken).ConfigureAwait(false);
        var (target, _) = await conversations.RequireAccessAsync(userId, targetConversationId, cancellationToken).ConfigureAwait(false);
        if (source.Type == ConversationType.Website || target.Type == ConversationType.Website)
        {
            throw new RequestValidationException("targetConversationId", "forward_unavailable");
        }

        if (await store.FindMessageByClientIdAsync(targetConversationId, userId, key, cancellationToken).ConfigureAwait(false) is { } done)
        {
            return (await ViewAsync(done, cancellationToken).ConfigureAwait(false), false);
        }

        if (!string.IsNullOrWhiteSpace(comment))
        {
            await SendAsync(userId, targetConversationId, key + ":c", comment, null, cancellationToken).ConfigureAwait(false);
        }

        var files = await attachments.CopyForForwardAsync(userId, message.Id, targetConversationId, cancellationToken).ConfigureAwait(false);
        return await SendAsync(userId, targetConversationId, key, message.Body ?? string.Empty, null, cancellationToken, files,
            forwardedFromUserId: message.ActorUserId).ConfigureAwait(false);
    }

    public async Task<HistoryEntryView> EditAsync(Guid userId, Guid conversationId, Guid messageId, string? body, CancellationToken cancellationToken)
    {
        await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var entry = await RequireMessageAsync(userId, conversationId, messageId, cancellationToken).ConfigureAwait(false);
        var before = entry.Version;
        var now = time.GetUtcNow();
        var hasFiles = (await attachmentStore.ListForMessageAsync(entry.Id, cancellationToken).ConfigureAwait(false)).Count > 0;
        entry.Edit(userId, body ?? string.Empty, settings.Value.EditWindow, settings.Value.MaxLength, now, allowEmptyBody: hasFiles);
        if (entry.Version != before)
        {
            await SaveWithOutboxAsync(entry, OutboxMessage.MessageUpdated, now, cancellationToken).ConfigureAwait(false);
        }

        return await ViewAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Authors delete their own messages; group owners and administrators, and channel managers (Phase 11), also others'.</summary>
    public async Task DeleteAsync(Guid userId, Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        var (conversation, me) = await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var entry = await RequireMessageAsync(userId, conversationId, messageId, cancellationToken).ConfigureAwait(false);
        if (entry.ActorUserId != userId
            && !(conversation.Type is ConversationType.Group or ConversationType.Channel && me.Can(ConversationPermission.ManageMembers)))
        {
            throw new ForbiddenException();
        }

        var now = time.GetUtcNow();
        entry.Delete(userId, now);
        if (entry.ThreadRootId is { } rootId && await store.FindEntryAsync(conversationId, rootId, cancellationToken).ConfigureAwait(false) is { } root)
        {
            root.ThreadReplyRemoved();
        }

        // Reactions and mentions go with the message.
        foreach (var reaction in await store.ListReactionsAsync([entry.Id], cancellationToken).ConfigureAwait(false))
        {
            store.Remove(reaction);
        }

        foreach (var mention in await store.ListMentionsForEntryAsync(entry.Id, cancellationToken).ConfigureAwait(false))
        {
            store.Remove(mention);
        }

        // So do its pin and everyone's bookmarks (Phase 18).
        var (saved, pin) = await store.ListToolsForEntryAsync(entry.Id, cancellationToken).ConfigureAwait(false);
        foreach (var bookmark in saved)
        {
            store.Remove(bookmark);
        }

        if (pin is not null)
        {
            store.Remove(pin);
        }

        // Owner decision: deleting a message deletes its files. Metadata changes commit with the
        // tombstone; the stored bytes are removed afterwards (retried by the cleanup job).
        var files = await attachments.DeleteForMessageAsync(entry.Id, cancellationToken).ConfigureAwait(false);
        await SaveWithOutboxAsync(entry, OutboxMessage.MessageDeleted, now, cancellationToken).ConfigureAwait(false);
        await attachments.RemoveContentAndSaveAsync(files, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A device of the caller received everything up to <paramref name="sequence"/> (ADR-023 delivery acknowledgement).</summary>
    public async Task<long> MarkDeliveredAsync(Guid userId, Guid conversationId, long sequence, CancellationToken cancellationToken)
    {
        var (conversation, me) = await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (me.MarkDelivered(sequence, conversation.LastSequence))
        {
            if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new ConflictException("conflict");
            }

            await PublishReceiptAsync(conversationId, userId, "delivered", me.LastDeliveredSequence, cancellationToken).ConfigureAwait(false);
        }

        return me.LastDeliveredSequence;
    }

    /// <summary>Tells the other participants that a watermark moved.</summary>
    public async Task PublishReceiptAsync(Guid conversationId, Guid userId, string kind, long sequence, CancellationToken cancellationToken)
    {
        var recipients = await store.ListRecipientsAsync(conversationId, long.MaxValue, cancellationToken).ConfigureAwait(false);
        await publisher.PublishToUsersAsync(recipients, new ReceiptChanged(conversationId, userId, kind, sequence), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Publishes due outbox rows (called by the background dispatcher). Returns how many failed.</summary>
    public async Task<int> DispatchDueAsync(int limit, CancellationToken cancellationToken)
    {
        var failed = 0;
        foreach (var id in await store.ListDueOutboxAsync(time.GetUtcNow(), limit, cancellationToken).ConfigureAwait(false))
        {
            if (!await DispatchAsync(id, cancellationToken).ConfigureAwait(false))
            {
                failed++;
            }
        }

        return failed;
    }

    /// <summary>
    /// Publishes one outbox row to the participants who may see the entry <em>now</em> (stale
    /// membership never receives anything) and removes the row. Failures are retried with backoff.
    /// </summary>
    /// <returns>False when publishing failed (the row is kept for a retry, or dropped after the last attempt).</returns>
    public async Task<bool> DispatchAsync(Guid outboxId, CancellationToken cancellationToken)
    {
        var row = await store.FindOutboxAsync(outboxId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return true; // Already published by the other path.
        }

        var published = true;

        try
        {
            var entry = await store.FindEntryAsync(row.ConversationId, row.EntryId, cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                var recipients = await store.ListRecipientsAsync(row.ConversationId, entry.Sequence, cancellationToken).ConfigureAwait(false);
                if (recipients.Count > 0)
                {
                    var view = await ViewAsync(entry, cancellationToken).ConfigureAwait(false);
                    await publisher.PublishToUsersAsync(recipients, new MessageChanged(row.Id, row.Type, view), cancellationToken).ConfigureAwait(false);
                }
            }

            store.Remove(row);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Clients recover from history after the last attempt; the host logs failures.
            published = false;
            if (row.Attempts + 1 >= settings.Value.OutboxMaxAttempts)
            {
                store.Remove(row);
            }
            else
            {
                row.Failed(time.GetUtcNow(), settings.Value.OutboxRetryDelay);
            }
        }

        // A concurrent dispatcher may have removed the row first; that is fine (at least once).
        await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return published;
    }

    public async Task<HistoryEntryView> ViewAsync(ConversationEvent entry, CancellationToken cancellationToken) =>
        (await ViewsAsync([entry], cancellationToken).ConfigureAwait(false))[0];

    public async Task<IReadOnlyList<HistoryEntryView>> ViewsAsync(IReadOnlyList<ConversationEvent> entries, CancellationToken cancellationToken) =>
        await conversations.DescribeAsync(entries, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Adds or removes the caller's emoji reaction (Phase 16, owner decision: any emoji, at most 20
    /// different ones per message) and tells everyone who can see the message. Not in website
    /// conversations. Returns the message's reactions.
    /// </summary>
    public async Task<IReadOnlyList<ReactionView>> SetReactionAsync(
        Guid userId, Guid conversationId, Guid messageId, string? emoji, bool reacted, CancellationToken cancellationToken)
    {
        var (conversation, _) = await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation.Type == ConversationType.Website)
        {
            throw new RequestValidationException("emoji", "reactions_unavailable");
        }

        var entry = await RequireMessageAsync(userId, conversationId, messageId, cancellationToken).ConfigureAwait(false);
        string normalized;
        try
        {
            normalized = MessageReaction.NormalizeEmoji(emoji);
        }
        catch (DomainRuleException)
        {
            throw new RequestValidationException("emoji", "reaction_invalid");
        }

        var existing = await store.FindReactionAsync(entry.Id, userId, normalized, cancellationToken).ConfigureAwait(false);
        var current = await store.ListReactionsAsync([entry.Id], cancellationToken).ConfigureAwait(false);
        if (reacted && existing is null)
        {
            if (!current.Any(r => r.Emoji == normalized) && current.Select(r => r.Emoji).Distinct(StringComparer.Ordinal).Count() >= MessageReaction.MaxDistinctPerMessage)
            {
                throw new ConflictException("too_many_reactions");
            }

            store.Add(MessageReaction.Create(Guid.CreateVersion7(time.GetUtcNow()), entry, userId, normalized, time.GetUtcNow()));
        }
        else if (!reacted && existing is not null)
        {
            store.Remove(existing);
        }
        else
        {
            return Group(current);
        }

        // A concurrent identical change (double click, two devices) lost the race: the result is the same.
        await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var reactions = Group(await store.ListReactionsAsync([entry.Id], cancellationToken).ConfigureAwait(false));
        var recipients = await store.ListRecipientsAsync(conversationId, entry.Sequence, cancellationToken).ConfigureAwait(false);
        await publisher.PublishToUsersAsync(recipients, new ReactionChanged(conversationId, entry.Id, reactions), cancellationToken).ConfigureAwait(false);
        return reactions;
    }

    /// <summary>
    /// Mentions of a new message (Phase 17, ADR-031): the named people (who must take part in the
    /// conversation) and <c>@everyone</c> / <c>@here</c> written in the text, expanded to one row per
    /// recipient. Not in website or direct conversations' "everyone" sense: a direct conversation
    /// only has named mentions. Above <see cref="MessagingSettings.EveryoneMentionLimit"/>
    /// participants only owners and administrators may notify everyone.
    /// </summary>
    private async Task<List<MessageMention>> ResolveMentionsAsync(
        Conversation conversation, ConversationParticipant me, ConversationEvent entry, IReadOnlyList<Guid>? mentionUserIds, CancellationToken cancellationToken)
    {
        var named = (mentionUserIds ?? []).Where(id => id != me.UserId).Distinct().ToList();
        var (everyone, here) = MentionText.FindBroadcasts(entry.Body);
        if (conversation.Type == ConversationType.Website)
        {
            return named.Count == 0 ? [] : throw new RequestValidationException("mentionUserIds", "mentions_unavailable");
        }

        if (named.Count > settings.Value.MaxMentions)
        {
            throw new RequestValidationException("mentionUserIds", "too_many_mentions");
        }

        var broadcast = (everyone || here) && conversation.Type is ConversationType.Group or ConversationType.Channel;
        if (named.Count == 0 && !broadcast)
        {
            return [];
        }

        var participants = (await store.ListActiveParticipantsAsync(conversation.Id, cancellationToken).ConfigureAwait(false))
            .Where(p => !p.IsVisitor).ToList();
        var byUser = participants.ToDictionary(p => p.UserId);
        if (named.Any(id => !byUser.ContainsKey(id)))
        {
            throw new RequestValidationException("mentionUserIds", "mention_invalid");
        }

        if (broadcast && participants.Count > settings.Value.EveryoneMentionLimit && me.Role == ParticipantRole.Member)
        {
            throw new ForbiddenException("mention_everyone_forbidden");
        }

        var mentions = named.Select(id => MessageMention.Create(Guid.CreateVersion7(entry.CreatedAtUtc), entry, id, MentionKind.User)).ToList();
        if (broadcast)
        {
            var seen = named.ToHashSet();
            var others = participants.Where(p => p.UserId != me.UserId && !seen.Contains(p.UserId)).ToList();
            var online = everyone ? null : await presence.OnlineAmongAsync(others.Select(p => p.UserId).ToList(), cancellationToken).ConfigureAwait(false);
            foreach (var p in others)
            {
                if (everyone)
                {
                    mentions.Add(MessageMention.Create(Guid.CreateVersion7(entry.CreatedAtUtc), entry, p.UserId, MentionKind.Everyone));
                }
                else if (online!.Contains(p.UserId))
                {
                    mentions.Add(MessageMention.Create(Guid.CreateVersion7(entry.CreatedAtUtc), entry, p.UserId, MentionKind.Here));
                }
            }
        }

        return mentions;
    }

    private static List<ReactionView> Group(IReadOnlyList<MessageReaction> reactions) =>
        reactions.GroupBy(r => r.Emoji, StringComparer.Ordinal)
            .OrderBy(g => g.Min(r => r.CreatedAtUtc))
            .Select(g => new ReactionView(g.Key, g.OrderBy(r => r.CreatedAtUtc).Select(r => r.UserId).ToList()))
            .ToList();

    /// <summary>Makes <paramref name="userId"/> follow the thread; <paramref name="readUpTo"/> moves their thread read position.</summary>
    private async Task FollowThreadAsync(ConversationEvent root, Guid userId, long? readUpTo, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await store.FindThreadFollowAsync(root.Id, userId, cancellationToken).ConfigureAwait(false) is { } follow)
        {
            follow.SetFollowing(true);
            if (readUpTo is { } sequence)
            {
                follow.MarkRead(sequence);
            }

            return;
        }

        store.Add(ThreadFollow.Start(Guid.CreateVersion7(now), root, userId, readUpTo ?? root.Sequence, following: true, now));
    }

    private async Task SaveWithOutboxAsync(ConversationEvent entry, string type, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var outbox = OutboxMessage.Create(Guid.CreateVersion7(now), entry, type, now, settings.Value.OutboxRetryDelay);
        store.Add(outbox);
        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("conflict");
        }

        await DispatchAsync(outbox.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConversationEvent> RequireMessageAsync(Guid userId, Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        var entry = await RequireVisibleMessageAsync(userId, conversationId, messageId, cancellationToken).ConfigureAwait(false);
        return entry.IsDeleted ? throw new ResourceNotFoundException("message_not_found") : entry;
    }

    /// <summary>A message of this conversation inside the caller's visible history.</summary>
    private async Task<ConversationEvent> RequireVisibleMessageAsync(Guid userId, Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        var (_, me) = await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var entry = await store.FindEntryAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false);
        return entry is { IsMessage: true } && entry.Sequence >= me.VisibleFromSequence
            ? entry
            : throw new ResourceNotFoundException("message_not_found");
    }

    /// <summary>
    /// Direct conversations go quiet across a block (groups are unaffected, owner decision of Phase 5)
    /// and once the other person deleted their account (Phase 13): the history stays readable.
    /// </summary>
    private async Task RequireNotBlockedAsync(Guid userId, Conversation conversation, CancellationToken cancellationToken)
    {
        if (conversation.Type != ConversationType.Direct)
        {
            return;
        }

        var others = (await store.ListActiveParticipantsAsync(conversation.Id, cancellationToken).ConfigureAwait(false))
            .Where(p => p.UserId != userId).Select(p => p.UserId).ToList();
        var blocks = await contacts.ListBlockPartnersAsync(userId, cancellationToken).ConfigureAwait(false);
        if (others.Any(blocks.Contains))
        {
            throw new ForbiddenException("messaging_blocked");
        }

        var summaries = await directory.GetSummariesAsync(others, cancellationToken).ConfigureAwait(false);
        if (summaries.Values.Any(s => s.Deleted))
        {
            throw new ForbiddenException("recipient_deleted");
        }
    }
}
