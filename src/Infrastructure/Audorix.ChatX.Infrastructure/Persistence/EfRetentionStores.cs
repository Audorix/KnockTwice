using System.Data.Common;
using Audorix.ChatX.Application.Administration;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Domain.Attachments;
using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Persistence;

/// <summary>
/// Account erasure (Phase 13). Runs after the person left their organizations and conversations
/// through the application services; this removes what is left in one transaction. Dates are
/// compared in memory where SQLite cannot compare <see cref="DateTimeOffset"/> values.
/// </summary>
internal sealed class EfAccountErasure(ChatXDbContext db, Application.Attachments.IAttachmentStorage storage) : IAccountErasure
{
    public async Task EraseAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        string? photoKey = null;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
            var placeholder = $"deleted-{userId:N}@deleted.invalid";
            user.Email = placeholder;
            user.NormalizedEmail = placeholder.ToUpperInvariant();
            user.UserName = placeholder;
            user.NormalizedUserName = placeholder.ToUpperInvariant();
            user.EmailConfirmed = false;
            user.PasswordHash = null;
            user.PhoneNumber = null;
            user.PhoneNumberConfirmed = false;
            user.TwoFactorEnabled = false;
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.MaxValue;
            user.AccessFailedCount = 0;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            user.DeletedAtUtc ??= nowUtc;

