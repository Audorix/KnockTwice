using Audorix.ChatX.Domain.Attachments;
using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.ChatX.Infrastructure.Persistence.Configurations;

// Phase 7 attachment metadata (ADR-007). Content lives in the storage provider under storage_key.
internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments", t =>
        {
            t.HasCheckConstraint("ck_attachments_size", "size >= 0");
            // Only ready files are linked to messages.
            t.HasCheckConstraint("ck_attachments_linked_ready", "message_entry_id IS NULL OR status IN ('Ready', 'Deleted')");
        });
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.StorageKey).HasMaxLength(Attachment.StorageKeyMaxLength).IsRequired();
        builder.Property(a => a.FileName).HasMaxLength(Attachment.FileNameMaxLength).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(Attachment.ContentTypeMaxLength).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(a => a.RejectionCode).HasMaxLength(64);
        builder.Ignore(a => a.IsReady);
        builder.Ignore(a => a.IsLinked);

        // UTC ticks so the cleanup job's age comparisons run in the database on SQLite as well.
        builder.Property(a => a.CreatedAtUtc)
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero))
            .HasColumnName("created_at_ticks");

        builder.HasIndex(a => a.StorageKey).IsUnique();
        builder.HasIndex(a => new { a.OwnerUserId, a.Status });
        builder.HasIndex(a => a.MessageEntryId);
        builder.HasIndex(a => new { a.Status, a.CreatedAtUtc });

        builder.HasOne<Conversation>().WithMany().HasForeignKey(a => a.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(a => a.OwnerUserId).OnDelete(DeleteBehavior.Cascade);

        // The message must belong to the same conversation as the attachment.
        builder.HasOne<ConversationEvent>().WithMany()
            .HasForeignKey(a => new { a.ConversationId, a.MessageEntryId })
            .HasPrincipalKey(e => new { e.ConversationId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
