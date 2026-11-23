using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Conversations;
using Audorix.ChatX.Domain.Conversations;

namespace Audorix.ChatX.Application.Realtime;

/// <summary>One recorded change: a history entry of a conversation was added or changed (or its reactions).</summary>
public sealed record ChangeRecord(long Id, Guid ConversationId, Guid EntryId, long RecordedTicks);

/// <summary>The change log (Phase 22, ADR-022 second half), written with every change by Infrastructure.</summary>
public interface IChangeFeedStore
{
    /// <summary>The newest change id (0 when there is none).</summary>
    Task<long> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>The oldest change id still kept, or null when the log is empty.</summary>
    Task<long?> OldestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Changes after <paramref name="since"/> in conversations the person is an active participant of,
    /// oldest first, at most <paramref name="limit"/>. Access is re-checked by the caller.
    /// </summary>
    Task<IReadOnlyList<ChangeRecord>> ListForUserAsync(Guid userId, long since, int limit, CancellationToken cancellationToken);

    /// <summary>Removes changes recorded before <paramref name="ticks"/>; returns how many.</summary>
    Task<int> PurgeBeforeAsync(long ticks, CancellationToken cancellationToken);
}

/// <param name="Reset">Too much changed, or the cursor is too old: reload instead of applying changes.</param>
/// <param name="Cursor">Pass as <c>since</c> next time.</param>
/// <param name="ConversationIds">Conversations (the caller can read) with changes.</param>
public sealed record ChangeSet(bool Reset, long Cursor, IReadOnlyList<Guid> ConversationIds, IReadOnlyList<HistoryEntryView> Entries);

/// <summary>
/// Reconnect catch-up (Phase 22, owner decision: up to 500 changes, otherwise reload). A client
/// keeps a cursor; after reconnecting it asks for what changed since, gets the current state of
/// every changed entry it may see, and applies it instead of reloading everything.
/// </summary>
public sealed class ChangeFeedService(IChangeFeedStore store, IConversationStore conversations, ConversationService service, TimeProvider time)
{
    public const int MaxChanges = 500;

    /// <summary>Changes are kept this long; older cursors reload.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>
    /// Ids are assigned when rows are written but become visible when their transaction commits, so a
    /// slower transaction can appear with a lower id later. The returned cursor therefore stops at
    /// changes at least this old; newer ones may be returned again and are applied idempotently.
    /// </summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(5);

    public Task<long> CurrentAsync(CancellationToken cancellationToken) => store.CurrentAsync(cancellationToken);

    public async Task<ChangeSet> ListAsync(Guid userId, long since, CancellationToken cancellationToken)
    {
        if (since < 0)
        {
            throw new RequestValidationException("since", "cursor_invalid");
        }

        var oldest = await store.OldestAsync(cancellationToken).ConfigureAwait(false);
        // The 500 limit counts the caller's own conversations, not the whole installation's traffic.
        var changes = await store.ListForUserAsync(userId, since, MaxChanges + 1, cancellationToken).ConfigureAwait(false);
        if ((oldest is { } first && since < first - 1) || changes.Count > MaxChanges)
        {
            // A gap (changes already purged) or too many: the client reloads, then continues from now.
            return new ChangeSet(true, await store.CurrentAsync(cancellationToken).ConfigureAwait(false), [], []);
        }

        var settled = time.GetUtcNow().Subtract(SettleTime).UtcTicks;
        var cursor = since;
        foreach (var change in changes)
        {
            if (change.RecordedTicks > settled)
            {
                break;
            }

            cursor = change.Id;
        }

        var readable = new List<ConversationEvent>();
        var touched = new List<Guid>();
        foreach (var group in changes.GroupBy(c => c.ConversationId))
        {
            ConversationParticipant me;
            try
            {
                (_, me) = await service.RequireAccessAsync(userId, group.Key, cancellationToken).ConfigureAwait(false);
            }
            catch (ResourceNotFoundException)
            {
                continue; // not (or no longer) the caller's conversation
            }

            touched.Add(group.Key);
            var entries = await conversations.FindEntriesAsync(group.Select(c => c.EntryId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
            readable.AddRange(entries.Where(e => e.ConversationId == group.Key && e.Sequence >= me.VisibleFromSequence));
        }

        var views = readable.Count == 0
            ? []
            : await service.WithCallerStateAsync(userId, await service.DescribeAsync(readable.OrderBy(e => e.Sequence).ToList(), cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        return new ChangeSet(false, cursor, touched, views);
    }

    public Task<int> PurgeAsync(CancellationToken cancellationToken) =>
        store.PurgeBeforeAsync(time.GetUtcNow().Subtract(Retention).UtcTicks, cancellationToken);
}