            var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken).ConfigureAwait(false);
            photoKey = profile?.PhotoKey ?? photoKey;
            profile?.Erase(nowUtc);

            // Attachments not sent with a message are deleted; the cleanup job removes their content.
            foreach (var upload in await db.Attachments.Where(a => a.OwnerUserId == userId && a.MessageEntryId == null && a.Status != AttachmentStatus.Deleted)
                         .ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                upload.Delete(nowUtc);
            }

            foreach (var guest in await db.OrganizationGuests.Where(g => g.UserId == userId && g.Status == MembershipStatus.Active)
                         .ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                guest.Revoke(nowUtc);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await db.UserLogins.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.UserTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.UserClaims.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.UserRoles.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.UserSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Contacts.Where(c => c.UserLowId == userId || c.UserHighId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.ContactRequests.Where(r => r.RequesterId == userId || r.RecipientId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.UserBlocks.Where(b => b.BlockerId == userId || b.BlockedId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.TeamMembers.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.WidgetResponders.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.SavedMessages.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.MessageDrafts.Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.ScheduledMessages.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Reminders.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.ProfileLinks.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Conversations.Where(c => c.AssignedToUserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.AssignedToUserId, (Guid?)null), cancellationToken).ConfigureAwait(false);

            // A visitor's record holds the name and email its website signed; the sessions go with it.
            var visitorIds = await db.Visitors.Where(v => v.UserId == userId).Select(v => v.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            await db.VisitorSessions.Where(s => visitorIds.Contains(s.VisitorId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Visitors.Where(v => v.UserId == userId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        db.ChangeTracker.Clear();

        // The photo file goes after the commit; nothing points at it any more.
        if (photoKey is not null)
        {
            try
            {
                await storage.DeleteAsync(photoKey, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A leftover file without a reference is harmless.
            }
        }
    }

    public async Task<bool> TryRemoveUnusedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var referenced = await db.ConversationEvents.AnyAsync(e => e.ActorUserId == userId || e.SubjectUserId == userId || e.DeletedByUserId == userId, cancellationToken)
                             .ConfigureAwait(false)
                         || await db.Conversations.AnyAsync(c => c.CreatedByUserId == userId, cancellationToken).ConfigureAwait(false)
                         || await db.Attachments.AnyAsync(a => a.OwnerUserId == userId && a.MessageEntryId != null, cancellationToken).ConfigureAwait(false);
        if (referenced)
        {
            return false;
        }

        try
        {
            // Profile, participations and remaining attachment records follow by cascade.
            return await db.Users.Where(u => u.Id == userId && u.DeletedAtUtc != null).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) == 1;
        }
        catch (DbException)
        {
            // Something else still refers to the account (for example an invitation it sent): keep the erased row.
            return false;
        }
    }

    public async Task<IReadOnlyList<Guid>> ListInactiveAnonymousVisitorsAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken)
    {
        var visitors = await (
            from v in db.Visitors.AsNoTracking()
            where v.ExternalSubject == null
            join u in db.Users.AsNoTracking() on v.UserId equals u.Id
            where u.DeletedAtUtc == null
            select new { v.UserId, v.LastSeenAtUtc }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return visitors.Where(v => v.LastSeenAtUtc < cutoff).OrderBy(v => v.LastSeenAtUtc).Select(v => v.UserId).Distinct().Take(limit).ToList();
    }

    public async Task<IReadOnlyList<Guid>> ListStaleUnconfirmedAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken)
    {
        var accounts = await (
            from u in db.Users.AsNoTracking()
            where !u.EmailConfirmed && !u.IsVisitor && u.DeletedAtUtc == null
            join p in db.UserProfiles.AsNoTracking() on u.Id equals p.UserId
            select new { u.Id, p.CreatedAtUtc }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return accounts.Where(a => a.CreatedAtUtc < cutoff).OrderBy(a => a.CreatedAtUtc).Select(a => a.Id).Take(limit).ToList();
    }
}

/// <summary>Organization deletion (Phase 13). History is deleted explicitly before the cascade, because replies and files refer to messages with restricting keys.</summary>
internal sealed class EfOrganizationRemoval(ChatXDbContext db) : Application.Organizations.IOrganizationRemoval
{
    public async Task<IReadOnlyList<Application.Organizations.OrganizationConversation>> ListConversationsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var conversations = await db.Conversations.AsNoTracking().Where(c => c.OrganizationId == organizationId)
            .Select(c => new { c.Id, c.LastSequence }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = conversations.Select(c => c.Id).ToList();
        var participants = await db.ConversationParticipants.AsNoTracking()
            .Where(p => ids.Contains(p.ConversationId) && p.Status == ParticipantStatus.Active)
            .Select(p => new { p.ConversationId, p.UserId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return conversations.Select(c => new Application.Organizations.OrganizationConversation(
            c.Id, c.LastSequence, participants.Where(p => p.ConversationId == c.Id).Select(p => p.UserId).ToList())).ToList();
    }

    public async Task<IReadOnlyList<string>> ListAttachmentKeysAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await (from a in db.Attachments.AsNoTracking()
               join c in db.Conversations.AsNoTracking() on a.ConversationId equals c.Id
               where c.OrganizationId == organizationId && !a.ContentRemoved
               select a.StorageKey).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Guid>> ListVisitorUserIdsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await (from v in db.Visitors.AsNoTracking()
               join w in db.Widgets.AsNoTracking() on v.WidgetId equals w.Id
               where w.OrganizationId == organizationId
               select v.UserId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task DeleteAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var conversationIds = db.Conversations.Where(c => c.OrganizationId == organizationId).Select(c => c.Id);
            await EfMessageRetentionStore.QueueSearchRemovalsAsync(db, db.ConversationEvents.Where(e => conversationIds.Contains(e.ConversationId)), cancellationToken)
                .ConfigureAwait(false);
            await db.ConversationEvents.Where(e => conversationIds.Contains(e.ConversationId) && e.ReplyToEntryId != null)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReplyToEntryId, (Guid?)null), cancellationToken).ConfigureAwait(false);
            await db.Attachments.Where(a => conversationIds.Contains(a.ConversationId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.OutboxMessages.Where(o => conversationIds.Contains(o.ConversationId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.ConversationEvents.Where(e => conversationIds.Contains(e.ConversationId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.ChannelTeams.Where(l => conversationIds.Contains(l.ConversationId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.GuestInvitations.Where(i => i.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.ConversationParticipants.Where(p => conversationIds.Contains(p.ConversationId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Conversations.Where(c => c.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            // Members, invitations, workspaces (with members and teams), guests and widgets (with
            // responders, visitors and their sessions) follow by cascade.
            await db.Organizations.Where(o => o.Id == organizationId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        db.ChangeTracker.Clear();
    }
}

/// <summary>Message retention (Phase 13). Uses the sortable <c>created_at_ticks</c> column, which works on both providers.</summary>
internal sealed class EfMessageRetentionStore(ChatXDbContext db) : IMessageRetentionStore
{
    public async Task<IReadOnlyList<ExpiredMessage>> ListExpiredMessagesAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken)
    {
        var ticks = cutoff.UtcTicks;
        return await db.ConversationEvents.AsNoTracking()
            .Where(e => e.Kind == ConversationEventKind.Message && EF.Property<long>(e, ChatXDbContext.CreatedAtTicks) < ticks)
            .OrderBy(e => EF.Property<long>(e, ChatXDbContext.CreatedAtTicks))
            .Take(limit)
            .Select(e => new ExpiredMessage(e.ConversationId, e.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(Guid EntryId, string StorageKey)>> ListAttachmentKeysAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        var rows = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageEntryId != null && entryIds.Contains(a.MessageEntryId.Value) && !a.ContentRemoved)
            .Select(a => new { EntryId = a.MessageEntryId!.Value, a.StorageKey })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => (r.EntryId, r.StorageKey)).ToList();
    }

    public async Task<int> PurgeMessagesAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        var ids = entryIds.ToList();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await QueueSearchRemovalsAsync(db, db.ConversationEvents.Where(e => ids.Contains(e.Id)), cancellationToken).ConfigureAwait(false);
            await db.ConversationEvents.Where(e => e.ReplyToEntryId != null && ids.Contains(e.ReplyToEntryId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReplyToEntryId, (Guid?)null), cancellationToken).ConfigureAwait(false);
            await db.ConversationEvents.Where(e => e.ThreadRootId != null && ids.Contains(e.ThreadRootId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ThreadRootId, (Guid?)null).SetProperty(e => e.ShownInConversation, false), cancellationToken)
                .ConfigureAwait(false);
            await db.Attachments.Where(a => a.MessageEntryId != null && ids.Contains(a.MessageEntryId.Value)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.OutboxMessages.Where(o => ids.Contains(o.EntryId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            var deleted = await db.ConversationEvents.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return deleted;
        }).ConfigureAwait(false);
    }

    /// <summary>Bulk deletes bypass the change tracker: queue the search index removals explicitly (Phase 14).</summary>
    internal static async Task QueueSearchRemovalsAsync(ChatXDbContext db, IQueryable<ConversationEvent> entries, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var messages = await entries.Where(e => e.Kind == ConversationEventKind.Message)
            .Select(e => new { e.Id, e.ConversationId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (messages.Count == 0)
        {
            return;
        }

        db.SearchQueue.AddRange(messages.Select(m => new Search.SearchQueueItem { EntryId = m.Id, ConversationId = m.ConversationId, EnqueuedAtTicks = now }));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
