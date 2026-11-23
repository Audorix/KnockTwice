using Audorix.ChatX.Domain.Notifications;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.ChatX.Infrastructure.Notifications;

/// <summary>A new message to push (Phase 15), added in the same transaction as the message.</summary>
public sealed class PushQueueItem
{
    public long Id { get; set; }

    public Guid EntryId { get; set; }

    public long EnqueuedAtTicks { get; set; }
}

/// <summary>The installation's VAPID key pair (Phase 15); the private key is protected with Data Protection.</summary>
public sealed class WebPushKeys
{
    public int Id { get; set; }

    public string PublicKey { get; set; } = string.Empty;

    public string ProtectedPrivateKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class PushQueueItemConfiguration : IEntityTypeConfiguration<PushQueueItem>
{
    public void Configure(EntityTypeBuilder<PushQueueItem> builder)
    {
        builder.ToTable("push_queue");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedOnAdd();
    }
}

internal sealed class WebPushKeysConfiguration : IEntityTypeConfiguration<WebPushKeys>
{
    public void Configure(EntityTypeBuilder<WebPushKeys> builder)
    {
        builder.ToTable("web_push_keys", t => t.HasCheckConstraint("ck_web_push_keys_single", "id = 1"));
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();
        builder.Property(k => k.PublicKey).HasMaxLength(200).IsRequired();
        builder.Property(k => k.ProtectedPrivateKey).HasMaxLength(2000).IsRequired();
    }
}

internal sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Endpoint).HasMaxLength(PushSubscription.EndpointMaxLength).IsRequired();
        builder.Property(s => s.P256dh).HasMaxLength(PushSubscription.KeyMaxLength).IsRequired();
        builder.Property(s => s.Auth).HasMaxLength(PushSubscription.KeyMaxLength).IsRequired();
        builder.HasIndex(s => s.Endpoint).IsUnique();
        builder.HasIndex(s => s.UserId);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
