using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Domain.Organizations;
using Audorix.ChatX.Domain.Widgets;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.ChatX.Infrastructure.Persistence.Configurations;

// Phase 10: website widgets, their responders, visitors and visitor sessions.

internal sealed class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        builder.ToTable("widgets");
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.Name).HasMaxLength(Widget.NameMaxLength).IsRequired();
        builder.Property(w => w.Title).HasMaxLength(Widget.TitleMaxLength).IsRequired();
        builder.Property(w => w.WelcomeMessage).HasMaxLength(Widget.WelcomeMaxLength);
        builder.Property(w => w.AccentColor).HasMaxLength(7).IsRequired();
        builder.Property(w => w.Position).HasConversion<string>().HasMaxLength(8);
        builder.Property(w => w.DefaultLanguage).HasMaxLength(8).IsRequired();
        builder.Property(w => w.AllowedOrigins).HasMaxLength(4000).IsRequired();
        builder.Property(w => w.ProtectedSecret).HasMaxLength(1000).IsRequired();
        builder.Ignore(w => w.Origins);
        builder.HasIndex(w => w.OrganizationId);
        builder.HasOne<Organization>().WithMany().HasForeignKey(w => w.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(w => w.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WidgetResponderConfiguration : IEntityTypeConfiguration<WidgetResponder>
{
    public void Configure(EntityTypeBuilder<WidgetResponder> builder)
    {
        builder.ToTable("widget_responders");
        builder.HasKey(r => new { r.WidgetId, r.UserId });
        builder.HasOne<Widget>().WithMany().HasForeignKey(r => r.WidgetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VisitorConfiguration : IEntityTypeConfiguration<Visitor>
{
    public void Configure(EntityTypeBuilder<Visitor> builder)
    {
        builder.ToTable("visitors");
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.ExternalSubject).HasMaxLength(Visitor.SubjectMaxLength);
        builder.Property(v => v.DisplayName).HasMaxLength(80).IsRequired();
        builder.Property(v => v.Email).HasMaxLength(254);
        builder.Ignore(v => v.IsIdentified);

        // One identified visitor per widget and host subject (NULL for anonymous visitors, which may repeat).
        builder.HasIndex(v => new { v.WidgetId, v.ExternalSubject }).IsUnique();
        builder.HasIndex(v => v.UserId).IsUnique();
        builder.HasOne<Widget>().WithMany().HasForeignKey(v => v.WidgetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VisitorSessionConfiguration : IEntityTypeConfiguration<VisitorSession>
{
    public void Configure(EntityTypeBuilder<VisitorSession> builder)
    {
        builder.ToTable("visitor_sessions");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(s => s.TokenHash).IsUnique();
        builder.HasIndex(s => s.VisitorId);
        builder.HasOne<Visitor>().WithMany().HasForeignKey(s => s.VisitorId).OnDelete(DeleteBehavior.Cascade);
    }
}
