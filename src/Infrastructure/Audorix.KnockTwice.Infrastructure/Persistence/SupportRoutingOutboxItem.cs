namespace Audorix.KnockTwice.Infrastructure.Persistence;

/// <summary>
/// A durable routing event written with its support conversation change. This is the database
/// outbox for both the Database queue and the Redis relay; conversations remain authoritative.
/// </summary>
public sealed class SupportRoutingOutboxItem
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ConversationId { get; set; }

    public string EventType { get; set; } = "case.changed";

    public DateTimeOffset EnqueuedAtUtc { get; set; }

    public long EnqueuedAtTicks { get; set; }

    public DateTimeOffset AvailableAtUtc { get; set; }

    public long AvailableAtTicks { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public Guid? LeaseToken { get; set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    public long? LeaseExpiresAtTicks { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset? DeadLetteredAtUtc { get; set; }

    public string? LastError { get; set; }
}
