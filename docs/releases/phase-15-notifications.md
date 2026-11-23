# Phase 15 — Notifications: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Scope: [Implementation, Phase 15](../IMPLEMENTATION.md#phase-15--notifications). Applicable ADRs:
029 (new), 023 (read positions), 025 (private chats are never notified), 016 (single instance).

Before this phase, on request, the demo data was enlarged (300 people, 50 companies, 150 groups,
10,000 messages, password `Demo123!`) and a **Clear demo data** action added; see the
[Phase 14 record](phase-14-search.md#demo-data-requested-after-this-phase-before-phase-15).

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 15 is | Notifications |
| When to email | Soon after, batched: still unread after 15 minutes and not online; at most one email per hour; per person daily or off |
| What push shows | Follows the preview setting: the sender, and an excerpt only with previews on |
| What notifies | Direct conversations, groups and channels that are not muted; support conversations notify their assignee |

## Delivered

| Area | What |
| --- | --- |
| Domain | `UserProfile.EmailNotifications` (batched/daily/off) and `LastNotificationEmailAtUtc`; `ConversationParticipant.LastNotifiedSequence`; `PushSubscription` (endpoint, keys, owning user and sign-in session, failure count). |
| Email | `NotificationService.SendEmailsAsync`: pending unread messages (one query over `created_at_ticks`), grouped per person, skipped while they are connected or inside their interval; one localized email (EN/ES, `NotificationEmailComposer`) with up to ten conversations; then marks what was included as notified. Demo accounts and demo conversations are excluded. |
| Push | `push_queue` filled with each new message (EF hook); `NotificationService.SendPushAsync` sends to recipients without an open connection whose browser sign-in is still active. `WebPushCrypto` implements RFC 8291 and RFC 8292 with platform cryptography (verified against the RFC 8291 example); `WebPushSender` keeps the VAPID keys in `web_push_keys` (private key protected by Data Protection). 404/410 drop a subscription; five failures in a row too. |
| API | `GET /api/v1/notifications/push`, `POST …/push/subscriptions`, `POST …/push/subscriptions/remove`; `emailNotifications` in preferences; `NotificationDispatchService` (push every 10 s, email check every minute); live settings for the switches, delay and interval. |
| Web app | Account → Preferences → **When you are away**: email choice, push switch with permission handling; `/sw.js` (shows notifications, opens the conversation; no caching); signing out unsubscribes this browser. English and Spanish. |
| Migration `AddNotifications` | Both databases; existing participants count as notified up to now, profiles default to batched. |
| Documentation | ADR-029; configuration (`Notifications:*`), API guide, testing guide, changelog. |

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| Unread messages are emailed once, batched, honouring the choice and the preview setting | **Pass** (SQLite and PostgreSQL): one email per interval, none for the author or someone with email off, no text without previews, "daily" saved and invalid values refused. |
| Push reaches subscribed browsers only while not connected, and stops at sign-out | **Pass** against a fake push service that decrypts the payload: VAPID header, `aes128gcm`, title/body/link, no text without previews, gone subscriptions dropped, nothing after sign-out. **Not verified with a real browser push service**: the built-in browser used here denies notification permission (the app reports it correctly). |
| Muted conversations, deleted and demo accounts, and people without access are never notified | Covered by the queries and rules (mute, active participation, visible history, account state, demo flags); mute and access are exercised indirectly by the tests above. |

## Validation results

- Notification tests 4/4 (both databases), Web Push unit tests 4/4, web tests 62/62, type-check clean.
- Full .NET run (Release, both databases): 391 of 392 passed. The PostgreSQL demo data test timed
  out at the test client's 100-second limit while the whole suite shared the database server; its
  client now waits 5 minutes (as the web app does) and the demo data tests pass on both databases.

## Open items

- Try push once in Chrome, Edge or Firefox against an HTTPS deployment (subscription, delivery,
  click-through).
- Not included: mentions, per-conversation notification levels, quiet hours, native mobile push,
  email for website visitors.
- Presence is per API instance: a second instance would not know who is online (ADR-016).
