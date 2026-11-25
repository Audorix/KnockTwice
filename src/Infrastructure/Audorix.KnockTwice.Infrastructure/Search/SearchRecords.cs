using Audorix.KnockTwice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.KnockTwice.Infrastructure.Search;

/// <summary>
/// A message whose index entry must be written or removed (Phase 14). Rows are added in the same
/// transaction as the message change (see <see cref="ChatXDbContext"/>), or in bulk for a rebuild,
/// and deleted once the search provider accepted the change. No foreign key: the message may be gone.
/// </summary>
public sealed class SearchQueueItem
{
    public long Id { get; set; }

    public Guid EntryId { get; set; }

    public Guid ConversationId { get; set; }

    public long EnqueuedAtTicks { get; set; }
}

/// <summary>
/// The database provider's index (Phase 14): one row per live message with its normalized text.
/// PostgreSQL adds a generated <c>tsvector</c> column with a GIN index; SQLite an FTS5 table kept in
/// step by triggers. Both are created by the migration, outside the EF model.
/// </summary>
public sealed class MessageSearchRow
{
    public Guid EntryId { get; set; }

    public Guid ConversationId { get; set; }

    public long Sequence { get; set; }

    public Guid SenderId { get; set; }

    public long CreatedAtTicks { get; set; }

    public bool HasFiles { get; set; }

    public string Content { get; set; } = string.Empty;
}

internal sealed class SearchQueueItemConfiguration : IEntityTypeConfiguration<SearchQueueItem>
{
    public void Configure(EntityTypeBuilder<SearchQueueItem> builder)
    {
        builder.ToTable("search_queue");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedOnAdd();
    }
}

internal sealed class MessageSearchRowConfiguration : IEntityTypeConfiguration<MessageSearchRow>
{
    public void Configure(EntityTypeBuilder<MessageSearchRow> builder)
    {
        builder.ToTable("message_search");
        builder.HasKey(r => r.EntryId);
        builder.Property(r => r.EntryId).ValueGeneratedNever();
        builder.Property(r => r.Content).IsRequired();
        builder.HasIndex(r => new { r.ConversationId, r.Sequence });
        builder.HasIndex(r => r.CreatedAtTicks);
    }
}
