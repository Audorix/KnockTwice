using Audorix.KnockTwice.Application.Conversations;
using Audorix.KnockTwice.Application.Messaging;
using Audorix.KnockTwice.Application.Realtime;
using Audorix.KnockTwice.Domain.Conversations;
using Audorix.KnockTwice.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

internal sealed class EfConversationStore(ChatXDbContext db, TimeProvider time) : IConversationStore
{
    public void Add(Conversation conversation) => db.Conversations.Add(conversation);

    public void Add(ConversationParticipant participant) => db.ConversationParticipants.Add(participant);

    public void Add(ConversationEvent entry) => db.ConversationEvents.Add(entry);

    public Task<Conversation?> FindAsync(Guid conversationId, CancellationToken cancellationToken) =>
        db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public Task<Conversation?> FindDirectAsync(string directKey, CancellationToken cancellationToken) =>
        db.Conversations.FirstOrDefaultAsync(c => c.DirectKey == directKey, cancellationToken);

    public Task<ConversationParticipant?> FindParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken) =>
        db.ConversationParticipants.FirstOrDefaultAsync(p => p.ConversationId == conversationId && p.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<ConversationParticipant>> ListActiveParticipantsAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizationId = await db.Conversations.Where(c => c.Id == conversationId).Select(c => c.OrganizationId)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var activeGuestIds = organizationId is { } orgId
            ? await db.OrganizationGuests.AsNoTracking().Where(g => g.OrganizationId == orgId && g.Status == MembershipStatus.Active && g.AccessExpiresAtUtc > now)
                .Select(g => g.UserId).ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];
        return await db.ConversationParticipants
            .Where(p => p.ConversationId == conversationId && p.Status == ParticipantStatus.Active && (!p.IsGuest || activeGuestIds.Contains(p.UserId)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Participation>> ListForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await (
            from p in db.ConversationParticipants.AsNoTracking()
            where p.UserId == userId && p.Status == ParticipantStatus.Active
            join c in db.Conversations.AsNoTracking() on p.ConversationId equals c.Id
            select new { Conversation = c, Participant = p }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Conversation.Id).ToList();

        // Unread: entries after the read position written by someone else (visible history starts
        // at or before read position + 1 by construction).
        var unread = await (
            from e in db.ConversationEvents
            join p in db.ConversationParticipants on e.ConversationId equals p.ConversationId
            where p.UserId == userId && p.Status == ParticipantStatus.Active && ids.Contains(e.ConversationId)
                  && e.Sequence > p.LastReadSequence && e.ActorUserId != userId && e.DeletedAtUtc == null
                  && (e.ThreadRootId == null || e.ShownInConversation)
            group e by e.ConversationId into g
            select new { ConversationId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.ConversationId, x => x.Count, cancellationToken).ConfigureAwait(false);

        var directIds = rows.Where(r => r.Conversation.Type == ConversationType.Direct).Select(r => r.Conversation.Id).ToList();
        var counterparts = directIds.Count == 0
            ? []
            : await db.ConversationParticipants.AsNoTracking()
                .Where(p => directIds.Contains(p.ConversationId) && p.UserId != userId && p.Status == ParticipantStatus.Active)
                .ToDictionaryAsync(p => p.ConversationId, p => p.UserId, cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r => new Participation(
                r.Conversation,
                r.Participant,
                unread.GetValueOrDefault(r.Conversation.Id),
                counterparts.TryGetValue(r.Conversation.Id, out var other) ? other : null))
            .ToList();
    }

    public Task<int> CountUnreadAsync(Guid conversationId, Guid userId, long afterSequence, CancellationToken cancellationToken) =>
        db.ConversationEvents.CountAsync(e => e.ConversationId == conversationId && e.Sequence > afterSequence && e.ActorUserId != userId && e.DeletedAtUtc == null
            && (e.ThreadRootId == null || e.ShownInConversation), cancellationToken);

    /// <summary>The main timeline: thread replies (Phase 16) appear only when they were also sent to the conversation.</summary>
    public async Task<IReadOnlyList<ConversationEvent>> ListHistoryAsync(
        Guid conversationId, long fromSequence, long beforeSequence, int limit, CancellationToken cancellationToken) =>
        await db.ConversationEvents.AsNoTracking()
            .Where(e => e.ConversationId == conversationId && e.Sequence >= fromSequence && e.Sequence < beforeSequence
                        && (e.ThreadRootId == null || e.ShownInConversation))
            .OrderByDescending(e => e.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ConversationEvent>> ListHistoryAfterAsync(
        Guid conversationId, long afterSequence, int limit, CancellationToken cancellationToken) =>
        await db.ConversationEvents.AsNoTracking()
            .Where(e => e.ConversationId == conversationId && e.Sequence > afterSequence
                        && (e.ThreadRootId == null || e.ShownInConversation))
            .OrderBy(e => e.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    // ---- Reactions and threads (Phase 16) --------------------------------------------------------

    public void Add(MessageReaction reaction) => db.MessageReactions.Add(reaction);

    public void Remove(MessageReaction reaction) => db.MessageReactions.Remove(reaction);

    public Task<MessageReaction?> FindReactionAsync(Guid entryId, Guid userId, string emoji, CancellationToken cancellationToken) =>
        db.MessageReactions.FirstOrDefaultAsync(r => r.EntryId == entryId && r.UserId == userId && r.Emoji == emoji, cancellationToken);

    public async Task<IReadOnlyList<MessageReaction>> ListReactionsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken) =>
        entryIds.Count == 0
            ? []
            : await db.MessageReactions.AsNoTracking().Where(r => entryIds.Contains(r.EntryId)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(ThreadFollow follow) => db.ThreadFollows.Add(follow);

    public Task<ThreadFollow?> FindThreadFollowAsync(Guid rootId, Guid userId, CancellationToken cancellationToken) =>
        db.ThreadFollows.FirstOrDefaultAsync(f => f.RootId == rootId && f.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<ThreadFollow>> ListThreadFollowsAsync(Guid userId, IReadOnlyCollection<Guid> rootIds, CancellationToken cancellationToken) =>
        rootIds.Count == 0
            ? []
            : await db.ThreadFollows.AsNoTracking().Where(f => f.UserId == userId && rootIds.Contains(f.RootId)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Guid>> ListThreadFollowerIdsAsync(Guid rootId, CancellationToken cancellationToken) =>
        await db.ThreadFollows.AsNoTracking().Where(f => f.RootId == rootId && f.Following).Select(f => f.UserId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, int>> CountThreadUnreadAsync(Guid userId, IReadOnlyCollection<ThreadFollow> follows, CancellationToken cancellationToken)
    {
        if (follows.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var rootIds = follows.Select(f => f.RootId).ToList();
        var replies = await db.ConversationEvents.AsNoTracking()
            .Where(e => e.ThreadRootId != null && rootIds.Contains(e.ThreadRootId.Value) && e.ActorUserId != userId && e.DeletedAtUtc == null)
            .Select(e => new { Root = e.ThreadRootId!.Value, e.Sequence }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var read = follows.ToDictionary(f => f.RootId, f => f.LastReadSequence);
        return replies.Where(r => r.Sequence > read[r.Root]).GroupBy(r => r.Root).ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task<IReadOnlyList<ConversationEvent>> ListThreadAsync(
        Guid conversationId, Guid rootId, long fromSequence, long beforeSequence, int limit, CancellationToken cancellationToken) =>
        await db.ConversationEvents.AsNoTracking()
            .Where(e => e.ConversationId == conversationId && e.ThreadRootId == rootId && e.Sequence >= fromSequence && e.Sequence < beforeSequence)
            .OrderByDescending(e => e.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    // ---- Message tools (Phase 18) -----------------------------------------------------------------

    public void Add(MessagePin pin) => db.MessagePins.Add(pin);

    public void Remove(MessagePin pin) => db.MessagePins.Remove(pin);

    public Task<MessagePin?> FindPinAsync(Guid entryId, CancellationToken cancellationToken) =>
        db.MessagePins.FirstOrDefaultAsync(p => p.EntryId == entryId, cancellationToken);

    public Task<int> CountPinsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        db.MessagePins.CountAsync(p => p.ConversationId == conversationId, cancellationToken);

    public async Task<IReadOnlyList<MessagePin>> ListPinsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await db.MessagePins.AsNoTracking().Where(p => p.ConversationId == conversationId).OrderByDescending(p => p.PinnedTicks)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Guid>> ListPinnedEntryIdsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken) =>
        entryIds.Count == 0
            ? []
            : await db.MessagePins.AsNoTracking().Where(p => entryIds.Contains(p.EntryId)).Select(p => p.EntryId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(SavedMessage saved) => db.SavedMessages.Add(saved);

    public void Remove(SavedMessage saved) => db.SavedMessages.Remove(saved);

    public Task<SavedMessage?> FindSavedAsync(Guid userId, Guid entryId, CancellationToken cancellationToken) =>
        db.SavedMessages.FirstOrDefaultAsync(s => s.UserId == userId && s.EntryId == entryId, cancellationToken);

    public Task<int> CountSavedAsync(Guid userId, CancellationToken cancellationToken) =>
        db.SavedMessages.CountAsync(s => s.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListSavedEntryIdsAsync(Guid userId, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken) =>
        entryIds.Count == 0
            ? []
            : await db.SavedMessages.AsNoTracking().Where(s => s.UserId == userId && entryIds.Contains(s.EntryId)).Select(s => s.EntryId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<(IReadOnlyList<SavedMessage> Saved, MessagePin? Pin)> ListToolsForEntryAsync(Guid entryId, CancellationToken cancellationToken) =>
        (await db.SavedMessages.Where(s => s.EntryId == entryId).ToListAsync(cancellationToken).ConfigureAwait(false),
            await db.MessagePins.FirstOrDefaultAsync(p => p.EntryId == entryId, cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<SavedRecord>> ListSavedForUserAsync(Guid userId, long beforeTicks, int limit, CancellationToken cancellationToken)
    {
        var rows = await (
            from s in db.SavedMessages.AsNoTracking()
            where s.UserId == userId && s.SavedTicks < beforeTicks
            join e in db.ConversationEvents.AsNoTracking() on s.EntryId equals e.Id
            where e.DeletedAtUtc == null
            join p in db.ConversationParticipants.AsNoTracking() on new { e.ConversationId, UserId = userId } equals new { p.ConversationId, p.UserId }
            where p.Status == ParticipantStatus.Active && e.Sequence >= p.VisibleFromSequence
            orderby s.SavedTicks descending
            select new { Entry = e, s.SavedTicks })
            .Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new SavedRecord(r.Entry, r.SavedTicks)).ToList();
    }

    // ---- Mentions (Phase 17) -----------------------------------------------------------------------

    public void Add(MessageMention mention) => db.MessageMentions.Add(mention);

    public void Remove(MessageMention mention) => db.MessageMentions.Remove(mention);

    public async Task<IReadOnlyList<MessageMention>> ListMentionsForEntryAsync(Guid entryId, CancellationToken cancellationToken) =>
        await db.MessageMentions.Where(m => m.EntryId == entryId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, MentionsView>> DescribeMentionsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<Guid, MentionsView>();
        }

        // Named people row by row; @everyone/@here only as "used" (they can be hundreds of rows).
        var named = await db.MessageMentions.AsNoTracking()
            .Where(m => entryIds.Contains(m.EntryId) && m.Kind == MentionKind.User)
            .Select(m => new { m.EntryId, m.UserId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var broadcasts = await db.MessageMentions.AsNoTracking()
            .Where(m => entryIds.Contains(m.EntryId) && m.Kind != MentionKind.User)
            .Select(m => new { m.EntryId, m.Kind }).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        return named.Select(n => n.EntryId).Concat(broadcasts.Select(b => b.EntryId)).Distinct().ToDictionary(
            id => id,
            id => new MentionsView(
                named.Where(n => n.EntryId == id).Select(n => n.UserId).ToList(),
                broadcasts.Any(b => b.EntryId == id && b.Kind == MentionKind.Everyone),
                broadcasts.Any(b => b.EntryId == id && b.Kind == MentionKind.Here)));
    }

    public async Task<IReadOnlyList<(Domain.Attachments.Attachment Attachment, ConversationEvent Entry)>> ListSharedFilesAsync(
        Guid conversationId, long visibleFromSequence, long? beforeSequence, int limit, CancellationToken cancellationToken)
    {
        var rows = await (
            from a in db.Attachments.AsNoTracking()
            where a.ConversationId == conversationId && a.MessageEntryId != null && a.Status == Domain.Attachments.AttachmentStatus.Ready
            join e in db.ConversationEvents.AsNoTracking() on a.MessageEntryId equals e.Id
            where e.DeletedAtUtc == null && e.Sequence >= visibleFromSequence && (beforeSequence == null || e.Sequence < beforeSequence)
            orderby e.Sequence descending, a.CreatedAtUtc descending, a.Id
            select new { Attachment = a, Entry = e })
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => (r.Attachment, r.Entry)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, ConversationEvent>> FindLastMessagesAsync(IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken)
    {
        if (conversationIds.Count == 0)
        {
            return new Dictionary<Guid, ConversationEvent>();
        }

        var ids = conversationIds.ToList();
        var messages = db.ConversationEvents.AsNoTracking()
            .Where(e => ids.Contains(e.ConversationId) && e.Kind == ConversationEventKind.Message && (e.ThreadRootId == null || e.ShownInConversation));
        var newest = messages.GroupBy(e => e.ConversationId).Select(g => new { ConversationId = g.Key, Sequence = g.Max(e => e.Sequence) });
        var rows = await (
            from e in messages
            join n in newest on new { e.ConversationId, e.Sequence } equals new { n.ConversationId, n.Sequence }
            select e).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(e => e.ConversationId);
    }

    public async Task<IReadOnlyList<Guid>> ListMentionedUserIdsAsync(Guid entryId, CancellationToken cancellationToken) =>
        await db.MessageMentions.AsNoTracking().Where(m => m.EntryId == entryId).Select(m => m.UserId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListMentionedUserIdsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        }

        var ids = entryIds.ToList();
        var rows = await db.MessageMentions.AsNoTracking().Where(m => ids.Contains(m.EntryId)).Select(m => new { m.EntryId, m.UserId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(r => r.EntryId).ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.UserId).ToList());
    }

    public async Task<IReadOnlyList<MentionRecord>> ListMentionsForUserAsync(Guid userId, long beforeTicks, int limit, CancellationToken cancellationToken)
    {
        var rows = await MentionsOf(userId)
            .Where(x => x.Mention.SentTicks < beforeTicks)
            .OrderByDescending(x => x.Mention.SentTicks)
            .Take(limit)
            .Select(x => new { x.Entry, x.Unread })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new MentionRecord(r.Entry, r.Unread)).ToList();
    }

    public Task<int> CountUnreadMentionsAsync(Guid userId, CancellationToken cancellationToken) =>
        MentionsOf(userId).CountAsync(x => x.Unread, cancellationToken);

    /// <summary>
    /// The user's mentions in messages they can still see. Unread: in the main timeline after their
    /// read position; a thread reply kept in the thread until they read the thread that far.
    /// </summary>
    private IQueryable<MentionRow> MentionsOf(Guid userId) =>
        from m in db.MessageMentions.AsNoTracking()
        where m.UserId == userId
        join e in db.ConversationEvents.AsNoTracking() on m.EntryId equals e.Id
        where e.DeletedAtUtc == null
        join p in db.ConversationParticipants.AsNoTracking() on new { e.ConversationId, UserId = userId } equals new { p.ConversationId, p.UserId }
        where p.Status == ParticipantStatus.Active && e.Sequence >= p.VisibleFromSequence
        select new MentionRow
        {
            Mention = m,
            Entry = e,
            Unread = (e.ThreadRootId == null || e.ShownInConversation)
                ? e.Sequence > p.LastReadSequence
                : !db.ThreadFollows.Any(f => f.RootId == e.ThreadRootId && f.UserId == userId && f.LastReadSequence >= e.Sequence),
        };

    private sealed class MentionRow
    {
        public required MessageMention Mention { get; init; }

        public required ConversationEvent Entry { get; init; }

        public bool Unread { get; init; }
    }

    public async Task<IReadOnlyList<Guid>> ListActiveConversationIdsInOrganizationAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var isActiveGuest = await db.OrganizationGuests.AsNoTracking().AnyAsync(g => g.OrganizationId == organizationId && g.UserId == userId
            && g.Status == MembershipStatus.Active && g.AccessExpiresAtUtc > now, cancellationToken).ConfigureAwait(false);
        return await (
            from p in db.ConversationParticipants
            where p.UserId == userId && p.Status == ParticipantStatus.Active
                && (!p.IsGuest || isActiveGuest)
            join c in db.Conversations on p.ConversationId equals c.Id
            where c.OrganizationId == organizationId
            select c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Add(OutboxMessage message) => db.OutboxMessages.Add(message);

    public void Remove(OutboxMessage message) => db.OutboxMessages.Remove(message);

    public Task<ConversationEvent?> FindEntryAsync(Guid conversationId, Guid entryId, CancellationToken cancellationToken) =>
        db.ConversationEvents.FirstOrDefaultAsync(e => e.ConversationId == conversationId && e.Id == entryId, cancellationToken);

    public async Task<IReadOnlyList<ConversationEvent>> FindEntriesAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken) =>
        await db.ConversationEvents.AsNoTracking().Where(e => entryIds.Contains(e.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<ConversationEvent?> FindMessageByClientIdAsync(Guid conversationId, Guid authorUserId, string clientMessageId, CancellationToken cancellationToken) =>
        db.ConversationEvents.FirstOrDefaultAsync(
            e => e.ConversationId == conversationId && e.ActorUserId == authorUserId && e.ClientMessageId == clientMessageId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListRecipientsAsync(Guid conversationId, long sequence, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var organizationId = await db.Conversations.AsNoTracking().Where(c => c.Id == conversationId).Select(c => c.OrganizationId)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var activeGuestIds = organizationId is { } orgId
            ? await db.OrganizationGuests.AsNoTracking().Where(g => g.OrganizationId == orgId && g.Status == MembershipStatus.Active && g.AccessExpiresAtUtc > now)
                .Select(g => g.UserId).ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];
        var guestsEnabled = organizationId is { } guestOrgId
            && await db.Organizations.AsNoTracking().AnyAsync(o => o.Id == guestOrgId && o.GuestsEnabled, cancellationToken).ConfigureAwait(false);
        return await (
            from p in db.ConversationParticipants
            where p.ConversationId == conversationId && p.Status == ParticipantStatus.Active && p.VisibleFromSequence <= sequence
            join c in db.Conversations on p.ConversationId equals c.Id
            where c.OrganizationId == null || p.IsVisitor
                  || (!p.IsGuest
                      && db.OrganizationMembers.Any(m => m.OrganizationId == c.OrganizationId && m.UserId == p.UserId && m.Status == MembershipStatus.Active)
                      && (c.WorkspaceId == null
                          || db.WorkspaceMembers.Any(w => w.WorkspaceId == c.WorkspaceId && w.UserId == p.UserId && w.Status == MembershipStatus.Active)))
                  || (p.IsGuest && guestsEnabled && activeGuestIds.Contains(p.UserId))
            select p.UserId).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Conversation>> ListChannelsAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        await db.Conversations.AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId && c.Type == ConversationType.Channel)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveParticipantsAsync(IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken)
    {
        if (conversationIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var now = time.GetUtcNow();
        var candidates = await (
            from p in db.ConversationParticipants.AsNoTracking()
            join c in db.Conversations on p.ConversationId equals c.Id
            where conversationIds.Contains(p.ConversationId) && p.Status == ParticipantStatus.Active
            select new { Participant = p, c.OrganizationId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var organizationIds = candidates.Where(c => c.Participant.IsGuest && c.OrganizationId != null).Select(c => c.OrganizationId!.Value).Distinct().ToList();
        var activeGuests = organizationIds.Count == 0
            ? []
            : await db.OrganizationGuests.AsNoTracking().Where(g => organizationIds.Contains(g.OrganizationId)
                && g.Status == MembershipStatus.Active && g.AccessExpiresAtUtc > now)
                .Select(g => new { g.OrganizationId, g.UserId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var activeGuestKeys = activeGuests.Select(g => (g.OrganizationId, g.UserId)).ToHashSet();
        return candidates.Where(c => !c.Participant.IsGuest || (c.OrganizationId is { } orgId && activeGuestKeys.Contains((orgId, c.Participant.UserId))))
            .GroupBy(c => c.Participant.ConversationId).ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task<IReadOnlyList<Guid>> ListActiveChannelIdsInWorkspaceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        await (
            from p in db.ConversationParticipants
            where p.UserId == userId && p.Status == ParticipantStatus.Active
            join c in db.Conversations on p.ConversationId equals c.Id
            where c.WorkspaceId == workspaceId
            select c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Conversation>> ListWebsiteConversationsAsync(Guid organizationId, SupportStatus? status, int limit, CancellationToken cancellationToken)
    {
        var query = db.Conversations.AsNoTracking().Where(c => c.OrganizationId == organizationId && c.Type == ConversationType.Website);
        if (status is { } s)
        {
            query = query.Where(c => c.SupportStatus == s);
        }

        // Ordered in memory: SQLite cannot order by DateTimeOffset; the inbox is bounded by the limit below.
        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.OrderByDescending(c => c.LastActivityAtUtc).ThenByDescending(c => c.Id).Take(limit).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, ConversationParticipant>> ListParticipationsAsync(
        Guid userId, IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken) =>
        conversationIds.Count == 0
            ? new Dictionary<Guid, ConversationParticipant>()
            : await db.ConversationParticipants.AsNoTracking()
                .Where(p => p.UserId == userId && conversationIds.Contains(p.ConversationId))
                .ToDictionaryAsync(p => p.ConversationId, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, ConversationEvent>> ListEdgeMessagesAsync(
        IReadOnlyCollection<Guid> conversationIds, bool newest, CancellationToken cancellationToken)
    {
        if (conversationIds.Count == 0)
        {
            return new Dictionary<Guid, ConversationEvent>();
        }

        var live = db.ConversationEvents.AsNoTracking().Where(e => conversationIds.Contains(e.ConversationId)
            && e.Kind == ConversationEventKind.Message && e.DeletedAtUtc == null && (e.ThreadRootId == null || e.ShownInConversation));
        var edges = newest
            ? live.GroupBy(e => e.ConversationId).Select(g => new { ConversationId = g.Key, Sequence = g.Max(e => e.Sequence) })
            : live.GroupBy(e => e.ConversationId).Select(g => new { ConversationId = g.Key, Sequence = g.Min(e => e.Sequence) });
        var rows = await (from e in db.ConversationEvents.AsNoTracking()
                          join edge in edges on new { e.ConversationId, e.Sequence } equals new { edge.ConversationId, edge.Sequence }
                          select e).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(e => e.ConversationId);
    }

    public void Add(SupportNote note) => db.SupportNotes.Add(note);

    public async Task<IReadOnlyList<SupportNote>> ListSupportNotesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        // Ordered in memory (SQLite cannot order by DateTimeOffset); a conversation has few notes.
        var notes = await db.SupportNotes.AsNoTracking().Where(n => n.ConversationId == conversationId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return notes.OrderBy(n => n.CreatedAtUtc).ThenBy(n => n.Id).ToList();
    }

    public Task<OutboxMessage?> FindOutboxAsync(Guid outboxId, CancellationToken cancellationToken) =>
        db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == outboxId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListDueOutboxAsync(DateTimeOffset nowUtc, int limit, CancellationToken cancellationToken)
    {
        // NextAttemptAtUtc is stored as UTC ticks (see OutboxMessageConfiguration), so the
        // comparison and ordering run in the database on both providers.
        return await db.OutboxMessages.AsNoTracking()
            .Where(o => o.NextAttemptAtUtc <= nowUtc)
            .OrderBy(o => o.NextAttemptAtUtc)
            .Select(o => o.Id)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken) => db.TrySaveChangesAsync(cancellationToken);

    public async Task<IConversationAppendLock> LockForAppendAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A write that changes nothing takes the row lock (PostgreSQL) or the write lock (SQLite)
            // as the transaction's first statement; the following read then sees the latest position.
            await db.Conversations.Where(c => c.Id == conversation.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSequence, c => c.LastSequence), cancellationToken).ConfigureAwait(false);
            await db.Entry(conversation).ReloadAsync(cancellationToken).ConfigureAwait(false);
            return new AppendLock(transaction);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class AppendLock(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction) : IConversationAppendLock
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync(); // rolls back when not committed
    }
}

/// <summary>
/// Realtime destination access (replaces the Phase 4 placeholder): an active participant, and for
/// organization conversations an active member of that organization. Checked on every join and
/// typing signal.
/// </summary>
internal sealed class EfConversationAccess(ChatXDbContext db, TimeProvider time) : IConversationAccess
{
    public async Task<bool> CanAccessAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var row = await (
            from p in db.ConversationParticipants.AsNoTracking()
            join c in db.Conversations.AsNoTracking() on p.ConversationId equals c.Id
            where p.ConversationId == conversationId && p.UserId == userId && p.Status == ParticipantStatus.Active
            select new { p.IsVisitor, p.IsGuest, c.OrganizationId, c.WorkspaceId })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        if (row.OrganizationId is not { } organizationId || row.IsVisitor)
        {
            return true;
        }

        if (row.IsGuest)
        {
            var now = time.GetUtcNow();
            return await db.OrganizationGuests.AsNoTracking().AnyAsync(g => g.OrganizationId == organizationId && g.UserId == userId
                    && g.Status == MembershipStatus.Active && g.AccessExpiresAtUtc > now, cancellationToken).ConfigureAwait(false)
                && await db.Organizations.AsNoTracking().AnyAsync(o => o.Id == organizationId && o.GuestsEnabled, cancellationToken).ConfigureAwait(false);
        }

        var isOrganizationMember = await db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == organizationId
            && m.UserId == userId && m.Status == MembershipStatus.Active, cancellationToken).ConfigureAwait(false);
        if (!isOrganizationMember)
        {
            return false;
        }

        return row.WorkspaceId is not { } workspaceId
            || await db.WorkspaceMembers.AsNoTracking().AnyAsync(w => w.WorkspaceId == workspaceId && w.UserId == userId
                && w.Status == MembershipStatus.Active, cancellationToken).ConfigureAwait(false);
    }
}
