# Phase 17 — Mentions: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Scope: [Implementation, Phase 17](../IMPLEMENTATION.md#phase-17--mentions). Applicable ADRs:
031 (new), 029 (notifications), 030 (threads), 023 (read positions).

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 17 is | Mentions (over bots and webhooks, calls, or message tools) |
| Who may use `@everyone` / `@here` | Anyone in groups and channels of up to 20 people; above that only owners and administrators. `@here` = people online |
| Notification levels | All messages / Only mentions / Nothing; default All; existing mutes become Only mentions, so mentions break through; Nothing silences mentions too |
| Where mentions show | A Mentions view (no highlighting in the timeline, no separate mention email) |

## Delivered

| Area | What |
| --- | --- |
| Domain | `NotificationLevel` on participants (replaces `Muted`, which stays as "not All"); `WantsNotification(mentioned)`; `MessageMention` (named, everyone or here; one row per person). |
| Application | Mention resolution when sending (participants only, limits, size/role rule for broadcasts, `@here` from presence); `MentionText.FindBroadcasts`; mentions removed with the message; `MentionService` (list with access re-check, unread count); `SetNotificationLevelAsync`; history entries carry who they mention. |
| Notifications | Push and email follow the level; mentions reach people who do not follow the thread they were written in. |
| API | `mentionUserIds` when sending; `mentions` on entries; `notificationLevel` on summaries and in `PUT …/settings` (`muted` still accepted); `GET /api/v1/mentions`, `GET /api/v1/mentions/unread`; settings `Messaging:EveryoneMentionLimit` and `Messaging:MaxMentions`. Error texts in English and Spanish. |
| Web app | Suggestions after `@` in the conversation and thread composers (keyboard and mouse, `@everyone`/`@here` where allowed); the Mute button became a notifications selector; "mentions only" / "muted" in the list; Mentions page and navigation item with badge; in-app notifications follow the level ("Ana mentioned you"). English and Spanish. |
| Migration `AddMentions` | Both databases; muted participants become `Mentions` before the old column is dropped. |
| Documentation | ADR-031, Implementation Phase 17, API guide, configuration, testing guide, changelog. |

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| Only participants can be mentioned; `@everyone` / `@here` follow the size and role rule | **Pass** (SQLite and PostgreSQL). |
| Push and email follow the level | **Pass** for email on both databases (a person on "only mentions" receives just the mention, one on "nothing" nothing). Push uses the same rule (`WantsNotification`, unit-tested); no separate push test. |
| The Mentions view lists only readable messages with correct unread state | **Pass** (both databases): unread until read, gone after deletion or leaving. |

## Validation results

- Full .NET run (Release, both databases): 414/414, including `MentionsTests` 4/4 and
  `MentionTextTests` 14/14.
- Web tests 65/65 (composer suggestions and level selector, Mentions page); type-check clean.
- Release build without warnings; OpenAPI re-exported (137 paths); no secrets in the repository.
- Not checked in a browser this phase (the local check environment dropped requests in Phase 16).

## Open items

- Mention names are not highlighted in messages (owner decision); a later phase could add it.
- Opening a mention goes to the conversation, not straight to the message or its thread.
- `@everyone` in a very large channel writes one row per participant.
- Not included: user groups (`@team`), keyword alerts, mention-specific email timing.
