using Audorix.ChatX.Application.Notifications;
using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Domain.Notifications;
using Audorix.ChatX.Domain.Users;
using Audorix.ChatX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Notifications;

/// <summary>Notification persistence (Phase 15). Ages are compared on <c>created_at_ticks</c>, which works on SQLite too.</summary>
internal sealed class EfNotificationStore(ChatXDbContext db) : INotificationStore
{
    public async Task<IReadOnlyList<PendingNotice>> ListPendingEmailAsync(
        DateTimeOffset createdAfter, DateTimeOffset createdBefore, int limit, CancellationToken cancellationToken)
    {
        var after = createdAfter.UtcTicks;
        var before = createdBefore.UtcTicks;
        var rows = await (
            from e in db.ConversationEvents.AsNoTracking()
            where e.Kind == ConversationEventKind.Message && e.DeletedAtUtc == null
                  && EF.Property<long>(e, ChatXDbContext.CreatedAtTicks) > after && EF.Property<long>(e, ChatXDbContext.CreatedAtTicks) <= before
            join p in db.ConversationParticipants.AsNoTracking() on e.ConversationId equals p.ConversationId
            where p.Status == ParticipantStatus.Active && p.NotificationLevel != NotificationLevel.Nothing && !p.IsVisitor && p.UserId != e.ActorUserId
                  && e.Sequence > p.LastNotifiedSequence && e.Sequence >= p.VisibleFromSequence
            let mentioned = db.MessageMentions.Any(m => m.EntryId == e.Id && m.UserId == p.UserId)
            // Main timeline: unread in the conversation. Thread replies kept in the thread (Phase 16):
            // unread in the thread, for followers, or (Phase 17) for people they mention.
            let mainUnread = (e.ThreadRootId == null || e.ShownInConversation) && e.Sequence > p.LastReadSequence
            let hidden = e.ThreadRootId != null && !e.ShownInConversation
            where (p.NotificationLevel == NotificationLevel.All
                      && (mainUnread || (hidden && db.ThreadFollows.Any(f => f.RootId == e.ThreadRootId && f.UserId == p.UserId && f.Following && e.Sequence > f.LastReadSequence))))
                  || (mentioned
                      && (mainUnread || (hidden && !db.ThreadFollows.Any(f => f.RootId == e.ThreadRootId && f.UserId == p.UserId && e.Sequence <= f.LastReadSequence))))
            join c in db.Conversations.AsNoTracking() on e.ConversationId equals c.Id
            where !c.IsDemo && (c.Type != ConversationType.Website || c.AssignedToUserId == null || c.AssignedToUserId == p.UserId)
            join profile in db.UserProfiles.AsNoTracking() on p.UserId equals profile.UserId
            where profile.EmailNotifications != UserProfile.EmailOff && profile.DeletedAtUtc == null
            join u in db.Users.AsNoTracking() on p.UserId equals u.Id
            where u.EmailConfirmed && !u.IsVisitor && !u.IsDemo && u.DeletedAtUtc == null
            orderby EF.Property<long>(e, ChatXDbContext.CreatedAtTicks)
            select new { p.UserId, e.ConversationId, e.Id, e.Sequence, e.ActorUserId, e.Body, e.CreatedAtUtc })
            .Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new PendingNotice(r.UserId, r.ConversationId, r.Id, r.Sequence, r.ActorUserId, r.Body, r.CreatedAtUtc)).ToList();
    }

    public async Task MarkNotifiedAsync(Guid userId, IReadOnlyDictionary<Guid, long> sequences, CancellationToken cancellationToken)
    {
        var ids = sequences.Keys.ToList();
        foreach (var participant in await db.ConversationParticipants.Where(p => p.UserId == userId && ids.Contains(p.ConversationId))
                     .ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            participant.MarkNotified(sequences[participant.ConversationId]);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PushQueueEntry>> TakePushAsync(int limit, CancellationToken cancellationToken) =>
        await db.PushQueue.AsNoTracking().OrderBy(q => q.Id).Take(limit)
            .Select(q => new PushQueueEntry(q.Id, q.EntryId)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task CompletePushAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken) =>
        db.PushQueue.Where(q => ids.Contains(q.Id)).ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<PushSubscription>> ListSubscriptionsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        await db.PushSubscriptions.Where(s => userIds.Contains(s.UserId)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<PushSubscription?> FindSubscriptionAsync(string endpoint, CancellationToken cancellationToken) =>
        db.PushSubscriptions.SingleOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);

    public void Add(PushSubscription subscription) => db.PushSubscriptions.Add(subscription);

    public void Remove(PushSubscription subscription) => db.PushSubscriptions.Remove(subscription);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
