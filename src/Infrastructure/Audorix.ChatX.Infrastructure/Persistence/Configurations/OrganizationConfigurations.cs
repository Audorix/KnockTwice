using Audorix.ChatX.Domain.Contacts;
using Audorix.ChatX.Domain.Organizations;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.ChatX.Infrastructure.Persistence.Configurations;

// Enums are stored as strings for readable data and stable filters ("status = 'Pending'").
// Integrity rules from Architecture §17.2 are enforced by the database where possible.

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength).IsRequired();
        builder.Property(o => o.GuestsEnabled).HasDefaultValue(true);
        builder.Property(o => o.ChannelCreation).HasConversion<string>().HasMaxLength(16).HasDefaultValue(ChannelCreationPolicy.Members);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(o => o.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams");
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(Team.NameMaxLength).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(Team.DescriptionMaxLength);
        builder.HasIndex(t => t.WorkspaceId);
        // The workspace must belong to the team's organization.
        builder.HasOne<Workspace>().WithMany()
            .HasForeignKey(t => new { t.OrganizationId, t.WorkspaceId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_members");
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);
        builder.HasAlternateKey(m => new { m.TeamId, m.UserId });
        builder.HasIndex(m => new { m.WorkspaceId, m.UserId });
        builder.HasOne<Team>().WithMany().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
        // Team members are workspace members (the row must exist; activity is checked by the service).
        builder.HasOne<WorkspaceMember>().WithMany()
            .HasForeignKey(m => new { m.WorkspaceId, m.UserId })
            .HasPrincipalKey(w => new { w.WorkspaceId, w.UserId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChannelTeamConfiguration : IEntityTypeConfiguration<ChannelTeam>
{
    public void Configure(EntityTypeBuilder<ChannelTeam> builder)
    {
        builder.ToTable("channel_teams");
        builder.HasKey(l => new { l.ConversationId, l.TeamId });
        builder.HasIndex(l => l.TeamId);
        builder.HasOne<Domain.Conversations.Conversation>().WithMany().HasForeignKey(l => l.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(l => l.TeamId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OrganizationGuestConfiguration : IEntityTypeConfiguration<OrganizationGuest>
{
    public void Configure(EntityTypeBuilder<OrganizationGuest> builder)
    {
        builder.ToTable("organization_guests");
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(g => g.IsActive);
        builder.HasAlternateKey(g => new { g.OrganizationId, g.UserId });
        builder.HasIndex(g => new { g.UserId, g.Status });
        builder.HasOne<Organization>().WithMany().HasForeignKey(g => g.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(g => g.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GuestInvitationConfiguration : IEntityTypeConfiguration<GuestInvitation>
{
    public void Configure(EntityTypeBuilder<GuestInvitation> builder)
    {
        builder.ToTable("guest_invitations");
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Email).HasMaxLength(OrganizationInvitation.EmailMaxLength).IsRequired();
        builder.Property(i => i.NormalizedEmail).HasMaxLength(OrganizationInvitation.EmailMaxLength).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(i => new { i.NormalizedEmail, i.Status });
        builder.HasIndex(i => new { i.OrganizationId, i.Status });
        builder.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Domain.Conversations.Conversation>().WithMany().HasForeignKey(i => i.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(i => i.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.ToTable("organization_members");
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(m => m.IsActive);

        // One membership row per organization/user (revocation is a status, not a new row).
        builder.HasAlternateKey(m => new { m.OrganizationId, m.UserId });
        builder.HasIndex(m => new { m.UserId, m.Status });
        builder.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OrganizationInvitationConfiguration : IEntityTypeConfiguration<OrganizationInvitation>
{
    public void Configure(EntityTypeBuilder<OrganizationInvitation> builder)
    {
        builder.ToTable("organization_invitations");
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Email).HasMaxLength(OrganizationInvitation.EmailMaxLength).IsRequired();
        builder.Property(i => i.NormalizedEmail).HasMaxLength(OrganizationInvitation.EmailMaxLength).IsRequired();
        builder.Property(i => i.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(i => new { i.NormalizedEmail, i.Status });
        builder.HasIndex(i => new { i.OrganizationId, i.Status });
        builder.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(i => i.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces");
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.Name).HasMaxLength(Workspace.NameMaxLength).IsRequired();
        // Composite key target so workspace members can only reference a workspace of their own organization.
        builder.HasAlternateKey(w => new { w.OrganizationId, w.Id });
        builder.HasOne<Organization>().WithMany().HasForeignKey(w => w.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("workspace_members");
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(m => m.IsActive);
        builder.HasAlternateKey(m => new { m.WorkspaceId, m.UserId });

        // The workspace must belong to the member's organization ...
        builder.HasOne<Workspace>().WithMany()
            .HasForeignKey(m => new { m.OrganizationId, m.WorkspaceId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // ... and the user must hold a membership record in that organization.
        builder.HasOne<OrganizationMember>().WithMany()
            .HasForeignKey(m => new { m.OrganizationId, m.UserId })
            .HasPrincipalKey(o => new { o.OrganizationId, o.UserId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts", t => t.HasCheckConstraint("ck_contacts_ordered_pair", "user_low_id <> user_high_id"));
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Ignore(c => c.Pair);
        builder.HasIndex(c => new { c.UserLowId, c.UserHighId }).IsUnique();
        builder.HasIndex(c => c.UserHighId);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(c => c.UserLowId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(c => c.UserHighId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContactRequestConfiguration : IEntityTypeConfiguration<ContactRequest>
{
    public void Configure(EntityTypeBuilder<ContactRequest> builder)
    {
        builder.ToTable("contact_requests");
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(r => r.IsPending);

        // At most one pending request per pair of users, in either direction.
        builder.HasIndex(r => new { r.PairLowId, r.PairHighId })
            .IsUnique()
            .HasFilter("status = 'Pending'")
            .HasDatabaseName("ux_contact_requests_pending_pair");
        builder.HasIndex(r => new { r.RecipientId, r.Status });
        builder.HasIndex(r => new { r.RequesterId, r.Status });
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(r => r.RequesterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(r => r.RecipientId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> builder)
    {
        builder.ToTable("user_blocks", t => t.HasCheckConstraint("ck_user_blocks_distinct", "blocker_id <> blocked_id"));
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.HasIndex(b => new { b.BlockerId, b.BlockedId }).IsUnique();
        builder.HasIndex(b => b.BlockedId);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(b => b.BlockerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(b => b.BlockedId).OnDelete(DeleteBehavior.Cascade);
    }
}
