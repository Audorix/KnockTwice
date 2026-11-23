using Audorix.ChatX.Application.Conversations;
using Audorix.ChatX.Domain.Conversations;

namespace Audorix.ChatX.UnitTests;

/// <summary>Phase 17: <c>@everyone</c> / <c>@here</c> detection and notification levels.</summary>
public sealed class MentionTextTests
{
    [Theory]
    [InlineData("@everyone lunch", true, false)]
    [InlineData("Lunch, @Everyone!", true, false)]
    [InlineData("(@here) quick question", false, true)]
    [InlineData("@here and @everyone", true, true)]
    [InlineData("mail x@everyone.example", false, false)]
    [InlineData("@everyones", false, false)]
    [InlineData("@@here", false, false)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void Finds_broadcast_mentions_only_as_whole_words(string? body, bool everyone, bool here) =>
        Assert.Equal((everyone, here), MentionText.FindBroadcasts(body));

    [Theory]
    [InlineData(NotificationLevel.All, false, true)]
    [InlineData(NotificationLevel.All, true, true)]
    [InlineData(NotificationLevel.Mentions, false, false)]
    [InlineData(NotificationLevel.Mentions, true, true)]
    [InlineData(NotificationLevel.Nothing, true, false)]
    public void Levels_decide_what_notifies(NotificationLevel level, bool mentioned, bool notified)
    {
        var participant = ConversationParticipant.Join(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ParticipantRole.Member, 1, DateTimeOffset.UtcNow);
        participant.SetNotificationLevel(level);
        Assert.Equal(notified, participant.WantsNotification(mentioned));
        Assert.Equal(level != NotificationLevel.All, participant.Muted);
    }
}
