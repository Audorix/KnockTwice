using System.Text.Json;
using Audorix.ChatX.Application.Abstractions;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Conversations;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Domain.Notifications;
using Audorix.ChatX.Domain.Users;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Notifications;

/// <summary>
/// Notifications (Phase 15, owner decisions of 2026-11-23), bound from <c>Notifications</c>: email
/// about messages that stay unread, and Web Push to browsers while ChatX is closed.
/// </summary>
public sealed class NotificationSettings
{
    public const string SectionName = "Notifications";

    /// <summary>Live switch for notification emails (people choose batched, daily or off).</summary>
    public bool EmailEnabled { get; set; } = true;

    /// <summary>Live switch for Web Push.</summary>
    public bool PushEnabled { get; set; } = true;

    /// <summary>A message is emailed only after it stayed unread this long.</summary>
    public TimeSpan EmailDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>At most one notification email per person per interval ("batched").</summary>
    public TimeSpan EmailInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How often the dispatcher runs (push is sent on the next run after a message).</summary>
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The "subject" of VAPID (RFC 8292): a mailto: or https: contact for push services. Defaults to App:PublicBaseUrl.</summary>
    public string? PushContact { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (EmailDelay < TimeSpan.Zero || EmailDelay > TimeSpan.FromDays(1))
        {
            errors.Add("Notifications:EmailDelay must be between 0 and 1 day.");
        }

        if (EmailInterval < TimeSpan.Zero || EmailInterval > TimeSpan.FromDays(7))
        {
            errors.Add("Notifications:EmailInterval must be between 0 and 7 days.");
        }

        if (DispatchInterval < TimeSpan.FromMilliseconds(100) || DispatchInterval > TimeSpan.FromMinutes(10))
        {
            errors.Add("Notifications:DispatchInterval must be between 100 milliseconds and 10 minutes.");
        }

        if (!string.IsNullOrWhiteSpace(PushContact) && !PushContact.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            && !PushContact.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Notifications:PushContact must be a mailto: or https: URL.");
        }

        return errors;
    }
}

/// <summary>An unread message somebody has not been emailed about yet.</summary>
public sealed record PendingNotice(Guid UserId, Guid ConversationId, Guid EntryId, long Sequence, Guid AuthorId, string? Body, DateTimeOffset CreatedAtUtc);

public sealed record PushQueueEntry(long Id, Guid EntryId);

/// <summary>Notification persistence (implemented by Infrastructure).</summary>
public interface INotificationStore
{
    /// <summary>
    /// Unread messages, created in (<paramref name="createdAfter"/>, <paramref name="createdBefore"/>],
    /// that their recipients have not been emailed about: active participants who did not write
    /// them, whose notification level asks for them (all messages, or only those mentioning them;
    /// never with "nothing"), with email notifications on and a confirmed address. In support conversations
    /// only the assignee (or, unassigned, everyone taking part).
    /// </summary>
    Task<IReadOnlyList<PendingNotice>> ListPendingEmailAsync(DateTimeOffset createdAfter, DateTimeOffset createdBefore, int limit, CancellationToken cancellationToken);

    Task MarkNotifiedAsync(Guid userId, IReadOnlyDictionary<Guid, long> sequences, CancellationToken cancellationToken);

    /// <summary>New messages to push (added with the message; see the database context).</summary>
    Task<IReadOnlyList<PushQueueEntry>> TakePushAsync(int limit, CancellationToken cancellationToken);

    Task CompletePushAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<PushSubscription>> ListSubscriptionsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<PushSubscription?> FindSubscriptionAsync(string endpoint, CancellationToken cancellationToken);

    void Add(PushSubscription subscription);

    void Remove(PushSubscription subscription);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public enum PushOutcome
{
    Delivered = 1,

    /// <summary>The push service says the subscription no longer exists (404/410): drop it.</summary>
    Gone = 2,

    /// <summary>Temporary failure (network, 429, 5xx).</summary>
    Failed = 3,
}

/// <summary>Web Push delivery (RFC 8030) with message encryption (RFC 8291) and VAPID (RFC 8292).</summary>
public interface IWebPushSender
{
    /// <summary>The installation's VAPID public key (uncompressed P-256 point, base64url), created on first use.</summary>
    Task<string> GetPublicKeyAsync(CancellationToken cancellationToken);

