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

        // Profile photo (redesign 2026-11): a storage key, its type and a version for cache busting.
        builder.Property(p => p.Handle).HasMaxLength(UserProfile.HandleMaxLength);
        builder.HasIndex(p => p.Handle).IsUnique();
        builder.Property(p => p.PhotoKey).HasMaxLength(200);
        builder.Property(p => p.PhotoContentType).HasMaxLength(32);
        builder.Ignore(p => p.HasPhoto);

        // A profile cannot outlive its account.
        builder.HasOne<ChatXUser>().WithOne().HasForeignKey<UserProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Profile links (redesign 2026-11), removed with the account or when the person erases it.</summary>
internal sealed class ProfileLinkConfiguration : IEntityTypeConfiguration<ProfileLink>
{
    public void Configure(EntityTypeBuilder<ProfileLink> builder)
    {
        builder.ToTable("profile_links");
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Platform).HasConversion<string>().HasMaxLength(16);
        builder.Property(l => l.Visibility).HasConversion<string>().HasMaxLength(16);
        builder.Property(l => l.Url).HasMaxLength(ProfileLink.UrlMaxLength).IsRequired();
        builder.HasIndex(l => new { l.UserId, l.Position });
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
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
