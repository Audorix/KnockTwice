using Audorix.ChatX.Domain.Sessions;
using Audorix.ChatX.Domain.Users;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.ChatX.Infrastructure.Persistence.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).ValueGeneratedNever();
        builder.Property(p => p.DisplayName).HasMaxLength(UserProfile.DisplayNameMaxLength).IsRequired();
        builder.Property(p => p.UiLanguage).HasMaxLength(8).IsRequired();

        // Phase 8 preferences. Existing rows get "system" and previews on in the migration.
        builder.Property(p => p.Theme).HasMaxLength(8).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("ck_user_profiles_theme", "theme IN ('system', 'light', 'dark')"));
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();
        builder.Ignore(p => p.Initials);
        builder.Ignore(p => p.IsDeleted);

        // A profile cannot outlive its account.
        builder.HasOne<ChatXUser>().WithOne().HasForeignKey<UserProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Description).HasMaxLength(UserSession.DescriptionMaxLength).IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.LastSeenAtUtc).IsRequired();
        builder.Property(s => s.ExpiresAtUtc).IsRequired();
        builder.HasIndex(s => new { s.UserId, s.RevokedAtUtc });

        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
