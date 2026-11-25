using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Domain.Conversations;

namespace Audorix.KnockTwice.Application.Conversations;

/// <summary>A pinned message with who pinned it and when.</summary>
public sealed record PinView(HistoryEntryView Entry, UserSummary? PinnedBy, DateTimeOffset PinnedAtUtc);

/// <summary>A saved message as the person sees it.</summary>
public sealed record SavedItem(Conversation Conversation, HistoryEntryView Entry, DateTimeOffset SavedAtUtc);

/// <param name="NextBefore">Pass as <c>before</c> for older saved messages.</param>
public sealed record SavedPage(IReadOnlyList<SavedItem> Items, long? NextBefore, bool HasMore);

/// <summary>A saved message as stored, for the saved list.</summary>
public sealed record SavedRecord(ConversationEvent Entry, long SavedTicks);

public sealed partial class ConversationService
{
    // ---- Pins (Phase 18, ADR-032) -----------------------------------------------------------

    /// <summary>
    /// Pins or unpins a message. Owner decision: in direct conversations either person, in groups and
    /// channels owners and administrators; at most <see cref="MessagePin.MaxPerConversation"/>; not in
    /// website conversations. Pinning records a "pinned" entry in the history.
    /// </summary>
    public async Task SetPinnedAsync(Guid userId, Guid conversationId, Guid messageId, bool pinned, CancellationToken cancellationToken)
    {
        var changed = false;
        long lastSequence = 0;
        await MutateAsync(async () =>
        {
            changed = false;
            var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
            if (conversation.Type == ConversationType.Website)
            {
                throw new RequestValidationException("messageId", "pins_unavailable");
            }

            if (conversation.Type != ConversationType.Direct && !me.Can(ConversationPermission.ManageSettings))
            {
                throw new ForbiddenException();
            }

            var message = await store.FindEntryAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false);
            if (message is not { IsMessage: true } || message.Sequence < me.VisibleFromSequence)
            {
                throw new ResourceNotFoundException("message_not_found");
            }

            var existing = await store.FindPinAsync(messageId, cancellationToken).ConfigureAwait(false);
            if (pinned && existing is null)
            {
                if (await store.CountPinsAsync(conversationId, cancellationToken).ConfigureAwait(false) >= MessagePin.MaxPerConversation)
                {
                    throw new ConflictException("too_many_pins");
                }

                var now = time.GetUtcNow();
                store.Add(MessagePin.Create(Guid.CreateVersion7(now), message, userId, now));
                store.Add(ConversationEvent.RecordPinned(Guid.CreateVersion7(now), conversation, userId, message, now));
                changed = true;
            }
            else if (!pinned && existing is not null)
            {
                store.Remove(existing);
                changed = true;
            }

            lastSequence = conversation.LastSequence;
        }, cancellationToken).ConfigureAwait(false);

        if (changed)
        {
            var recipients = await store.ListRecipientsAsync(conversationId, long.MaxValue, cancellationToken).ConfigureAwait(false);
            await publisher.PublishToUsersAsync(recipients, new ConversationUpdated(conversationId, "pins", lastSequence), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The conversation's pinned messages the caller can see, most recently pinned first.</summary>
    public async Task<IReadOnlyList<PinView>> ListPinsAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var (_, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var pins = await store.ListPinsAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var entries = (await store.FindEntriesAsync(pins.Select(p => p.EntryId).ToList(), cancellationToken).ConfigureAwait(false))
            .Where(e => e is { IsMessage: true, IsDeleted: false } && e.Sequence >= me.VisibleFromSequence)
            .ToDictionary(e => e.Id);
        var visible = pins.Where(p => entries.ContainsKey(p.EntryId)).ToList();
        var views = await WithCallerStateAsync(userId, await DescribeAsync(visible.Select(p => entries[p.EntryId]).ToList(), cancellationToken).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
        var pinners = await directory.GetSummariesAsync(visible.Select(p => p.PinnedByUserId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        return visible.Zip(views, (p, v) => new PinView(v, pinners.GetValueOrDefault(p.PinnedByUserId), new DateTimeOffset(p.PinnedTicks, TimeSpan.Zero))).ToList();
    }

    // ---- Saved messages (Phase 18) ----------------------------------------------------------

    /// <summary>Saves or unsaves a message for the caller (a private bookmark).</summary>
    public async Task SetSavedAsync(Guid userId, Guid conversationId, Guid messageId, bool saved, CancellationToken cancellationToken)
    {
        var (_, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var message = await store.FindEntryAsync(conversationId, messageId, cancellationToken).ConfigureAwait(false);
        if (message is not { IsMessage: true } || message.Sequence < me.VisibleFromSequence)
        {
            throw new ResourceNotFoundException("message_not_found");
        }

        var existing = await store.FindSavedAsync(userId, messageId, cancellationToken).ConfigureAwait(false);
        if (saved && existing is null)
        {
            if (await store.CountSavedAsync(userId, cancellationToken).ConfigureAwait(false) >= SavedMessage.MaxPerUser)
            {
                throw new ConflictException("too_many_saved");
            }

            store.Add(SavedMessage.Create(Guid.CreateVersion7(time.GetUtcNow()), message, userId, time.GetUtcNow()));
        }
        else if (!saved && existing is not null)
        {
            store.Remove(existing);
        }
        else
        {
            return;
        }

        // A concurrent identical change (double click) lost the race: the result is the same.
        await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The caller's saved messages they can still read, most recently saved first.</summary>
    public async Task<SavedPage> ListSavedAsync(Guid userId, long? before, int? limit, CancellationToken cancellationToken)
    {
        var size = limit ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
        {
            throw new RequestValidationException("limit", "limit_out_of_range");
        }

        var records = await store.ListSavedForUserAsync(userId, before ?? long.MaxValue, size + 1, cancellationToken).ConfigureAwait(false);
        var hasMore = records.Count > size;
        var page = records.Take(size).ToList();
        var allowed = new Dictionary<Guid, Conversation>();
        foreach (var id in page.Select(r => r.Entry.ConversationId).Distinct())
        {
            try
            {
                var (conversation, _) = await RequireAccessAsync(userId, id, cancellationToken).ConfigureAwait(false);
                allowed[id] = conversation;
            }
            catch (ResourceNotFoundException)
            {
                // Owner decision: saved messages show only while they can be read.
            }
        }

        var visible = page.Where(r => allowed.ContainsKey(r.Entry.ConversationId)).ToList();
        var views = await WithCallerStateAsync(userId, await DescribeAsync(visible.Select(r => r.Entry).ToList(), cancellationToken).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
        var items = visible.Zip(views, (r, v) => new SavedItem(allowed[r.Entry.ConversationId], v, new DateTimeOffset(r.SavedTicks, TimeSpan.Zero))).ToList();
        return new SavedPage(items, hasMore ? page[^1].SavedTicks : null, hasMore);
    }
}
