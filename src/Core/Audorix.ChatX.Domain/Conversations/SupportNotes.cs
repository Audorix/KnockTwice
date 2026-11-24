using Audorix.ChatX.Domain.Organizations;

namespace Audorix.ChatX.Domain.Conversations;

/// <summary>
/// An internal note on a support conversation (redesign 2026-11): written by support staff for each
/// other, never shown to the customer. Notes are kept apart from the conversation's history so no
/// message path (history, realtime, notifications, search, the visitor widget) can ever deliver one.
/// </summary>
public sealed class SupportNote
{
    public const int BodyMaxLength = 4000;

    private SupportNote(Guid id, Guid conversationId, Guid authorUserId, string body, DateTimeOffset createdAtUtc)
    {
        Id = id;
        ConversationId = conversationId;
        AuthorUserId = authorUserId;
        Body = body;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Body { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static SupportNote Create(Guid id, Conversation conversation, Guid authorUserId, string? body, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        conversation.RequireWebsite();
        var text = (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (text.Length == 0)
        {
            throw new DomainRuleException("note_body_required", "A note needs some text.");
        }

        if (text.Length > BodyMaxLength || text.Any(c => char.IsControl(c) && c is not '\n' and not '\t'))
        {
            throw new DomainRuleException("note_body_invalid", $"A note may contain at most {BodyMaxLength} characters.");
        }

        return new SupportNote(Require.Id(id), conversation.Id, Require.Id(authorUserId), text, Require.Utc(nowUtc));
    }
}