    Task<PushOutcome> SendAsync(PushSubscription subscription, string payload, CancellationToken cancellationToken);
}

/// <summary>What a push notification shows (the service worker renders it).</summary>
public sealed record PushPayload(string Title, string Body, string Url, string Tag);

public sealed record NotificationRunResult(int Emails, int Pushes)
{
    public bool Any => Emails + Pushes > 0;
}

/// <summary>
/// Sends notifications (Phase 15). Email: messages still unread <see cref="NotificationSettings.EmailDelay"/>
/// after they arrived, for people who are not online, batched into one email at most every
/// <see cref="NotificationSettings.EmailInterval"/> (or once a day for "daily"); each message is
/// emailed once. Push: every new message to its recipients' browsers while they have no open
/// ChatX connection. Both honour mute, the preview setting, deleted accounts and access.
/// </summary>
public sealed class NotificationService(
    INotificationStore store,
    IConversationStore conversations,
    IUserDirectory directory,
    IUserProfileStore profiles,
    IUserAccountManager accounts,
    IEmailSender email,
    NotificationEmailComposer composer,
    IWebPushSender push,
    PresenceDirectory presence,
    Sessions.ISessionStore sessions,
    TimeProvider time,
    IOptionsSnapshot<NotificationSettings> settings,
    IOptions<PublicUrlSettings> urls)
{
    private const int Batch = 200;

    public async Task<NotificationRunResult> RunAsync(bool includeEmail, CancellationToken cancellationToken)
    {
        var pushes = await SendPushAsync(cancellationToken).ConfigureAwait(false);
        var emails = includeEmail ? await SendEmailsAsync(cancellationToken).ConfigureAwait(false) : 0;
        return new NotificationRunResult(emails, pushes);
    }

    // ---- Email ---------------------------------------------------------------------------------

    public async Task<int> SendEmailsAsync(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.EmailEnabled)
        {
            return 0;
        }

        var now = time.GetUtcNow();
        var pending = await store.ListPendingEmailAsync(now.AddDays(-7), now - options.EmailDelay, 5000, cancellationToken).ConfigureAwait(false);
        var sent = 0;
        foreach (var person in pending.GroupBy(p => p.UserId))
        {
            if (await presence.IsOnlineAsync(person.Key, cancellationToken).ConfigureAwait(false))
            {
                continue; // they see it in the app
            }

            var profile = await profiles.FindAsync(person.Key, cancellationToken).ConfigureAwait(false);
            var account = await accounts.FindByIdAsync(person.Key, cancellationToken).ConfigureAwait(false);
            if (profile is null || profile.IsDeleted || account is not { EmailConfirmed: true, IsDeleted: false, IsVisitor: false }
                || profile.EmailNotifications == UserProfile.EmailOff)
            {
                continue;
            }

            var wait = profile.EmailNotifications == UserProfile.EmailDaily ? TimeSpan.FromDays(1) : options.EmailInterval;
            if (profile.LastNotificationEmailAtUtc is { } last && now - last < wait)
            {
                continue; // batched into the next email
            }

            var items = await DescribeAsync(person.ToList(), profile.NotificationPreviews, cancellationToken).ConfigureAwait(false);
            if (items.Count == 0)
            {
                continue;
            }

            try
            {
                await email.SendAsync(composer.UnreadMessages(account.Email, profile.DisplayName, profile.UiLanguage, items,
                    urls.Value.Link("conversations"), urls.Value.Link("account")), cancellationToken).ConfigureAwait(false);
            }
            catch (EmailDeliveryException)
            {
                continue; // nothing is marked: the next run tries again
            }

            await store.MarkNotifiedAsync(person.Key, person.GroupBy(p => p.ConversationId).ToDictionary(g => g.Key, g => g.Max(p => p.Sequence)), cancellationToken)
                .ConfigureAwait(false);
            profile.NotificationEmailSent(now);
            await profiles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            sent++;
        }

        return sent;
    }

