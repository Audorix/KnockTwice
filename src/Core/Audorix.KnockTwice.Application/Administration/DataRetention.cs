using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Identity;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Application.Administration;

/// <summary>
/// Data retention (Phase 13, owner decisions of 2026-11-23), bound from <c>Retention</c>. Every period
/// is in days; 0 switches that rule off.
/// </summary>
public sealed class RetentionSettings
{
    public const string SectionName = "Retention";

    /// <summary>Anonymous website visitors not seen for this long are deleted (their conversations stay).</summary>
    public int VisitorInactiveDays { get; set; } = 90;

    /// <summary>Accounts whose email was never confirmed are deleted after this long (only while confirmation is required).</summary>
    public int UnconfirmedAccountDays { get; set; } = 30;

    /// <summary>Messages (and their files) older than this are deleted. Off by default.</summary>
    public int MessageDays { get; set; }

    /// <summary>
    /// Phase 21: sign-in sessions that ended (expired or signed out) this long ago are deleted,
    /// with the push subscriptions of those browsers.
    /// </summary>
    public int ClosedSessionDays { get; set; } = 30;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (VisitorInactiveDays is < 0 or > 3650)
        {
            errors.Add("Retention:VisitorInactiveDays must be 0 (off) to 3650.");
        }

        if (UnconfirmedAccountDays is < 0 or > 3650)
        {
            errors.Add("Retention:UnconfirmedAccountDays must be 0 (off) to 3650.");
        }

        if (MessageDays is < 0 or > 36500)
        {
            errors.Add("Retention:MessageDays must be 0 (off) to 36500.");
        }

        if (ClosedSessionDays is < 0 or > 3650)
        {
            errors.Add("Retention:ClosedSessionDays must be 0 (off) to 3650.");
        }

        return errors;
    }
}

/// <summary>A message past the retention period.</summary>
public sealed record ExpiredMessage(Guid ConversationId, Guid EntryId);

/// <summary>Message retention persistence (implemented by Infrastructure).</summary>
public interface IMessageRetentionStore
{
    /// <summary>Messages created before <paramref name="cutoff"/>, oldest first.</summary>
    Task<IReadOnlyList<ExpiredMessage>> ListExpiredMessagesAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken);

    /// <summary>Storage keys of every file attached to the messages, whatever their state.</summary>
    Task<IReadOnlyList<(Guid EntryId, string StorageKey)>> ListAttachmentKeysAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the messages and their attachment records in one transaction; replies to them keep
    /// their own text and lose the quote. Returns how many messages were deleted.
    /// </summary>
    Task<int> PurgeMessagesAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);
}

public sealed record RetentionResult(int Visitors, int UnconfirmedAccounts, int Messages, int Sessions = 0)
{
    public bool Any => Visitors + UnconfirmedAccounts + Messages + Sessions > 0;
}

/// <summary>
/// Applies <see cref="RetentionSettings"/>. Runs in the background every few hours and from the
/// command line; safe to repeat. Message retention removes file content before the records, and a
/// message whose file cannot be removed right now stays until the next run.
/// </summary>
public sealed class DataRetentionService(
    IAccountErasure erasure,
    AccountDeletionService deletion,
    IMessageRetentionStore messages,
    Sessions.ISessionStore sessions,
    IAttachmentStorage storage,
    AuditService audit,
    IOptionsSnapshot<RetentionSettings> settings,
    IOptionsSnapshot<AccountSettings> accounts)
{
    private const int Batch = 200;

    public async Task<RetentionResult> ApplyAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var options = settings.Value;
        var visitors = options.VisitorInactiveDays > 0
            ? await DeleteAccountsAsync(c => erasure.ListInactiveAnonymousVisitorsAsync(c, Batch, cancellationToken), nowUtc.AddDays(-options.VisitorInactiveDays), cancellationToken)
                .ConfigureAwait(false)
            : 0;

        // Without required confirmation an unconfirmed account is a working account.
        var unconfirmed = options.UnconfirmedAccountDays > 0 && accounts.Value.RequireConfirmedEmail
            ? await DeleteAccountsAsync(c => erasure.ListStaleUnconfirmedAsync(c, Batch, cancellationToken), nowUtc.AddDays(-options.UnconfirmedAccountDays), cancellationToken)
                .ConfigureAwait(false)
            : 0;

        var purged = options.MessageDays > 0 ? await PurgeMessagesAsync(nowUtc.AddDays(-options.MessageDays), cancellationToken).ConfigureAwait(false) : 0;

        // Phase 21: ended sign-in sessions (they were never purged before) and their push subscriptions.
        var sessionsPurged = options.ClosedSessionDays > 0
            ? await sessions.PurgeClosedAsync(nowUtc.AddDays(-options.ClosedSessionDays), cancellationToken).ConfigureAwait(false)
            : 0;

        var result = new RetentionResult(visitors, unconfirmed, purged, sessionsPurged);
        if (result.Any)
        {
            await audit.RecordAsync(AuditActions.RetentionApplied, null, details: new Dictionary<string, object?>
            {
                ["visitors"] = visitors,
                ["unconfirmedAccounts"] = unconfirmed,
                ["messages"] = purged,
                ["sessions"] = sessionsPurged,
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<int> DeleteAccountsAsync(Func<DateTimeOffset, Task<IReadOnlyList<Guid>>> list, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var skipped = new HashSet<Guid>();
        while (true)
        {
            var candidates = (await list(cutoff).ConfigureAwait(false)).Where(id => !skipped.Contains(id)).ToList();
            if (candidates.Count == 0)
            {
                return deleted;
            }

            foreach (var userId in candidates)
            {
                if (await deletion.DeleteForRetentionAsync(userId, cancellationToken).ConfigureAwait(false))
                {
                    deleted++;
                }
                else
                {
                    skipped.Add(userId);
                }
            }
        }
    }

    private async Task<int> PurgeMessagesAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var total = 0;
        var blocked = new HashSet<Guid>();
        while (true)
        {
            var expired = (await messages.ListExpiredMessagesAsync(cutoff, Batch + blocked.Count, cancellationToken).ConfigureAwait(false))
                .Where(m => !blocked.Contains(m.EntryId))
                .Take(Batch)
                .ToList();
            if (expired.Count == 0)
            {
                return total;
            }

            var ids = expired.Select(m => m.EntryId).ToList();
            var ready = ids.ToHashSet();
            foreach (var (entryId, key) in await messages.ListAttachmentKeysAsync(ids, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await storage.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
                }
                catch (StorageUnavailableException)
                {
                    // Keep the message and its file record; the next run tries again.
                    ready.Remove(entryId);
                    blocked.Add(entryId);
                }
            }

            if (ready.Count == 0)
            {
                return total; // storage unavailable: the next run continues
            }

            total += await messages.PurgeMessagesAsync(ready, cancellationToken).ConfigureAwait(false);
        }
    }
}
