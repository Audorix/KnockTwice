using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.KnockTwice.Infrastructure.Persistence.Configurations;

internal sealed class AttachmentStorageMigrationJobConfiguration : IEntityTypeConfiguration<AttachmentStorageMigrationJob>
{
    public void Configure(EntityTypeBuilder<AttachmentStorageMigrationJob> b)
    {
        b.ToTable("attachment_storage_migration_jobs");
        b.HasKey(x => x.Id).HasName("pk_attachment_storage_migration_jobs");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SourceProvider).HasMaxLength(32).IsRequired();
        b.Property(x => x.DestinationProvider).HasMaxLength(32).IsRequired();
        b.Property(x => x.State).HasMaxLength(24).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.HasIndex(x => new { x.State, x.LeaseExpiresAtTicks }).HasDatabaseName("ix_attachment_storage_migration_jobs_claim");
    }
}

internal sealed class AttachmentStorageMigrationFileConfiguration : IEntityTypeConfiguration<AttachmentStorageMigrationFile>
{
    public void Configure(EntityTypeBuilder<AttachmentStorageMigrationFile> b)
    {
        b.ToTable("attachment_storage_migration_files");
        b.HasKey(x => x.Id).HasName("pk_attachment_storage_migration_files");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.StorageKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.State).HasMaxLength(24).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.HasOne<AttachmentStorageMigrationJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.JobId, x.State, x.AvailableAtTicks }).HasDatabaseName("ix_attachment_storage_migration_files_claim");
        b.HasIndex(x => x.AttachmentId).HasDatabaseName("ix_attachment_storage_migration_files_attachment");
    }
}
