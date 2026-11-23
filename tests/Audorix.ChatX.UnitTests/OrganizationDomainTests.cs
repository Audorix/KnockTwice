using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Contacts;
using Audorix.ChatX.Domain.Organizations;

namespace Audorix.ChatX.UnitTests;

public sealed class OrganizationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(OrganizationRole.Owner, OrganizationPermission.ManageRoles, true)]
    [InlineData(OrganizationRole.Administrator, OrganizationPermission.ManageRoles, false)]
    [InlineData(OrganizationRole.Administrator, OrganizationPermission.ManageMembers, true)]
    [InlineData(OrganizationRole.Administrator, OrganizationPermission.ManageWorkspaces, true)]
    [InlineData(OrganizationRole.Member, OrganizationPermission.Manage, false)]
    [InlineData(OrganizationRole.Member, OrganizationPermission.ManageMembers, false)]
    public void Organization_roles_grant_expected_permissions(OrganizationRole role, OrganizationPermission permission, bool granted) =>
        Assert.Equal(granted, OrganizationPermissions.IsGranted(role, permission));

    [Fact]
    public void Revoked_membership_grants_nothing_and_can_be_reactivated_once()
    {
        var member = OrganizationMember.Join(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.Owner, Now);
        member.Revoke(Now.AddDays(1));

        Assert.False(member.Can(OrganizationPermission.Manage));
        member.Reactivate(OrganizationRole.Member, Now.AddDays(2));
        Assert.True(member.IsActive);
        Assert.Null(member.RevokedAtUtc);
        Assert.Equal("already_member", Assert.Throws<DomainRuleException>(() => member.Reactivate(OrganizationRole.Member, Now.AddDays(3))).Code);
    }

    [Fact]
    public void Workspace_membership_requires_active_membership_of_the_same_organization()
    {
        var organizationId = Guid.NewGuid();
        var workspace = Workspace.Create(Guid.NewGuid(), organizationId, "Design", Now);
        var otherOrganization = OrganizationMember.Join(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.Owner, Now);
        var revoked = OrganizationMember.Join(Guid.NewGuid(), organizationId, Guid.NewGuid(), OrganizationRole.Member, Now);
        revoked.Revoke(Now);

        Assert.Equal("not_organization_member", Assert.Throws<DomainRuleException>(() =>
            WorkspaceMember.Join(Guid.NewGuid(), workspace, otherOrganization, WorkspaceRole.Member, Now)).Code);
        Assert.Equal("not_organization_member", Assert.Throws<DomainRuleException>(() =>
            WorkspaceMember.Join(Guid.NewGuid(), workspace, revoked, WorkspaceRole.Member, Now)).Code);
    }

    [Fact]
    public void Invitations_cannot_grant_ownership_and_expire()
    {
        Assert.Equal("invitation_role_invalid", Assert.Throws<DomainRuleException>(() =>
            OrganizationInvitation.Create(Guid.NewGuid(), Guid.NewGuid(), "a@example.org", OrganizationRole.Owner, Guid.NewGuid(), Now)).Code);

        var invitation = OrganizationInvitation.Create(Guid.NewGuid(), Guid.NewGuid(), " A@Example.org ", OrganizationRole.Member, Guid.NewGuid(), Now);
        Assert.Equal("A@EXAMPLE.ORG", invitation.NormalizedEmail);
        Assert.True(invitation.IsOpen(Now.AddDays(13)));
        Assert.False(invitation.IsOpen(Now + OrganizationInvitation.Lifetime));
        Assert.Equal("invitation_not_open", Assert.Throws<DomainRuleException>(() => invitation.Accept(Now.AddDays(15))).Code);
    }

    [Fact]
    public void Names_are_normalized_and_bounded()
    {
        Assert.Equal("Acme Labs", Organization.Create(Guid.NewGuid(), "  Acme \t Labs ", Guid.NewGuid(), Now).Name);
        Assert.Equal("organization_name_required", Assert.Throws<DomainRuleException>(() => Organization.Create(Guid.NewGuid(), " ", Guid.NewGuid(), Now)).Code);
        Assert.Equal("workspace_name_too_long", Assert.Throws<DomainRuleException>(() =>
            Workspace.Create(Guid.NewGuid(), Guid.NewGuid(), new string('w', Workspace.NameMaxLength + 1), Now)).Code);
    }

    [Fact]
    public void User_pairs_are_order_independent_and_reject_self()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.Equal(UserPair.Of(a, b), UserPair.Of(b, a));
        Assert.Equal(b, UserPair.Of(a, b).Other(a));
        Assert.Equal("cannot_target_self", Assert.Throws<DomainRuleException>(() => UserPair.Of(a, a)).Code);
    }

    [Fact]
    public void Only_the_recipient_answers_and_only_the_requester_cancels()
    {
        var requester = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var request = ContactRequest.Create(Guid.NewGuid(), requester, recipient, Now);

        Assert.Equal("contact_request_not_found", Assert.Throws<DomainRuleException>(() => request.Accept(requester, Now)).Code);
        Assert.Equal("contact_request_not_found", Assert.Throws<DomainRuleException>(() => request.Cancel(recipient, Now)).Code);
        request.Decline(recipient, Now);
        Assert.Equal("contact_request_not_pending", Assert.Throws<DomainRuleException>(() => request.Accept(recipient, Now)).Code);
    }
}
