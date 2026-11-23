# Phase 6 — Messaging engine: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-22.
Applicable ADRs: 005, 012 (persist before publishing), 017, 018, 021, 022 (accepted), **023
(accepted by the owner for this phase)**.

Also delivered before this phase on request: **Populate demo data** (see the end of this record).

## Gate decisions (Implementation §12.2), owner answers of 2026-11-22

| Gate | Decision |
| --- | --- |
| Edit and delete windows | Authors edit their own messages for 15 minutes (`Messaging:EditWindow`) and delete their own at any time; a deleted message leaves a tombstone; group owners and administrators may delete others' messages. |
| Receipt semantics (ADR-023) | Accepted: sent = committed; delivered = a recipient device acknowledged; read = recipient read position. Forward-only per-participant watermarks, read implies delivered; dispatch alone never counts as delivery. |
| Idempotency retention | Client message ids are unique per conversation and sender for ever; a retry returns the original, a different body is `409 idempotency_conflict`. |
| Content | Plain text up to 4000 characters (`Messaging:MaxLength`) with line breaks and replies. |

## Delivered behaviour

- Messages are history entries (`kind = message`) on the shared ADR-022 sequence: history
  pagination, visibility from join and unread counts apply unchanged; deleted messages no longer
  count as unread.
- API: `POST /conversations/{id}/messages` (201, or 200 on an idempotent retry),
  `PUT …/messages/{messageId}` (edit), `DELETE …/messages/{messageId}` (tombstone),
  `POST /conversations/{id}/delivered` (delivery acknowledgement); `/read` now also publishes read
  receipts; participants carry `lastDeliveredSequence` and `lastReadSequence`; history entries
  carry body, reply preview, client message id, version, edited and deleted times.
- Replies must quote a visible message of the same conversation; the database enforces it with a
  composite foreign key.
- Direct conversations refuse new messages across a block (`403 messaging_blocked`); groups are
  unaffected (Phase 5 decision).
- Transactional outbox: each message change and its outbox row commit together; the row is
  published right after commit and deleted, otherwise the background dispatcher retries with
  backoff (`Messaging:Outbox*`). Recipients are computed at dispatch time from current access,
  so later joiners never get events about earlier messages and removed members get nothing.
- Realtime events (v1): `MessageCreated`, `MessageUpdated`, `MessageDeleted` (with `eventId` for
  deduplication and the entry's `version`), `MessageDelivered`, `MessageRead`.
- Web app: message timeline with own/others' bubbles, replies with quotes, edit (inside the
  window), delete, "edited"/"deleted" markers, sent/delivered/read (direct) and "read by N of M"
  (groups), typing indicator, composer (Enter sends, Shift+Enter new line), pending sends with a
  retry that reuses the client message id, automatic delivery acknowledgements, live list
  updates, EN/ES.

## Migration impact

`AddMessaging` (both providers): message columns on `conversation_events` (body, reply target,
client message id, version, edited/deleted times and actor); unique
`(conversation_id, actor_user_id, client_message_id)`; alternate key `(conversation_id, id)` and
composite reply foreign key; check constraints for message fields and versions;
`conversation_participants.last_delivered_sequence` (back-filled from the read position, check
`delivered >= read`); new `outbox_messages` table. SQLite needs a temporary unique index during the
table rebuild (in the migration). Development installations apply it automatically at startup
(`Database:MigrateOnStartup`).

## Validation results

| Check | Result | Evidence |
| --- | --- | --- |
| .NET build (Release, warnings as errors) | **Passed** | 0 warnings, 0 errors |
| .NET tests incl. real PostgreSQL 18 | **Passed** — 235 total; final full run 234 passed, 1 failed (the PostgreSQL setup test lost its database connection mid-migration, `NpgsqlException: Exception while reading from stream`, while frontend tests loaded the machine); that test then passed 3 out of 3 times alone | `scripts/validate.ps1 -SkipFrontend` |
| Acceptance: messages persisted before realtime publication | **Passed** | A failing transport leaves the message committed and in history; the outbox dispatcher publishes it later and the outbox empties. |
| Acceptance: duplicate requests do not create duplicates | **Passed** | Retry returns the original; six concurrent identical sends store one message (both providers); the database rejects a reused client id. |
| Acceptance: authorized participants receive realtime events | **Passed** | Created/updated/deleted and receipts reach participants; a later joiner gets no events about earlier messages; a removed member and outsiders get none. |
| Acceptance: history available after reconnection | **Passed** | History endpoint returns current state (edits, tombstones); the web client reloads on reconnect. |
| Frontend | **Passed** — 34 tests | Sending a reply with a client id and showing its receipt; a failed send retried with the same id. |
| Browser, end to end (scratch instance) | **Passed** | The new migration applied itself at startup; demo messages visible; a message from demo user Alice on another device arrived live; my reply showed "Delivered to 1 of 5" and then "Read by 1 of 5" when Alice read it; Alice's device received the reply, typing and read events. |
| Docker profiles | **Not run** | Docker is not installed. |

Two flaky test setups were found and fixed during validation: test hosts started before their
database was migrated, so Data Protection's key ring loader (and the new outbox dispatcher) could
read missing tables. Test helpers now migrate before starting the host.

## Demo data (requested before this phase)

`POST /api/v1/admin/demo-data` and a **Populate demo data** button on the home page for
installation administrators, only where `DemoData:Enabled` (Development, or installations set up
in development mode). Adds 10 people (`*@demo.chatx.test`, password `chatx demo password`), two
companies with workspaces, contacts, five conversations and seven messages, including the caller.
Idempotent: fixed keys and natural keys, never re-adds someone who left; a second run adds nothing.

## Open items

- Change feed for reconnect catch-up (second half of ADR-022) is not implemented; clients reload
  history after reconnecting.
- No formatting, mentions, reactions or link previews; attachments are Phase 7; notifications
  (muting effects, email/push) are later phases.
- Conversation list is not paginated.

## Next slice

Phase 7 — Attachments (storage abstraction, upload lifecycle, validation, private downloads).
Gate: quotas, allowed types, scan policy and cleanup lifecycle.
