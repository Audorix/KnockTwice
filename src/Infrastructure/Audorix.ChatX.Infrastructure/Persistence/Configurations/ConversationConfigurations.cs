using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Domain.Organizations;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.ChatX.Infrastructure.Persistence.Configurations;

// Phase 5 conversation model. Enums are stored as strings like the Phase 3 tables; the scope and
// direct-key rules of ADR-021 and the per-conversation sequence of ADR-022 are backed by
// check constraints and unique indexes on both providers.

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations", t =>
        {
            t.HasCheckConstraint("ck_conversations_scope",
                "(scope = 'Personal' AND organization_id IS NULL) OR (scope = 'Organization' AND organization_id IS NOT NULL)");
            t.HasCheckConstraint("ck_conversations_direct_key",
                "(type = 'Direct' AND direct_key IS NOT NULL) OR (type <> 'Direct' AND direct_key IS NULL)");
            t.HasCheckConstraint("ck_conversations_sequence", "last_sequence >= 0");
        });
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Scope).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Title).HasMaxLength(Conversation.TitleMaxLength);
        builder.Property(c => c.Description).HasMaxLength(Conversation.DescriptionMaxLength);
        builder.Property(c => c.DirectKey).HasMaxLength(120);

        // Every history entry increments the sequence; the token turns concurrent writers into a
        // retryable conflict instead of a duplicate number.
        builder.Property(c => c.LastSequence).IsConcurrencyToken();

        // One direct conversation per pair and scope (NULL for groups, which may repeat).
        builder.HasIndex(c => c.DirectKey).IsUnique().HasDatabaseName("ux_conversations_direct_key");
        builder.HasIndex(c => c.OrganizationId);
        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Cascade);

        // Website conversations (Phase 10) remember their widget; deleting a widget keeps the history.
        builder.HasIndex(c => c.WidgetId);
        builder.HasOne<Domain.Widgets.Widget>().WithMany().HasForeignKey(c => c.WidgetId).OnDelete(DeleteBehavior.SetNull);

        // Channels (Phase 11): in one workspace of the same organization, names unique per workspace.
        builder.Property(c => c.Visibility).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.ChannelKey).HasMaxLength(120);
        builder.HasIndex(c => c.ChannelKey).IsUnique().HasDatabaseName("ux_conversations_channel_key");
        builder.HasIndex(c => c.WorkspaceId);
        builder.HasOne<Workspace>().WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.WorkspaceId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // Support (Phase 11): status and assignee of website conversations.
        builder.Property(c => c.SupportStatus).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(c => new { c.OrganizationId, c.Type, c.SupportStatus });
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(c => c.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(c => c.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConversationParticipantConfiguration : IEntityTypeConfiguration<ConversationParticipant>
{
    public void Configure(EntityTypeBuilder<ConversationParticipant> builder)
    {
        builder.ToTable("conversation_participants", t =>
        {
            t.HasCheckConstraint("ck_conversation_participants_visible_from", "visible_from_sequence >= 1");
            t.HasCheckConstraint("ck_conversation_participants_read", "last_read_sequence >= visible_from_sequence - 1");
            t.HasCheckConstraint("ck_conversation_participants_delivered", "last_delivered_sequence >= last_read_sequence");
        });
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.NotificationLevel).HasConversion<string>().HasMaxLength(16).HasDefaultValue(NotificationLevel.All).HasSentinel(0);
        builder.Ignore(p => p.IsActive);
        builder.Ignore(p => p.Muted);

        // One participation row per conversation/user; leaving is a status, rejoining reuses the row.
        builder.HasAlternateKey(p => new { p.ConversationId, p.UserId });
        builder.HasIndex(p => new { p.UserId, p.Status });
        builder.HasOne<Conversation>().WithMany().HasForeignKey(p => p.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<Application.Messaging.OutboxMessage>
{
    public void Configure(EntityTypeBuilder<Application.Messaging.OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Type).HasMaxLength(32).IsRequired();

        // UTC ticks so "due" comparisons and ordering run in the database on SQLite as well.
        builder.Property(o => o.NextAttemptAtUtc)
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero))
            .HasColumnName("next_attempt_ticks");
        builder.HasIndex(o => o.NextAttemptAtUtc);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(o => o.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MessageReactionConfiguration : IEntityTypeConfiguration<MessageReaction>
{
    public void Configure(EntityTypeBuilder<MessageReaction> builder)
    {
        builder.ToTable("message_reactions");
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Emoji).HasMaxLength(MessageReaction.EmojiMaxLength).IsRequired();
        builder.HasIndex(r => new { r.EntryId, r.UserId, r.Emoji }).IsUnique().HasDatabaseName("ux_message_reactions");
        builder.HasIndex(r => r.ConversationId);

        // Reactions go with their message (also when retention or a deletion removes it) and with the account.
        builder.HasOne<ConversationEvent>().WithMany()
            .HasForeignKey(r => new { r.ConversationId, r.EntryId })
            .HasPrincipalKey(e => new { e.ConversationId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MessageMentionConfiguration : IEntityTypeConfiguration<MessageMention>
{
    public void Configure(EntityTypeBuilder<MessageMention> builder)
    {
        builder.ToTable("message_mentions");
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(m => new { m.EntryId, m.UserId }).IsUnique().HasDatabaseName("ux_message_mentions");
        builder.HasIndex(m => new { m.UserId, m.SentTicks }).HasDatabaseName("ix_message_mentions_user");

        // Mentions go with their message and with the account.
        builder.HasOne<ConversationEvent>().WithMany()
            .HasForeignKey(m => new { m.ConversationId, m.EntryId })
            .HasPrincipalKey(e => new { e.ConversationId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MessagePinConfiguration : IEntityTypeConfiguration<MessagePin>
{
    public void Configure(EntityTypeBuilder<MessagePin> builder)
    {
        builder.ToTable("message_pins");
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasIndex(p => p.EntryId).IsUnique().HasDatabaseName("ux_message_pins");
        builder.HasIndex(p => new { p.ConversationId, p.PinnedTicks });
        builder.HasOne<ConversationEvent>().WithMany()
            .HasForeignKey(p => new { p.ConversationId, p.EntryId })
            .HasPrincipalKey(e => new { e.ConversationId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(p => p.PinnedByUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SavedMessageConfiguration : IEntityTypeConfiguration<SavedMessage>
{
    public void Configure(EntityTypeBuilder<SavedMessage> builder)
    {
        builder.ToTable("saved_messages");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => new { s.UserId, s.EntryId }).IsUnique().HasDatabaseName("ux_saved_messages");
        builder.HasIndex(s => new { s.UserId, s.SavedTicks });
        builder.HasOne<ConversationEvent>().WithMany()
            .HasForeignKey(s => new { s.ConversationId, s.EntryId })
            .HasPrincipalKey(e => new { e.ConversationId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MessageDraftConfiguration : IEntityTypeConfiguration<MessageDraft>
{
    public void Configure(EntityTypeBuilder<MessageDraft> builder)
    {
        builder.ToTable("message_drafts");
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Body).IsRequired();
        builder.Ignore(d => d.ThreadRootId);
        builder.HasIndex(d => new { d.UserId, d.ConversationId, d.ThreadKey }).IsUnique().HasDatabaseName("ux_message_drafts");
        builder.HasIndex(d => d.UpdatedTicks);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(d => d.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScheduledMessageConfiguration : IEntityTypeConfiguration<ScheduledMessage>
{
    public void Configure(EntityTypeBuilder<ScheduledMessage> builder)
    {
        builder.ToTable("scheduled_messages");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Body).IsRequired();
        builder.Property(s => s.MentionUserIds).HasMaxLength(4000).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.FailureCode).HasMaxLength(64);
        builder.Ignore(s => s.SendAtUtc);
        builder.Ignore(s => s.ClientMessageId);
        builder.Ignore(s => s.MentionedUserIds);
        builder.HasIndex(s => new { s.Status, s.SendAtTicks });
        builder.HasIndex(s => s.UserId);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(s => s.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScheduledMessageFileConfiguration : IEntityTypeConfiguration<ScheduledMessageFile>
{
    public void Configure(EntityTypeBuilder<ScheduledMessageFile> builder)
    {
        builder.ToTable("scheduled_message_files");
        builder.HasKey(f => new { f.ScheduledMessageId, f.AttachmentId });
        builder.HasIndex(f => f.AttachmentId);
        builder.HasOne<ScheduledMessage>().WithMany().HasForeignKey(f => f.ScheduledMessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Domain.Attachments.Attachment>().WithMany().HasForeignKey(f => f.AttachmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ToTable("reminders");
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(r => r.RemindAtUtc);
        builder.HasIndex(r => new { r.Status, r.RemindAtTicks });
        builder.HasIndex(r => r.UserId);
        builder.HasOne<ConversationEvent>().WithMany()
            .HasForeignKey(r => new { r.ConversationId, r.EntryId })
            .HasPrincipalKey(e => new { e.ConversationId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ThreadFollowConfiguration : IEntityTypeConfiguration<ThreadFollow>
{
    public void Configure(EntityTypeBuilder<ThreadFollow> builder)
    {
        builder.ToTable("thread_follows");
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.HasIndex(f => new { f.RootId, f.UserId }).IsUnique().HasDatabaseName("ux_thread_follows");
        builder.HasIndex(f => f.UserId);
        builder.HasOne<ConversationEvent>().WithMany()
            .HasForeignKey(f => new { f.ConversationId, f.RootId })
            .HasPrincipalKey(e => new { e.ConversationId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConversationEventConfiguration : IEntityTypeConfiguration<ConversationEvent>
{
    public void Configure(EntityTypeBuilder<ConversationEvent> builder)
    {
        builder.ToTable("conversation_events", t =>
        {
            t.HasCheckConstraint("ck_conversation_events_sequence", "sequence >= 1");
            // Message fields only on messages; a deleted message keeps no text.
            t.HasCheckConstraint("ck_conversation_events_message",
                "(kind = 'Message' AND client_message_id IS NOT NULL AND (body IS NOT NULL OR deleted_at_utc IS NOT NULL)) OR (kind <> 'Message' AND body IS NULL AND client_message_id IS NULL AND (reply_to_entry_id IS NULL OR kind = 'Pinned'))");
            t.HasCheckConstraint("ck_conversation_events_version", "version >= 1");
        });
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Text).HasMaxLength(ConversationEvent.TextMaxLength);
        builder.Property(e => e.Body).HasMaxLength(20000);
        builder.Property(e => e.ClientMessageId).HasMaxLength(ConversationEvent.ClientMessageIdMaxLength);
        builder.Property(e => e.Version).HasDefaultValue(1);
        builder.Ignore(e => e.IsMessage);
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.IsThreadReply);
        builder.Ignore(e => e.InMainTimeline);

        // Threads (Phase 16): a plain reference (no key), cleared explicitly when the first message goes.
        builder.HasIndex(e => new { e.ThreadRootId, e.Sequence }).HasDatabaseName("ix_conversation_events_thread");

        // Creation time as UTC ticks (Phase 13): message retention compares ages in the database.
        builder.Property<long>(ChatXDbContext.CreatedAtTicks).HasDefaultValue(0L);
        builder.HasIndex(ChatXDbContext.CreatedAtTicks).HasDatabaseName("ix_conversation_events_created_at_ticks");

        // The history order: unique per conversation (ADR-022), also the cursor index.
        builder.HasIndex(e => new { e.ConversationId, e.Sequence }).IsUnique().HasDatabaseName("ux_conversation_events_sequence");

        // Idempotent sends: a client message id belongs to one message per sender and conversation, for ever.
        builder.HasIndex(e => new { e.ConversationId, e.ActorUserId, e.ClientMessageId }).IsUnique().HasDatabaseName("ux_conversation_events_client_message");

        // Replies stay inside their conversation even if an id from another chat is supplied.
        builder.HasAlternateKey(e => new { e.ConversationId, e.Id });
        builder.HasOne<ConversationEvent>().WithMany()
            .HasForeignKey(e => new { e.ConversationId, e.ReplyToEntryId })
            .HasPrincipalKey(e => new { e.ConversationId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(e => e.DeletedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(e => e.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(e => e.SubjectUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
