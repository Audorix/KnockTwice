using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Conversations;

namespace Audorix.ChatX.UnitTests;

public sealed class ConversationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Ben = Guid.NewGuid();
    private static readonly Guid Org = Guid.NewGuid();

    [Fact]
    public void Direct_key_is_order_independent_and_includes_the_scope()
    {
        Assert.Equal(Conversation.DirectKeyFor(ConversationScope.Personal, null, Ana, Ben), Conversation.DirectKeyFor(ConversationScope.Personal, null, Ben, Ana));
        Assert.NotEqual(Conversation.DirectKeyFor(ConversationScope.Personal, null, Ana, Ben), Conversation.DirectKeyFor(ConversationScope.Organization, Org, Ana, Ben));
        Assert.Equal("cannot_target_self",
            Assert.Throws<DomainRuleException>(() => Conversation.CreateDirect(Guid.NewGuid(), ConversationScope.Personal, null, Ana, Ana, Now)).Code);
    }

    [Fact]
    public void Scope_and_organization_must_agree()
    {
        Assert.Equal("conversation_scope_invalid",
            Assert.Throws<DomainRuleException>(() => Conversation.CreateGroup(Guid.NewGuid(), ConversationScope.Organization, null, "T", Ana, Now)).Code);
        Assert.Equal("conversation_scope_invalid",
            Assert.Throws<DomainRuleException>(() => Conversation.CreateGroup(Guid.NewGuid(), ConversationScope.Personal, Org, "T", Ana, Now)).Code);
        Assert.Equal("conversation_title_required",
            Assert.Throws<DomainRuleException>(() => Conversation.CreateGroup(Guid.NewGuid(), ConversationScope.Personal, null, "  ", Ana, Now)).Code);
    }

    [Fact]
    public void Every_entry_takes_the_next_sequence()
    {
        var group = Conversation.CreateGroup(Guid.NewGuid(), ConversationScope.Personal, null, "Team", Ana, Now);
        var first = ConversationEvent.Record(Guid.NewGuid(), group, ConversationEventKind.Created, Ana, null, null, Now);
        var second = ConversationEvent.Record(Guid.NewGuid(), group, ConversationEventKind.Renamed, Ana, null, "New", Now.AddMinutes(1));

        Assert.Equal((1L, 2L, 2L), (first.Sequence, second.Sequence, group.LastSequence));
        Assert.Equal(Now.AddMinutes(1), group.LastActivityAtUtc);
    }

    [Fact]
    public void Read_position_moves_forward_only_and_never_past_the_last_entry()
    {
        var participant = ConversationParticipant.Join(Guid.NewGuid(), Guid.NewGuid(), Ben, ParticipantRole.Member, 5, Now);
        Assert.Equal(4, participant.LastReadSequence);

        Assert.True(participant.MarkRead(8, lastSequence: 10));
        Assert.False(participant.MarkRead(6, lastSequence: 10));
        Assert.True(participant.MarkRead(99, lastSequence: 10));
        Assert.Equal(10, participant.LastReadSequence);
    }

    [Fact]
    public void Rejoining_starts_history_again_and_ended_participations_cannot_act()
    {
        var participant = ConversationParticipant.Join(Guid.NewGuid(), Guid.NewGuid(), Ben, ParticipantRole.Administrator, 2, Now);
        participant.MarkRead(10, 10);
        participant.Leave(Now);
        Assert.False(participant.IsActive);
        Assert.False(participant.Can(ConversationPermission.ManageMembers));
        Assert.Throws<DomainRuleException>(() => participant.MarkRead(11, 11));

        participant.Rejoin(ParticipantRole.Member, 20, Now);
        Assert.Equal((20L, 19L, ParticipantRole.Member), (participant.VisibleFromSequence, participant.LastReadSequence, participant.Role));
    }

    [Theory]
    [InlineData(ParticipantRole.Owner, ConversationPermission.ManageRoles, true)]
    [InlineData(ParticipantRole.Owner, ConversationPermission.ManageMembers, true)]
    [InlineData(ParticipantRole.Administrator, ConversationPermission.ManageMembers, true)]
    [InlineData(ParticipantRole.Administrator, ConversationPermission.ManageSettings, true)]
    [InlineData(ParticipantRole.Administrator, ConversationPermission.ManageRoles, false)]
    [InlineData(ParticipantRole.Member, ConversationPermission.ManageSettings, false)]
    public void Roles_map_to_permissions(ParticipantRole role, ConversationPermission permission, bool granted) =>
        Assert.Equal(granted, ConversationParticipant.Join(Guid.NewGuid(), Guid.NewGuid(), Ana, role, 1, Now).Can(permission));
}
