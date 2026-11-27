using System.Collections.Concurrent;
using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Realtime;

namespace Audorix.KnockTwice.Application.PrivateChats;

/// <summary>
/// Private chat policy (owner decisions of 2026-11-22), bound from <c>PrivateChats</c>. Private
/// chats live only in this server's memory: nothing is written to the database, storage or logs.
/// </summary>
public sealed class PrivateChatSettings
{
    public const string SectionName = "PrivateChats";

    public bool Enabled { get; set; } = true;

    /// <summary>An invitation not accepted within this time ends the chat.</summary>
    public TimeSpan InvitationTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Messages kept in memory per chat; the oldest are dropped (and wiped) beyond it.</summary>
    public int MaxMessages { get; set; } = 1000;

    public long MaxFileSize { get; set; } = 10L * 1024 * 1024;

    /// <summary>Memory budget per chat for attachments.</summary>
    public long MaxBytesPerChat { get; set; } = 50L * 1024 * 1024;

    /// <summary>Private chats (invited or active) a person may take part in at once.</summary>
    public int MaxActivePerUser { get; set; } = 5;

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(5);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (InvitationTimeout < TimeSpan.FromSeconds(10) || InvitationTimeout > TimeSpan.FromDays(1))
        {
            errors.Add("PrivateChats:InvitationTimeout must be between 10 seconds and 1 day.");
        }

        if (MaxMessages is < 10 or > 100000 || MaxActivePerUser is < 1 or > 100)
        {
            errors.Add("PrivateChats:MaxMessages must be 10-100000 and MaxActivePerUser 1-100.");
        }

        if (MaxFileSize < 1024 || MaxBytesPerChat < MaxFileSize || MaxBytesPerChat > 1024L * 1024 * 1024)
        {
            errors.Add("PrivateChats:MaxFileSize must be at least 1 KB and MaxBytesPerChat at least MaxFileSize and at most 1 GB.");
        }

        if (SweepInterval < TimeSpan.FromMilliseconds(100))
        {
            errors.Add("PrivateChats:SweepInterval must be at least 100 ms.");
        }

        return errors;
    }
}

public enum PrivateChatStatus
{
    Invited = 1,
    Active = 2,
    Ended = 3,
}

/// <summary>Why a private chat ended. Sent to both people.</summary>
public static class PrivateChatEndReasons
{
    /// <summary>The initiator ended it (or cancelled the invitation).</summary>
    public const string Ended = "ended";

    /// <summary>The invitee declined.</summary>
    public const string Declined = "declined";

    /// <summary>The invitation was not accepted in time.</summary>
    public const string Expired = "expired";

    /// <summary>The initiator's sign-in session ended (sign-out, revocation, expiry).</summary>
    public const string SessionEnded = "session_ended";

    /// <summary>The two people may no longer talk (a block, or no shared relationship any more).</summary>
    public const string Unavailable = "unavailable";

    /// <summary>The server is shutting down; memory is not kept.</summary>
    public const string Shutdown = "shutdown";
}

public sealed record PrivateFile(Guid Id, Guid OwnerUserId, string FileName, string ContentType, FileKind Kind, byte[] Data, DateTimeOffset CreatedAtUtc)
{
    public bool Linked { get; set; }

    public long Size => Data.LongLength;
}

public sealed record PrivateMessage(
    Guid Id, long Sequence, Guid AuthorUserId, string ClientMessageId, string Body, IReadOnlyList<PrivateFile> Files, DateTimeOffset CreatedAtUtc);

/// <summary>
/// One private chat in memory. All members are accessed under <see cref="Gate"/>. Ending wipes
/// message bodies and zeroes file buffers before the chat is dropped.
/// </summary>
public sealed class PrivateChat
{
    internal PrivateChat(Guid id, Guid initiatorUserId, Guid initiatorSessionId, Guid inviteeUserId, Guid? organizationId, DateTimeOffset createdAtUtc, DateTimeOffset invitationExpiresAtUtc)
    {
        Id = id;
        InitiatorUserId = initiatorUserId;
        InitiatorSessionId = initiatorSessionId;
        InviteeUserId = inviteeUserId;
        OrganizationId = organizationId;
        CreatedAtUtc = createdAtUtc;
        InvitationExpiresAtUtc = invitationExpiresAtUtc;
    }

    public object Gate { get; } = new();

    public Guid Id { get; }

    public Guid InitiatorUserId { get; }

    /// <summary>The chat lives as long as this sign-in session (owner decision).</summary>
    public Guid InitiatorSessionId { get; }

    public Guid InviteeUserId { get; }

    /// <summary>Organization context, when the chat was started from an organization's member directory.</summary>
    public Guid? OrganizationId { get; }

    public PrivateChatStatus Status { get; internal set; } = PrivateChatStatus.Invited;

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset InvitationExpiresAtUtc { get; }

    public DateTimeOffset? AcceptedAtUtc { get; internal set; }

    public string? EndReason { get; internal set; }

    internal List<PrivateMessage> Messages { get; } = [];

    internal Dictionary<Guid, PrivateFile> Files { get; } = [];

    internal long NextSequence { get; set; } = 1;

    public long UsedBytes => Files.Values.Sum(f => f.Size);

    public bool Involves(Guid userId) => userId == InitiatorUserId || userId == InviteeUserId;

    public Guid CounterpartOf(Guid userId) => userId == InitiatorUserId ? InviteeUserId : InitiatorUserId;

    /// <summary>Forgets everything: message texts and file bytes are overwritten before release.</summary>
    internal void Wipe()
    {
        foreach (var file in Files.Values)
        {
            Array.Clear(file.Data);
        }

        Files.Clear();
        Messages.Clear();
    }
}

/// <summary>Process-local store of the running private chats. A restart ends them all; nothing is persisted.</summary>
public sealed class PrivateChatRegistry
{
    private readonly ConcurrentDictionary<Guid, PrivateChat> _chats = new();

    public PrivateChat? Find(Guid id) => _chats.TryGetValue(id, out var chat) ? chat : null;

    public IReadOnlyList<PrivateChat> All() => _chats.Values.ToList();

    internal void Add(PrivateChat chat) => _chats[chat.Id] = chat;

    internal bool Remove(Guid id) => _chats.TryRemove(id, out _);
}

// ---- Realtime events ---------------------------------------------------------------------------

/// <summary>A private chat was created (invitation), accepted or ended. Clients reload it, or show why it ended.</summary>
public sealed record PrivateChatChanged(Guid ChatId, string Status, string? Reason) : IRealtimeEvent;

/// <summary>A message arrived in a private chat. Not stored anywhere; clients keep it only in memory.</summary>
public sealed record PrivateMessageCreated(Guid ChatId, PrivateMessageView Message) : IRealtimeEvent;

public sealed record PrivateFileView(Guid Id, string FileName, string ContentType, FileKind Kind, long Size);

public sealed record PrivateMessageView(Guid Id, long Sequence, Users.UserSummary? Author, Guid AuthorUserId, string ClientMessageId, string Body,
    IReadOnlyList<PrivateFileView> Attachments, DateTimeOffset CreatedAtUtc);

public sealed record PrivateChatView(
    PrivateChat Chat, bool IsInitiator, Users.UserSummary? Counterpart, Users.UserSummary? Initiator, long UsedBytes, long MaxBytes, long MaxFileSize);

public sealed record PrivateChatDetailView(PrivateChatView Summary, IReadOnlyList<PrivateMessageView> Messages);
