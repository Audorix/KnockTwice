using Audorix.KnockTwice.Domain.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.KnockTwice.Infrastructure.Persistence.Configurations;

// Phase 9: audit log and live installation settings.

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Action).HasMaxLength(AuditEntry.ActionMaxLength).IsRequired();
        builder.Property(a => a.TargetType).HasMaxLength(32);
        builder.Property(a => a.TargetId).HasMaxLength(128);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.Details).HasMaxLength(AuditEntry.DetailsMaxLength);

        // UTC ticks so time filters, ordering and retention run in the database on SQLite as well.
        builder.Property(a => a.OccurredAtUtc)
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero))
            .HasColumnName("occurred_at_ticks");

        // No foreign key to users: the log outlives accounts and must not block their removal.
        builder.HasIndex(a => a.OccurredAtUtc);
        builder.HasIndex(a => new { a.Action, a.OccurredAtUtc });
        builder.HasIndex(a => new { a.ActorUserId, a.OccurredAtUtc });
        builder.HasIndex(a => a.TargetId);
    }
}

internal sealed class InstallationSettingConfiguration : IEntityTypeConfiguration<InstallationSetting>
{
    public void Configure(EntityTypeBuilder<InstallationSetting> builder)
    {
        builder.ToTable("installation_settings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(InstallationSetting.KeyMaxLength);
        builder.Property(s => s.Value).HasMaxLength(InstallationSetting.ValueMaxLength);
    }
}
