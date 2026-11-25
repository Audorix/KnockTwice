using Audorix.KnockTwice.Application.Planning;
using Audorix.KnockTwice.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

/// <summary>Drafts, scheduled messages and reminders (Phase 20). Times are UTC ticks so every database can compare them.</summary>
internal sealed class EfPlanningStore(ChatXDbContext db) : IPlanningStore
{
    public Task<MessageDraft?> FindDraftAsync(Guid userId, Guid conversationId, Guid threadKey, CancellationToken cancellationToken) =>
        db.MessageDrafts.FirstOrDefaultAsync(d => d.UserId == userId && d.ConversationId == conversationId && d.ThreadKey == threadKey, cancellationToken);

    public async Task<IReadOnlyList<MessageDraft>> ListDraftsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.MessageDrafts.AsNoTracking().Where(d => d.UserId == userId).OrderByDescending(d => d.UpdatedTicks).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task DeleteDraftAsync(Guid userId, Guid conversationId, Guid threadKey, CancellationToken cancellationToken) =>
        db.MessageDrafts.Where(d => d.UserId == userId && d.ConversationId == conversationId && d.ThreadKey == threadKey).ExecuteDeleteAsync(cancellationToken);

    public Task<int> DeleteDraftsBeforeAsync(long updatedTicks, CancellationToken cancellationToken) =>
        db.MessageDrafts.Where(d => d.UpdatedTicks < updatedTicks).ExecuteDeleteAsync(cancellationToken);

    public void Add(MessageDraft draft) => db.MessageDrafts.Add(draft);

    public void Remove(MessageDraft draft) => db.MessageDrafts.Remove(draft);

    public void Add(ScheduledMessage scheduled) => db.ScheduledMessages.Add(scheduled);

    public void Add(ScheduledMessageFile file) => db.ScheduledMessageFiles.Add(file);

    public void Remove(ScheduledMessage scheduled) => db.ScheduledMessages.Remove(scheduled);

    public Task<ScheduledMessage?> FindScheduledAsync(Guid id, CancellationToken cancellationToken) =>
        db.ScheduledMessages.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ScheduledMessage>> ListScheduledAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.ScheduledMessages.AsNoTracking().Where(s => s.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<int> CountPendingScheduledAsync(Guid userId, CancellationToken cancellationToken) =>
        db.ScheduledMessages.CountAsync(s => s.UserId == userId && s.Status == ScheduledMessageStatus.Pending, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListDueScheduledAsync(long nowTicks, int limit, CancellationToken cancellationToken) =>
        await db.ScheduledMessages.AsNoTracking()
            .Where(s => s.Status == ScheduledMessageStatus.Pending && s.SendAtTicks <= nowTicks)
            .OrderBy(s => s.SendAtTicks).Select(s => s.Id).Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Guid>> ListScheduledFilesAsync(Guid scheduledId, CancellationToken cancellationToken) =>
        await db.ScheduledMessageFiles.AsNoTracking().Where(f => f.ScheduledMessageId == scheduledId).Select(f => f.AttachmentId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, int>> CountScheduledFilesAsync(IReadOnlyCollection<Guid> scheduledIds, CancellationToken cancellationToken) =>
        scheduledIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.ScheduledMessageFiles.AsNoTracking().Where(f => scheduledIds.Contains(f.ScheduledMessageId))
                .GroupBy(f => f.ScheduledMessageId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken).ConfigureAwait(false);

    public void Add(Reminder reminder) => db.Reminders.Add(reminder);

    public void Remove(Reminder reminder) => db.Reminders.Remove(reminder);

    public Task<Reminder?> FindReminderAsync(Guid id, CancellationToken cancellationToken) =>
        db.Reminders.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Reminder>> ListRemindersAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Reminders.AsNoTracking().Where(r => r.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<int> CountPendingRemindersAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Reminders.CountAsync(r => r.UserId == userId && r.Status == ReminderStatus.Pending, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListDueRemindersAsync(long nowTicks, int limit, CancellationToken cancellationToken) =>
        await db.Reminders.AsNoTracking()
            .Where(r => r.Status == ReminderStatus.Pending && r.RemindAtTicks <= nowTicks)
            .OrderBy(r => r.RemindAtTicks).Select(r => r.Id).Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
