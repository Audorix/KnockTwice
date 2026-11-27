namespace Audorix.KnockTwice.Application.Conversations;

/// <summary>Replays a committed support case change to the staff inbox.</summary>
public interface ISupportRoutingNotifier
{
    Task NotifySupportRoutingAsync(Guid conversationId, CancellationToken cancellationToken);
}
