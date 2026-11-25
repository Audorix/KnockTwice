using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Domain.Conversations;

namespace Audorix.KnockTwice.Application.Conversations;

/// <summary>A thread as the caller sees it: the first message, a page of replies (ascending) and their own state.</summary>
public sealed record ThreadPage(HistoryEntryView Root, IReadOnlyList<HistoryEntryView> Items, long? NextBeforeSequence, bool HasMore, bool Following, long LastReadSequence);

public sealed partial class ConversationService
{
    /// <summary>
    /// A thread (Phase 16, owner decision: side panel): its first message and its replies, newest page
    /// first, within the caller's visible history.
    /// </summary>
    public async Task<ThreadPage> GetThreadAsync(Guid userId, Guid conversationId, Guid rootId, long? beforeSequence, int? limit, CancellationToken cancellationToken)
    {
        var size = limit ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
        {
            throw new RequestValidationException("limit", "limit_out_of_range");
        }

        var (_, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var root = await RequireThreadRootAsync(conversationId, rootId, me, cancellationToken).ConfigureAwait(false);
        var page = await store.ListThreadAsync(conversationId, rootId, me.VisibleFromSequence, beforeSequence ?? long.MaxValue, size + 1, cancellationToken)
            .ConfigureAwait(false);
        var hasMore = page.Count > size;
        var replies = page.Take(size).OrderBy(e => e.Sequence).ToList();
        var views = await DescribeAsync([root, .. replies], cancellationToken).ConfigureAwait(false);
        var withState = await WithCallerStateAsync(userId, views, cancellationToken).ConfigureAwait(false);
        var follow = await store.FindThreadFollowAsync(rootId, userId, cancellationToken).ConfigureAwait(false);
        return new ThreadPage(withState[0], withState.Skip(1).ToList(), hasMore ? replies[0].Sequence : null, hasMore,
            follow?.Following ?? false, follow?.LastReadSequence ?? root.Sequence);
    }

    /// <summary>Moves the caller's read position in the thread forward; returns the replies still unread.</summary>
    public async Task<int> MarkThreadReadAsync(Guid userId, Guid conversationId, Guid rootId, long sequence, CancellationToken cancellationToken)
    {
        var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var root = await RequireThreadRootAsync(conversationId, rootId, me, cancellationToken).ConfigureAwait(false);
        var target = Math.Min(sequence, conversation.LastSequence);
        var follow = await store.FindThreadFollowAsync(rootId, userId, cancellationToken).ConfigureAwait(false);
        if (follow is null)
        {
            follow = ThreadFollow.Start(Guid.CreateVersion7(time.GetUtcNow()), root, userId, target, following: false, time.GetUtcNow());
            store.Add(follow);
        }
        else
        {
            follow.MarkRead(target);
        }

        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        return (await store.CountThreadUnreadAsync(userId, [follow], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(rootId);
    }

    /// <summary>Follows or stops following a thread (followers are notified about its replies).</summary>
    public async Task<bool> SetThreadFollowingAsync(Guid userId, Guid conversationId, Guid rootId, bool following, CancellationToken cancellationToken)
    {
        var (_, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var root = await RequireThreadRootAsync(conversationId, rootId, me, cancellationToken).ConfigureAwait(false);
        var follow = await store.FindThreadFollowAsync(rootId, userId, cancellationToken).ConfigureAwait(false);
        if (follow is null)
        {
            store.Add(ThreadFollow.Start(Guid.CreateVersion7(time.GetUtcNow()), root, userId, root.Sequence, following, time.GetUtcNow()));
        }
        else
        {
            follow.SetFollowing(following);
        }

        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        return following;
    }

    /// <summary>A message the caller can see that can carry a thread (a message, not itself a thread reply).</summary>
    internal async Task<ConversationEvent> RequireThreadRootAsync(Guid conversationId, Guid rootId, ConversationParticipant me, CancellationToken cancellationToken)
    {
        var root = await store.FindEntryAsync(conversationId, rootId, cancellationToken).ConfigureAwait(false);
        return root is { IsMessage: true, IsThreadReply: false } && root.Sequence >= me.VisibleFromSequence
            ? root
            : throw new ResourceNotFoundException("message_not_found");
    }
}