    private async Task<IReadOnlyList<UnreadItem>> DescribeAsync(IReadOnlyList<PendingNotice> notices, bool previews, CancellationToken cancellationToken)
    {
        var authors = await directory.GetSummariesAsync(notices.Select(n => n.AuthorId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var items = new List<UnreadItem>();
        foreach (var group in notices.GroupBy(n => n.ConversationId))
        {
            var conversation = await conversations.FindAsync(group.Key, cancellationToken).ConfigureAwait(false);
            if (conversation is null)
            {
                continue;
            }

            var latest = group.OrderByDescending(n => n.Sequence).First();
            var author = authors.GetValueOrDefault(latest.AuthorId);
            var title = conversation.Type == ConversationType.Direct ? author?.DisplayName ?? string.Empty : conversation.Title ?? string.Empty;
            items.Add(new UnreadItem(title, author?.DisplayName ?? string.Empty, group.Count(), previews ? Excerpt(latest.Body) : null, latest.CreatedAtUtc));
        }

        return items.OrderByDescending(i => i.LatestAtUtc).ToList();
    }

    // ---- Push ----------------------------------------------------------------------------------

    public async Task<int> SendPushAsync(CancellationToken cancellationToken)
    {
        var taken = await store.TakePushAsync(Batch, cancellationToken).ConfigureAwait(false);
        if (taken.Count == 0)
        {
            return 0;
        }

        var sent = 0;
        if (settings.Value.PushEnabled)
        {
            var entries = await conversations.FindEntriesAsync(taken.Select(t => t.EntryId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
            sent = await PushMessagesAsync(entries.Where(e => e is { IsMessage: true, IsDeleted: false }).ToList(), cancellationToken).ConfigureAwait(false);
        }

        await store.CompletePushAsync(taken.Select(t => t.Id).ToList(), cancellationToken).ConfigureAwait(false);
        return sent;
    }

    /// <summary>
    /// Pushes a batch of new messages (Phase 24: per batch, not per message — each conversation and
    /// its participants are read once, mentions, presence, subscriptions and authors in one go).
    /// </summary>
    private async Task<int> PushMessagesAsync(List<ConversationEvent> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return 0;
        }

        var mentionsByEntry = await conversations.ListMentionedUserIdsAsync(entries.Select(e => e.Id).ToList(), cancellationToken).ConfigureAwait(false);
        var plans = new List<(Conversation Conversation, ConversationEvent Entry, List<Guid> Candidates)>();
        foreach (var group in entries.GroupBy(e => e.ConversationId))
        {
            var conversation = await conversations.FindAsync(group.Key, cancellationToken).ConfigureAwait(false);
            if (conversation is null)
            {
                continue;
            }

            var participants = await conversations.ListActiveParticipantsAsync(conversation.Id, cancellationToken).ConfigureAwait(false);
            foreach (var entry in group)
            {
                // Thread replies that stay in the thread (Phase 16) go only to the thread's followers;
                // mentions (Phase 17) reach the people mentioned unless their level is "nothing".
                var followers = entry.InMainTimeline ? null : (await conversations.ListThreadFollowerIdsAsync(entry.ThreadRootId!.Value, cancellationToken).ConfigureAwait(false)).ToHashSet();
                var mentioned = mentionsByEntry.TryGetValue(entry.Id, out var ids) ? ids.ToHashSet() : [];
                var candidates = participants
                    .Where(p => p.UserId != entry.ActorUserId && !p.IsVisitor && entry.Sequence >= p.VisibleFromSequence)
                    .Where(p => mentioned.Contains(p.UserId)
                        ? p.WantsNotification(mentioned: true)
                        : p.WantsNotification(mentioned: false) && (followers is null || followers.Contains(p.UserId)))
                    .Where(p => conversation.Type != ConversationType.Website || conversation.AssignedToUserId is null || conversation.AssignedToUserId == p.UserId)
                    .Select(p => p.UserId)
                    .ToList();
                if (candidates.Count > 0)
                {
                    plans.Add((conversation, entry, candidates));
                }
            }
        }

        // Online on any instance (Phase 21): the app shows it, no push.
        var everyone = plans.SelectMany(p => p.Candidates).Distinct().ToList();
        if (everyone.Count == 0)
        {
            return 0;
        }

        var online = await presence.OnlineAmongAsync(everyone, cancellationToken).ConfigureAwait(false);
        var offline = everyone.Where(id => !online.Contains(id)).ToList();
        var subscriptions = offline.Count == 0 ? [] : await store.ListSubscriptionsAsync(offline, cancellationToken).ConfigureAwait(false);
        if (subscriptions.Count == 0)
        {
            return 0;
        }

        var byUser = subscriptions.GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => g.ToList());
        var authors = await directory.GetSummariesAsync(plans.Select(p => p.Entry.ActorUserId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var sent = 0;
        foreach (var (conversation, entry, candidates) in plans)
        {
            var authorName = authors.GetValueOrDefault(entry.ActorUserId)?.DisplayName ?? string.Empty;
            foreach (var userId in candidates.Where(byUser.ContainsKey))
            {
                var profile = await profiles.FindAsync(userId, cancellationToken).ConfigureAwait(false);
                if (profile is null || profile.IsDeleted)
                {
                    continue;
                }

                var payload = JsonSerializer.Serialize(BuildPayload(conversation, entry, authorName, profile), PayloadJson);
                sent += await DeliverAsync(byUser[userId], payload, cancellationToken).ConfigureAwait(false);
            }
        }

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return sent;
    }

    /// <summary>Sends one payload to the browsers of one person; drops subscriptions that ended. The caller saves.</summary>
    private async Task<int> DeliverAsync(IEnumerable<PushSubscription> subscriptions, string payload, CancellationToken cancellationToken)
    {
        var sent = 0;
        foreach (var subscription in subscriptions)
        {
            // The browser's sign-in ended (signed out, revoked or expired): it gets nothing more.
            if (await sessions.FindAsync(subscription.SessionId, cancellationToken).ConfigureAwait(false) is not { } session
                || session.UserId != subscription.UserId || !session.IsActive(time.GetUtcNow()))
            {
                store.Remove(subscription);
                continue;
            }

            switch (await push.SendAsync(subscription, payload, cancellationToken).ConfigureAwait(false))
            {
                case PushOutcome.Delivered:
                    subscription.Delivered(time.GetUtcNow());
                    sent++;
                    break;
                case PushOutcome.Gone:
                    store.Remove(subscription);
                    break;
                default:
                    if (subscription.Failed())
                    {
                        store.Remove(subscription);
                    }

                    break;
            }
        }

        return sent;
    }

    // ---- Reminders (Phase 20) ------------------------------------------------------------------

    /// <summary>
    /// A reminder came due (owner decision: in the app, by push and by email). People who are online
    /// see it in the app; otherwise it is pushed and, unless their email notifications are off,
    /// emailed. The preview setting decides whether the text is included.
    /// </summary>
    public async Task NotifyReminderAsync(Guid userId, Conversation conversation, ConversationEvent entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(entry);
        if (await presence.IsOnlineAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            return; // the app shows it
        }

        var profile = await profiles.FindAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is null || profile.IsDeleted)
        {
            return;
        }

        var author = (await directory.GetSummariesAsync([entry.ActorUserId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(entry.ActorUserId);
        var authorName = author?.DisplayName ?? string.Empty;
        var text = profile.NotificationPreviews ? Excerpt(entry.Body) : null;
        var path = $"conversations?id={conversation.Id}&message={entry.Id}" + (entry.InMainTimeline ? string.Empty : $"&thread={entry.ThreadRootId}");
        var options = settings.Value;
        if (options.PushEnabled)
        {
            var spanish = profile.UiLanguage == "es";
            var payload = new PushPayload(
                spanish ? "Recordatorio" : "Reminder",
                text is null ? (spanish ? $"Mensaje de {authorName}" : $"Message from {authorName}") : $"{authorName}: {text}",
                "/" + path,
                $"reminder-{entry.Id}");
            await DeliverAsync(await store.ListSubscriptionsAsync([userId], cancellationToken).ConfigureAwait(false),
                JsonSerializer.Serialize(payload, PayloadJson), cancellationToken).ConfigureAwait(false);
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var account = await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (options.EmailEnabled && profile.EmailNotifications != UserProfile.EmailOff && account is { EmailConfirmed: true, IsDeleted: false, IsVisitor: false })
        {
            var title = conversation.Type == ConversationType.Direct ? authorName : conversation.Title ?? authorName;
            try
            {
                await email.SendAsync(composer.Reminder(account.Email, profile.DisplayName, profile.UiLanguage, title, authorName, text, urls.Value.Link(path)),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (EmailDeliveryException)
            {
                // The reminder still shows in the app; email is best effort.
            }
        }
    }

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    /// <summary>The preview setting decides whether text is included (owner decision); never more than a short excerpt.</summary>
    private static PushPayload BuildPayload(Conversation conversation, ConversationEvent entry, string authorName, UserProfile profile)
    {
        var spanish = profile.UiLanguage == "es";
        var direct = conversation.Type == ConversationType.Direct;
        var title = direct ? authorName : conversation.Title ?? authorName;
        var text = profile.NotificationPreviews ? Excerpt(entry.Body) : null;
        var body = text is null
            ? (spanish ? $"Nuevo mensaje de {authorName}" : $"New message from {authorName}")
            : direct ? text : $"{authorName}: {text}";
        if (string.IsNullOrEmpty(entry.Body) && text is not null)
        {
            body = spanish ? $"{authorName} envió un archivo" : $"{authorName} sent a file";
        }

        return new PushPayload(title, body, $"/conversations?id={conversation.Id}", conversation.Id.ToString());
    }

    /// <summary>Plain text without formatting markers (Phase 19), one line.</summary>
    private static string? Excerpt(string? body) => MessageText.Excerpt(body);

    // ---- Subscriptions -------------------------------------------------------------------------

    public async Task<string?> GetPublicKeyAsync(CancellationToken cancellationToken) =>
        settings.Value.PushEnabled ? await push.GetPublicKeyAsync(cancellationToken).ConfigureAwait(false) : null;

    /// <summary>Registers this browser for the signed-in person (the same browser re-subscribing replaces its keys and owner).</summary>
    public async Task SubscribeAsync(Guid userId, Guid sessionId, string? endpoint, string? p256dh, string? auth, CancellationToken cancellationToken)
    {
        if (!settings.Value.PushEnabled)
        {
            throw new FeatureDisabledException("push_disabled");
        }

        var existing = await store.FindSubscriptionAsync(endpoint?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);
        try
        {
            if (existing is null)
            {
                store.Add(PushSubscription.Create(Guid.CreateVersion7(time.GetUtcNow()), userId, sessionId, endpoint?.Trim() ?? string.Empty, p256dh ?? string.Empty,
                    auth ?? string.Empty, time.GetUtcNow()));
            }
            else
            {
                existing.Renew(userId, sessionId, p256dh ?? string.Empty, auth ?? string.Empty);
            }
        }
        catch (Domain.DomainRuleException ex)
        {
            throw new RequestValidationException(ex.Code == "push_endpoint_invalid" ? "endpoint" : "keys", ex.Code);
        }

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes this browser's subscription if it belongs to the caller (unknown endpoints are ignored).</summary>
    public async Task UnsubscribeAsync(Guid userId, string? endpoint, CancellationToken cancellationToken)
    {
        if (await store.FindSubscriptionAsync(endpoint?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false) is { } subscription && subscription.UserId == userId)
        {
            store.Remove(subscription);
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
