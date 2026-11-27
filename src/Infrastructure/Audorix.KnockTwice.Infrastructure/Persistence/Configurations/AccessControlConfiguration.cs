using Audorix.KnockTwice.Domain.Authorization;
using Audorix.KnockTwice.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audorix.KnockTwice.Infrastructure.Persistence.Configurations;

internal sealed class AccessRoleConfiguration : IEntityTypeConfiguration<AccessRole>
{
    public void Configure(EntityTypeBuilder<AccessRole> builder)
    {
        builder.ToTable("access_roles");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).ValueGeneratedNever();
        builder.Property(role => role.Name).HasMaxLength(AccessRole.NameMaxLength).IsRequired();
        builder.Property(role => role.NormalizedName).HasMaxLength(AccessRole.NameMaxLength).IsRequired();
        builder.Property(role => role.Description).HasMaxLength(AccessRole.DescriptionMaxLength).IsRequired();
        builder.Property(role => role.Scope).HasMaxLength(20).IsRequired();
        builder.Property(role => role.OrganizationId);
        builder.Property(role => role.WorkspaceId);
        builder.Property(role => role.CreatedAtUtc).IsRequired();
        builder.HasIndex(role => new { role.Scope, role.OrganizationId, role.WorkspaceId, role.NormalizedName }).IsUnique();
        builder.HasOne<Audorix.KnockTwice.Domain.Organizations.Organization>().WithMany().HasForeignKey(role => role.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Audorix.KnockTwice.Domain.Organizations.Workspace>().WithMany().HasForeignKey(role => role.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(role => role.Permissions).WithOne().HasForeignKey(permission => permission.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccessRolePermissionConfiguration : IEntityTypeConfiguration<AccessRoleGrant>
{
    public void Configure(EntityTypeBuilder<AccessRoleGrant> builder)
    {
        builder.ToTable("access_role_permissions");
        builder.HasKey(permission => new { permission.RoleId, permission.PermissionKey });
        builder.Property(permission => permission.RoleId).ValueGeneratedNever();
        builder.Property(permission => permission.PermissionKey).HasMaxLength(100).IsRequired();
        builder.Property(permission => permission.Effect).HasConversion<string>().HasMaxLength(10).IsRequired();
    }
}

internal sealed class AccessGroupConfiguration : IEntityTypeConfiguration<AccessGroup>
{
    public void Configure(EntityTypeBuilder<AccessGroup> builder)
    {
        builder.ToTable("access_groups");
        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).ValueGeneratedNever();
        builder.Property(group => group.Name).HasMaxLength(AccessGroup.NameMaxLength).IsRequired();
        builder.Property(group => group.NormalizedName).HasMaxLength(AccessGroup.NameMaxLength).IsRequired();
        builder.Property(group => group.Description).HasMaxLength(AccessGroup.DescriptionMaxLength).IsRequired();
        builder.Property(group => group.Scope).HasMaxLength(20).IsRequired();
        builder.Property(group => group.OrganizationId);
        builder.Property(group => group.WorkspaceId);
        builder.Property(group => group.CreatedAtUtc).IsRequired();
        builder.HasIndex(group => new { group.Scope, group.OrganizationId, group.WorkspaceId, group.NormalizedName }).IsUnique();
        builder.HasOne<Audorix.KnockTwice.Domain.Organizations.Organization>().WithMany().HasForeignKey(group => group.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Audorix.KnockTwice.Domain.Organizations.Workspace>().WithMany().HasForeignKey(group => group.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccessGroupMemberConfiguration : IEntityTypeConfiguration<AccessGroupMember>
{
    public void Configure(EntityTypeBuilder<AccessGroupMember> builder)
    {
        builder.ToTable("access_group_members");
        builder.HasKey(member => new { member.GroupId, member.UserId });
        builder.Property(member => member.GroupId).ValueGeneratedNever();
        builder.Property(member => member.AddedAtUtc).IsRequired();
        builder.HasOne<AccessGroup>().WithMany().HasForeignKey(member => member.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatXUser>().WithMany().HasForeignKey(member => member.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccessRoleGroupAssignmentConfiguration : IEntityTypeConfiguration<AccessRoleGroupAssignment>
{
    public void Configure(EntityTypeBuilder<AccessRoleGroupAssignment> builder)
    {
        builder.ToTable("access_role_groups");
        builder.HasKey(assignment => new { assignment.RoleId, assignment.GroupId });
        builder.Property(assignment => assignment.RoleId).ValueGeneratedNever();
        builder.Property(assignment => assignment.AssignedAtUtc).IsRequired();
        builder.HasOne<AccessRole>().WithMany().HasForeignKey(assignment => assignment.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AccessGroup>().WithMany().HasForeignKey(assignment => assignment.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
