using Audorix.ChatX.Application.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.ChatX.Infrastructure.Persistence;

/// <summary>A row of the change log (Phase 22), written by <see cref="ChatXDbContext"/> with the change.</summary>
public sealed class ChangeLogItem
{
    public long Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid EntryId { get; set; }

    public long RecordedTicks { get; set; }
}

internal sealed class ChangeLogItemConfiguration : IEntityTypeConfiguration<ChangeLogItem>
{
    public void Configure(EntityTypeBuilder<ChangeLogItem> builder)
    {
        builder.ToTable("change_log");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();
        builder.HasIndex(c => c.RecordedTicks);
        builder.HasIndex(c => new { c.ConversationId, c.Id }); // a person's changes: their conversations, after a cursor
    }
}

internal sealed class EfChangeFeedStore(ChatXDbContext db) : IChangeFeedStore
{
    public async Task<long> CurrentAsync(CancellationToken cancellationToken) =>
        await db.ChangeLog.MaxAsync(c => (long?)c.Id, cancellationToken).ConfigureAwait(false) ?? 0;

    public Task<long?> OldestAsync(CancellationToken cancellationToken) =>
        db.ChangeLog.MinAsync(c => (long?)c.Id, cancellationToken);

    public async Task<IReadOnlyList<ChangeRecord>> ListForUserAsync(Guid userId, long since, int limit, CancellationToken cancellationToken) =>
        await (
            from c in db.ChangeLog.AsNoTracking()
            where c.Id > since
                && db.ConversationParticipants.Any(p => p.ConversationId == c.ConversationId && p.UserId == userId && p.Status == Domain.Conversations.ParticipantStatus.Active)
            orderby c.Id
            select new ChangeRecord(c.Id, c.ConversationId, c.EntryId, c.RecordedTicks))
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<int> PurgeBeforeAsync(long ticks, CancellationToken cancellationToken) =>
        db.ChangeLog.Where(c => c.RecordedTicks < ticks).ExecuteDeleteAsync(cancellationToken);
}
