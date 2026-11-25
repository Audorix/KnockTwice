using System.Text.RegularExpressions;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Domain.Conversations;

namespace Audorix.KnockTwice.Application.Conversations;

/// <summary>Finds <c>@everyone</c> and <c>@here</c> in message text (Phase 17).</summary>
public static partial class MentionText
{
    [GeneratedRegex(@"(?<![\p{L}\p{N}_@])@(everyone|here)(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex Broadcast();

    public static (bool Everyone, bool Here) FindBroadcasts(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return (false, false);
        }

        var everyone = false;
        var here = false;
        foreach (Match match in Broadcast().Matches(body))
        {
            if (string.Equals(match.Groups[1].Value, "everyone", StringComparison.OrdinalIgnoreCase))
            {
                everyone = true;
            }
            else
            {
                here = true;
            }
        }

        return (everyone, here);
    }
}

/// <summary>Who a message mentions: people by name, and whether it notified everyone or those online.</summary>
public sealed record MentionsView(IReadOnlyList<Guid> UserIds, bool Everyone, bool Here);

/// <summary>A mention of one person as stored, for the Mentions view.</summary>
public sealed record MentionRecord(ConversationEvent Entry, bool Unread);

public sealed record MentionItem(Conversation Conversation, HistoryEntryView Entry, bool Unread);

/// <param name="NextBefore">Pass as <c>before</c> for older mentions.</param>
public sealed record MentionPage(IReadOnlyList<MentionItem> Items, long? NextBefore, bool HasMore);

/// <summary>
/// The Mentions view (Phase 17, ADR-031): messages that mention the caller, newest first, in
/// conversations they can still read. A mention is unread while its message is unread (in the
/// conversation, or for a thread reply kept in the thread, in the thread).
/// </summary>
public sealed class MentionService(IConversationStore store, ConversationService conversations)
{
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 100;

    public async Task<MentionPage> ListAsync(Guid userId, long? before, int? limit, CancellationToken cancellationToken)
    {
        var size = limit ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
        {
            throw new RequestValidationException("limit", "limit_out_of_range");
        }

        var records = await store.ListMentionsForUserAsync(userId, before ?? long.MaxValue, size + 1, cancellationToken).ConfigureAwait(false);
        var hasMore = records.Count > size;
        var page = records.Take(size).ToList();

        // Access is checked again per conversation (for example organization membership).
        var allowed = new Dictionary<Guid, Conversation>();
        foreach (var id in page.Select(r => r.Entry.ConversationId).Distinct())
        {
            try
            {
                var (conversation, _) = await conversations.RequireAccessAsync(userId, id, cancellationToken).ConfigureAwait(false);
                allowed[id] = conversation;
            }
            catch (ResourceNotFoundException)
            {
                // No longer readable: left out.
            }
        }

        var visible = page.Where(r => allowed.ContainsKey(r.Entry.ConversationId)).ToList();
        var views = await conversations.WithCallerStateAsync(userId,
            await conversations.DescribeAsync(visible.Select(r => r.Entry).ToList(), cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        var items = visible.Zip(views, (r, v) => new MentionItem(allowed[r.Entry.ConversationId], v, r.Unread)).ToList();
        return new MentionPage(items, hasMore ? page[^1].Entry.CreatedAtUtc.UtcTicks : null, hasMore);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) => store.CountUnreadMentionsAsync(userId, cancellationToken);
}
