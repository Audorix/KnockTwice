using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.KnockTwice.Infrastructure.Persistence.Configurations;

internal sealed class StoredMessageTranslationConfiguration : IEntityTypeConfiguration<StoredMessageTranslation>
{
    public void Configure(EntityTypeBuilder<StoredMessageTranslation> b)
    {
        b.ToTable("message_translations");
        b.HasKey(x => x.Id).HasName("pk_message_translations");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TargetLanguage).HasMaxLength(16).IsRequired();
        b.Property(x => x.SourceSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.OriginalText).IsRequired();
        b.Property(x => x.TranslatedText).IsRequired();
        b.HasOne<Domain.Conversations.ConversationEvent>()
            .WithMany()
            .HasForeignKey(x => new { x.ConversationId, x.MessageId })
            .HasPrincipalKey(x => new { x.ConversationId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.MessageId, x.TargetLanguage, x.SourceSha256 })
            .IsUnique()
            .HasDatabaseName("ux_message_translations_message_language_source");
    }
}
