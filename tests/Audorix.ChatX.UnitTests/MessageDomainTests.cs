using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Conversations;

namespace Audorix.ChatX.UnitTests;

public sealed class MessageDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Ben = Guid.NewGuid();

    private static Conversation Group() => Conversation.CreateGroup(Guid.NewGuid(), ConversationScope.Personal, null, "Team", Ana, Now);

    [Theory]
    [InlineData("Hello", "Hello")]
    [InlineData("\r\nLine one\r\nLine two  \n\n", "Line one\nLine two")]
    [InlineData("tab\tseparated", "tab\tseparated")]
    public void Bodies_are_normalized_plain_text(string input, string expected) =>
        Assert.Equal(expected, ConversationEvent.NormalizeBody(input, 4000));

    [Theory]
    [InlineData("   ", "message_body_required")]
    [InlineData("bell\a", "message_body_invalid")]
    public void Invalid_bodies_are_rejected(string input, string code) =>
        Assert.Equal(code, Assert.Throws<DomainRuleException>(() => ConversationEvent.NormalizeBody(input, 4000)).Code);

    [Fact]
    public void Length_limit_is_enforced() =>
        Assert.Equal("message_body_too_long", Assert.Throws<DomainRuleException>(() => ConversationEvent.NormalizeBody(new string('x', 11), 10)).Code);

    [Fact]
    public void Messages_take_the_conversation_sequence_and_replies_stay_inside_it()
    {
        var group = Group();
        var first = ConversationEvent.Post(Guid.NewGuid(), group, Ana, "a", "Hi", null, 4000, Now);
        var reply = ConversationEvent.Post(Guid.NewGuid(), group, Ben, "b", "Hello", first, 4000, Now);
        Assert.Equal((1L, 2L, first.Id), (first.Sequence, reply.Sequence, reply.ReplyToEntryId!.Value));

        var other = ConversationEvent.Post(Guid.NewGuid(), Group(), Ana, "c", "Elsewhere", null, 4000, Now);
        Assert.Equal("reply_target_invalid", Assert.Throws<DomainRuleException>(() => ConversationEvent.Post(Guid.NewGuid(), group, Ben, "d", "x", other, 4000, Now)).Code);
        Assert.Equal("client_message_id_invalid", Assert.Throws<DomainRuleException>(() => ConversationEvent.Post(Guid.NewGuid(), group, Ben, " ", "x", null, 4000, Now)).Code);
    }

    [Fact]
    public void Only_the_author_edits_within_the_window_and_versions_increase()
    {
        var message = ConversationEvent.Post(Guid.NewGuid(), Group(), Ana, "a", "Draft", null, 4000, Now);
        var window = TimeSpan.FromMinutes(15);

        Assert.Equal("message_not_editable", Assert.Throws<DomainRuleException>(() => message.Edit(Ben, "Mine", window, 4000, Now)).Code);
        message.Edit(Ana, "Draft", window, 4000, Now.AddMinutes(1));
        Assert.Equal(1, message.Version); // unchanged text is not an edit
        message.Edit(Ana, "Final", window, 4000, Now.AddMinutes(14));
        Assert.Equal(("Final", 2), (message.Body, message.Version));
        Assert.Equal("message_edit_window_expired", Assert.Throws<DomainRuleException>(() => message.Edit(Ana, "Late", window, 4000, Now.AddMinutes(16))).Code);
    }

    [Fact]
    public void Deleting_leaves_a_tombstone_and_ends_editing()
    {
        var message = ConversationEvent.Post(Guid.NewGuid(), Group(), Ana, "a", "Oops", null, 4000, Now);
        message.Delete(Ben, Now);

        Assert.Equal((null, true, 2, Ben), (message.Body, message.IsDeleted, message.Version, message.DeletedByUserId!.Value));
        Assert.Equal("message_not_found", Assert.Throws<DomainRuleException>(() => message.Edit(Ana, "Back", TimeSpan.FromHours(1), 4000, Now)).Code);
        Assert.False(message.IsSameSend("Oops", null, 4000));
    }

    [Fact]
    public void Delivery_watermark_moves_forward_and_reading_implies_delivery()
    {
        var participant = ConversationParticipant.Join(Guid.NewGuid(), Guid.NewGuid(), Ben, ParticipantRole.Member, 1, Now);
        Assert.True(participant.MarkDelivered(5, 10));
        Assert.False(participant.MarkDelivered(3, 10));
        participant.MarkRead(8, 10);
        Assert.Equal((8L, 8L), (participant.LastDeliveredSequence, participant.LastReadSequence));
        Assert.True(participant.MarkDelivered(50, 10));
        Assert.Equal(10, participant.LastDeliveredSequence);
    }
}
