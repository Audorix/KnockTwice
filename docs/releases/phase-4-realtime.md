# Phase 4 — Realtime infrastructure: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-22.
Applicable ADRs: 005 (realtime communication), 012 (persistence before publishing — no messages
yet), 013, 016 (scalability), 018 (security), ADR-022 remains Proposed.

## Gate decisions (Implementation §12.2)

| Gate | Decision |
| --- | --- |
| Authenticated connections | The hub requires the `chatx.auth` cookie (server-side session). Every hub call re-validates the session; sign-out and session revocation close that session's connections immediately, and a background check closes any other ended session within `Realtime:SessionCheckInterval`. Browser connections are accepted only from the API's own origin or `App:PublicBaseUrl`. |
| Expiring ephemeral state | Presence and typing state live in memory on one API instance and are never persisted. Typing indicators expire after `Realtime:TypingTimeout` (6 s); users go offline `Realtime:PresenceOfflineGrace` (5 s) after their last connection. |
| Protected fixture until Phase 5 | `IConversationAccess` guards every conversation destination. Production registers `NoConversationAccess` (denies all, because no conversations exist); integration tests use `FixtureConversationAccess`. |
| Presence visibility (product default) | Contacts and people sharing an active organization, never across a block; symmetric. Recorded in the ADR decision register as revisable. |

## Delivered behaviour

- `/hubs/chat` (SignalR, JSON) with versioned contracts in `Audorix.ChatX.Contracts.V1.Realtime`:
  client methods `JoinConversation`, `LeaveConversation`, `SendTypingStatus`, `GetPresence`;
  server events `UserPresenceChanged` and `TypingStatusChanged` (`version: 1`). Errors are
  `HubException`s carrying stable codes (`conversation_not_found`, `unauthorized`).
- Connection lifecycle and multiple devices: every connection of every user is tracked; presence
  changes are published once per user, not per device.
- Presence service: `online` on the first connection, `offline` after the grace period without
  connections; `GetPresence` returns the visible people's status.
- Reconnection: a reconnect within the grace period produces no events; joining is idempotent;
  the web client rejoins its conversations and reloads the presence snapshot after every
  reconnect, so state is replaced, never duplicated.
- Typing indicators: only for joined conversations with access re-checked on every signal; start
  and stop are published, renewals are silent, expiry and disconnects end them.
- Revocation: `RealtimeService.RevokeConversationAccessAsync` removes all of a user's connections
  from a destination (Phase 5 calls it when a member is removed); a failed access re-check does
  the same.
- Application ports (`IRealtimePublisher`, `IRealtimeConnections`, `IPresenceAudience`,
  `IConversationAccess`); SignalR only in Api; EF presence-audience query in Infrastructure.
- Web app: `@microsoft/signalr` 10.0.11 client with automatic and backoff reconnection, a live
  connection badge in the navigation (Live / Reconnecting… / Offline, EN/ES) and presence dots
  with accessible labels for contacts. Vite and nginx proxy `/hubs` with WebSocket upgrade.

## Migration impact

None. Presence and typing state are not persisted.

## Validation results

| Check | Result | Evidence |
| --- | --- | --- |
| .NET build (Release, warnings as errors) | **Passed** | `scripts/validate.ps1`: 0 warnings, 0 errors |
| .NET tests incl. real PostgreSQL 18 | **Passed** — 183 total, 183 passed, 0 skipped (repeated 3×) | 0 leftover `chatx_test_*` databases. |
| Frontend type-check, tests, build | **Passed** — 29 tests | Run individually; `npm ci` blocked (see below). |
| Acceptance: authenticated clients can connect | **Passed** | `RealtimeTests`: anonymous negotiate 401; cross-origin 403; own origin / no origin 200; connected. |
| Acceptance: unauthorized clients cannot join protected conversations | **Passed** | Join without access → `conversation_not_found`; typing without join refused; revoked access stops delivery and rejoining. |
| Acceptance: presence updates correctly | **Passed** | Both providers: only contacts/colleagues notified; unrelated user receives nothing and sees nothing; offline after the last device. |
| Acceptance: multiple devices are supported | **Passed** | Second device produces no event; closing one of two keeps the user online; sign-out closes only that session's connection. |
| Acceptance: reconnection does not duplicate state | **Passed** | Reconnect within grace → no events; rejoin idempotent; client test: rejoin + snapshot reload, no duplicates. |
| Acceptance: typing indicators expire automatically | **Passed** | Expiry event without a stop signal; renewals silent; unit tests with a manual clock. |
| Browser, end to end (fresh Vite → fresh API) | **Passed** | Badge "Live"; a second user on another device (Node SignalR client) became a contact → green dot with "Online" label; disconnect → "Offline" after the grace period; API stopped → "Reconnecting…", restarted → "Live" automatically. |
| Docker profiles | **Not run** | Docker is not installed. |
| `npm ci` | **Not run (environment)** | The orphaned dev server from an earlier session (PID 25260) still locks files under `node_modules`; `npm install` restored them. |

A flaky failure found during validation ("database is locked" on SQLite under concurrent hub
polling) came from the tests' non-pooled SQLite connections; test databases now use pooled
connections like production and clear the pool on disposal.

## Open items

- Single node only: several API instances need a SignalR backplane and shared presence together
  (ADR-016). Not implemented.
- Typing UI and conversation subscriptions arrive with conversations in Phase 5; the client API is
  ready and tested.
- No "appear offline" setting; presence audience is not configurable.
- Server events for messages, receipts, conversations and notifications arrive with their phases.

## Next slice

Phase 5 — Conversation engine (direct and group conversations, membership, history visibility).
Its gate: historical visibility on join/leave, role changes and direct-chat uniqueness.
