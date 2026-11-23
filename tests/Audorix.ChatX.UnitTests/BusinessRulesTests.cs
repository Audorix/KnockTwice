using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Domain.Organizations;

namespace Audorix.ChatX.UnitTests;

/// <summary>Domain rules of Phase 11: channels, guests, teams, support state and organization settings.</summary>
public sealed class BusinessRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Ws = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();

    [Fact]
    public void Channel_names_are_unique_per_workspace_ignoring_case_and_follow_renames()
    {
        var channel = Conversation.CreateChannel(Guid.NewGuid(), Org, Ws, "  Sales  ", ChannelVisibility.Public, Ana, Now);
        Assert.Equal(("Sales", Conversation.ChannelKeyFor(Ws, "SALES")), (channel.Title, channel.ChannelKey));
        Assert.NotEqual(channel.ChannelKey, Conversation.ChannelKeyFor(Guid.NewGuid(), "Sales"));

        channel.Rename("Sales EMEA");
        Assert.Equal(Conversation.ChannelKeyFor(Ws, "sales emea"), channel.ChannelKey);
        Assert.Equal("channel_name_too_long", Assert.Throws<DomainRuleException>(() => channel.Rename(new string('x', 81))).Code);
        Assert.Equal("not_a_group", Assert.Throws<DomainRuleException>(channel.RequireGroup).Code);
    }

    [Fact]
    public void Visibility_and_support_state_belong_to_their_conversation_types()
    {
        var group = Conversation.CreateGroup(Guid.NewGuid(), ConversationScope.Organization, Org, "Team", Ana, Now);
        Assert.Equal("not_a_channel", Assert.Throws<DomainRuleException>(() => group.SetVisibility(ChannelVisibility.Private)).Code);
        Assert.Equal("not_a_support_conversation", Assert.Throws<DomainRuleException>(() => group.Assign(Ana)).Code);

        var website = Conversation.CreateWebsite(Guid.NewGuid(), Org, Guid.NewGuid(), "Visitor", Guid.NewGuid(), Now);
        Assert.Equal(SupportStatus.Open, website.SupportStatus);
        Assert.True(website.Assign(Ana));
        Assert.False(website.Assign(Ana));
        Assert.True(website.SetSupportStatus(SupportStatus.Closed));
        Assert.False(website.SetSupportStatus(SupportStatus.Closed));
    }

    [Fact]
    public void Guests_are_plain_members_of_a_channel_and_can_come_back_as_members()
    {
        var guest = ConversationParticipant.JoinGuest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, Now);
        Assert.True(guest.IsGuest);
        Assert.False(guest.Can(ConversationPermission.ManageMembers));
        Assert.Equal("guest_role_fixed", Assert.Throws<DomainRuleException>(() => guest.ChangeRole(ParticipantRole.Administrator)).Code);

        guest.Leave(Now);
        guest.Rejoin(ParticipantRole.Member, 9, Now);
        Assert.False(guest.IsGuest);
        guest.Leave(Now);
        Assert.Throws<DomainRuleException>(() => guest.Rejoin(ParticipantRole.Administrator, 12, Now, asGuest: true));
    }

    [Fact]
    public void Team_members_must_be_active_members_of_the_teams_workspace()
    {
        var workspace = Workspace.Create(Ws, Org, "General", Now);
        var team = Team.Create(Guid.NewGuid(), workspace, "Support", "First line", Now);
        var membership = OrganizationMember.Join(Guid.NewGuid(), Org, Ana, OrganizationRole.Member, Now);
        var inWorkspace = WorkspaceMember.Join(Guid.NewGuid(), workspace, membership, WorkspaceRole.Member, Now);
        Assert.Equal(TeamRole.Manager, TeamMember.Join(Guid.NewGuid(), team, inWorkspace, TeamRole.Manager, Now).Role);

        var other = WorkspaceMember.Join(Guid.NewGuid(), Workspace.Create(Guid.NewGuid(), Org, "Other", Now), membership, WorkspaceRole.Member, Now);
        Assert.Equal("not_workspace_member", Assert.Throws<DomainRuleException>(() => TeamMember.Join(Guid.NewGuid(), team, other, TeamRole.Member, Now)).Code);
        inWorkspace.Revoke(Now);
        Assert.Equal("not_workspace_member", Assert.Throws<DomainRuleException>(() => TeamMember.Join(Guid.NewGuid(), team, inWorkspace, TeamRole.Member, Now)).Code);
    }

    [Fact]
    public void Organization_settings_and_support_agents()
    {
        var organization = Organization.Create(Org, "Acme", Ana, Now);
        Assert.Equal((true, ChannelCreationPolicy.Members), (organization.GuestsEnabled, organization.ChannelCreation));
        organization.Configure(false, null, Now);
        Assert.Equal((false, ChannelCreationPolicy.Members), (organization.GuestsEnabled, organization.ChannelCreation));
        organization.Configure(null, ChannelCreationPolicy.Administrators, Now);
        Assert.Equal((false, ChannelCreationPolicy.Administrators), (organization.GuestsEnabled, organization.ChannelCreation));

        var member = OrganizationMember.Join(Guid.NewGuid(), Org, Ana, OrganizationRole.Member, Now);
        member.SetSupportAgent(true);
        Assert.True(member.IsSupportAgent);
        member.Revoke(Now);
        Assert.False(member.IsSupportAgent);
        Assert.Throws<DomainRuleException>(() => member.SetSupportAgent(true));
    }

    [Fact]
    public void Guest_invitations_expire_and_answer_once()
    {
        var invitation = GuestInvitation.Create(Guid.NewGuid(), Org, Guid.NewGuid(), " Gina@Partner.example ", Ana, Now);
        Assert.Equal("GINA@PARTNER.EXAMPLE", invitation.NormalizedEmail);
        Assert.False(invitation.IsOpen(Now + GuestInvitation.Lifetime));
        invitation.Accept(Now);
        Assert.Equal("invitation_not_open", Assert.Throws<DomainRuleException>(() => invitation.Decline(Now)).Code);
        Assert.Equal("email_invalid", Assert.Throws<DomainRuleException>(() => GuestInvitation.Create(Guid.NewGuid(), Org, Guid.NewGuid(), "nope", Ana, Now)).Code);
    }
}
