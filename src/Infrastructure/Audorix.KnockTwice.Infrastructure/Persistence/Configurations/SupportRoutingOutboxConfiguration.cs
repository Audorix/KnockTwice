using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.KnockTwice.Infrastructure.Persistence.Configurations;

internal sealed class SupportRoutingOutboxConfiguration : IEntityTypeConfiguration<SupportRoutingOutboxItem>
{
    public void Configure(EntityTypeBuilder<SupportRoutingOutboxItem> builder)
    {
        builder.ToTable("support_routing_outbox");
        builder.HasKey(item => item.Id).HasName("pk_support_routing_outbox");
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.EventType).HasMaxLength(32).IsRequired();
        builder.Property(item => item.LastError).HasMaxLength(1000);
        builder.Property(item => item.EnqueuedAtTicks).HasDefaultValue(0L);
        builder.Property(item => item.AvailableAtTicks).HasDefaultValue(0L);
        builder.HasIndex(item => new { item.CompletedAtUtc, item.DeadLetteredAtUtc, item.PublishedAtUtc, item.AvailableAtTicks, item.LeaseExpiresAtTicks })
            .HasDatabaseName("ix_support_routing_outbox_delivery");
        builder.HasIndex(item => new { item.OrganizationId, item.ConversationId, item.EnqueuedAtUtc })
            .HasDatabaseName("ix_support_routing_outbox_conversation");
    }
}
