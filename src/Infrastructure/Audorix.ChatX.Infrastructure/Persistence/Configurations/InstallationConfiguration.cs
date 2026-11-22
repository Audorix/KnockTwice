using Audorix.ChatX.Domain.Installations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.ChatX.Infrastructure.Persistence.Configurations;

internal sealed class InstallationConfiguration : IEntityTypeConfiguration<Installation>
{
    /// <summary>Shadow column that, with a unique index and check constraint, permits exactly one row.</summary>
    internal const string SingletonColumn = "Singleton";

    public void Configure(EntityTypeBuilder<Installation> builder)
    {
        // Explicit lower-case snake_case names keep both providers identical and avoid quoting in PostgreSQL.
        builder.ToTable("installation", t => t.HasCheckConstraint("ck_installation_singleton", "singleton = 1"));

        builder.HasKey(i => i.Id).HasName("pk_installation");
        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(i => i.DefaultLanguage).HasColumnName("default_language").HasMaxLength(8).IsRequired();
        builder.Property(i => i.InitializedAtUtc).HasColumnName("initialized_at_utc").IsRequired();
        builder.Property(i => i.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        builder.Property<int>(SingletonColumn).HasColumnName("singleton").IsRequired();
        builder.HasIndex(SingletonColumn).IsUnique().HasDatabaseName("ux_installation_singleton");
    }
}
