using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Domain.Attachments;
using Microsoft.EntityFrameworkCore;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

internal sealed class EfAttachmentStore(ChatXDbContext db) : IAttachmentStore
{
    public void Add(Attachment attachment) => db.Attachments.Add(attachment);

    public Task<Attachment?> FindAsync(Guid attachmentId, CancellationToken cancellationToken) =>
        db.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);

    public async Task<IReadOnlyList<Attachment>> FindManyAsync(IReadOnlyCollection<Guid> attachmentIds, CancellationToken cancellationToken) =>
        await db.Attachments.Where(a => attachmentIds.Contains(a.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Attachment>> ListReadyForMessagesAsync(IReadOnlyCollection<Guid> messageEntryIds, CancellationToken cancellationToken) =>
        await db.Attachments.AsNoTracking()
            .Where(a => a.MessageEntryId != null && messageEntryIds.Contains(a.MessageEntryId.Value) && a.Status == AttachmentStatus.Ready)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Attachment>> ListForMessageAsync(Guid messageEntryId, CancellationToken cancellationToken) =>
        await db.Attachments.Where(a => a.MessageEntryId == messageEntryId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<long> SumStoredSizeAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await db.Attachments
            .Where(a => a.OwnerUserId == ownerUserId && a.Status != AttachmentStatus.Deleted && a.Status != AttachmentStatus.Rejected)
            .SumAsync(a => (long?)a.Size, cancellationToken).ConfigureAwait(false) ?? 0;

    public async Task<IReadOnlyList<Attachment>> ListAbandonedAsync(DateTimeOffset readyBefore, DateTimeOffset pendingBefore, int limit, CancellationToken cancellationToken) =>
        await db.Attachments
            .Where(a => a.MessageEntryId == null
                && ((a.Status == AttachmentStatus.PendingUpload && a.CreatedAtUtc < pendingBefore)
                    || ((a.Status == AttachmentStatus.Ready || a.Status == AttachmentStatus.Quarantined) && a.CreatedAtUtc < readyBefore))
                // Files waiting with a scheduled message (Phase 20) are kept until it is sent or removed.
                && !db.ScheduledMessageFiles.Any(f => f.AttachmentId == a.Id))
            .OrderBy(a => a.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Attachment>> ListContentToRemoveAsync(int limit, CancellationToken cancellationToken) =>
        await db.Attachments
            .Where(a => !a.ContentRemoved && (a.Status == AttachmentStatus.Deleted || a.Status == AttachmentStatus.Rejected))
            .OrderBy(a => a.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Attachment>> ListQuarantinedAsync(int limit, CancellationToken cancellationToken) =>
        await db.Attachments
            .Where(a => a.Status == AttachmentStatus.Quarantined)
            .OrderBy(a => a.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
