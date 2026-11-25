namespace Audorix.KnockTwice.Domain.Contacts;

/// <summary>
/// An unordered pair of distinct users, stored low/high so each relationship has one row and
/// can be protected by a unique index. Contacts are independent of organization membership.
/// </summary>
public readonly record struct UserPair
{
    private UserPair(Guid low, Guid high)
    {
        Low = low;
        High = high;
    }

    public Guid Low { get; }

    public Guid High { get; }

    public static UserPair Of(Guid a, Guid b)
    {
        if (a == Guid.Empty || b == Guid.Empty)
        {
            throw new DomainRuleException("identifier_required", "User identifiers are required.");
        }

        if (a == b)
        {
            throw new DomainRuleException("cannot_target_self", "A user cannot form a relationship with themselves.");
        }

        return a.CompareTo(b) < 0 ? new UserPair(a, b) : new UserPair(b, a);
    }

    public bool Contains(Guid userId) => Low == userId || High == userId;

    public Guid Other(Guid userId) => Low == userId ? High : High == userId ? Low : throw new ArgumentException("User is not part of the pair.", nameof(userId));
}

/// <summary>A mutual contact relationship.</summary>
public sealed class Contact
{
    private Contact(Guid id, Guid userLowId, Guid userHighId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        UserLowId = userLowId;
        UserHighId = userHighId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserLowId { get; private set; }

    public Guid UserHighId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public UserPair Pair => UserPair.Of(UserLowId, UserHighId);

    public static Contact Create(Guid id, UserPair pair, DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty)
        {
            throw new DomainRuleException("identifier_required", "An identifier is required.");
        }

        return new Contact(id, pair.Low, pair.High, EnsureUtc(nowUtc));
    }

    internal static DateTimeOffset EnsureUtc(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero ? value : throw new DomainRuleException("timestamp_not_utc", "Timestamps must be expressed in UTC.");
}

public enum ContactRequestStatus
{
    Pending = 1,
    Accepted = 2,
    Declined = 3,
    Cancelled = 4,
}

/// <summary>
/// A request to become contacts. At most one request per pair is pending at a time (enforced by
/// a filtered unique index on the pair). Only the recipient may accept or decline; only the
/// requester may cancel.
/// </summary>
public sealed class ContactRequest
{
    private ContactRequest(Guid id, Guid requesterId, Guid recipientId, Guid pairLowId, Guid pairHighId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        RequesterId = requesterId;
        RecipientId = recipientId;
        PairLowId = pairLowId;
        PairHighId = pairHighId;
        Status = ContactRequestStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid RequesterId { get; private set; }

    public Guid RecipientId { get; private set; }

    public Guid PairLowId { get; private set; }

    public Guid PairHighId { get; private set; }

    public ContactRequestStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? RespondedAtUtc { get; private set; }

    public bool IsPending => Status == ContactRequestStatus.Pending;

    public static ContactRequest Create(Guid id, Guid requesterId, Guid recipientId, DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty)
        {
            throw new DomainRuleException("identifier_required", "An identifier is required.");
        }

        var pair = UserPair.Of(requesterId, recipientId);
        return new ContactRequest(id, requesterId, recipientId, pair.Low, pair.High, Contact.EnsureUtc(nowUtc));
    }

    public void Accept(Guid actingUserId, DateTimeOffset nowUtc) => Respond(actingUserId, RecipientId, ContactRequestStatus.Accepted, nowUtc);

    public void Decline(Guid actingUserId, DateTimeOffset nowUtc) => Respond(actingUserId, RecipientId, ContactRequestStatus.Declined, nowUtc);

    public void Cancel(Guid actingUserId, DateTimeOffset nowUtc) => Respond(actingUserId, RequesterId, ContactRequestStatus.Cancelled, nowUtc);

    /// <summary>Closes the request as a side effect of a block, regardless of who blocked.</summary>
    public void CloseBecauseOfBlock(DateTimeOffset nowUtc)
    {
        if (IsPending)
        {
            Status = ContactRequestStatus.Cancelled;
            RespondedAtUtc = Contact.EnsureUtc(nowUtc);
        }
    }

    private void Respond(Guid actingUserId, Guid allowedUserId, ContactRequestStatus status, DateTimeOffset nowUtc)
    {
        if (actingUserId != allowedUserId)
        {
            throw new DomainRuleException("contact_request_not_found", "The contact request was not found.");
        }

        if (!IsPending)
        {
            throw new DomainRuleException("contact_request_not_pending", "The contact request is no longer pending.");
        }

        Status = status;
        RespondedAtUtc = Contact.EnsureUtc(nowUtc);
    }
}

/// <summary>A one-directional block: <see cref="BlockerId"/> no longer interacts with <see cref="BlockedId"/>.</summary>
public sealed class UserBlock
{
    private UserBlock(Guid id, Guid blockerId, Guid blockedId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        BlockerId = blockerId;
        BlockedId = blockedId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid BlockerId { get; private set; }

    public Guid BlockedId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static UserBlock Create(Guid id, Guid blockerId, Guid blockedId, DateTimeOffset nowUtc)
    {
        _ = UserPair.Of(blockerId, blockedId); // validates distinct, non-empty ids
        if (id == Guid.Empty)
        {
            throw new DomainRuleException("identifier_required", "An identifier is required.");
        }

        return new UserBlock(id, blockerId, blockedId, Contact.EnsureUtc(nowUtc));
    }
}
