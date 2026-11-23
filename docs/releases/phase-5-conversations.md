# Phase 5 — Conversation engine: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-22.
Applicable ADRs: 008, 012, 017, 018, **021 (accepted, scope)**, **022 (accepted by the owner for this
phase: per-conversation sequence)**, 023 (revocation semantics; still Proposed as a whole).

## Gate decisions (Implementation §12.2), owner answers of 2026-11-22

| Gate | Decision |
| --- | --- |
| Historical visibility on join/leave | Members see history from the entry that recorded their join. People who leave or are removed lose access to the conversation entirely (API, history, list and realtime). Rejoining starts history again from the rejoin. |
| Direct-chat uniqueness and eligibility | One direct conversation per pair per scope (`direct_key`, unique index). Personal: only between contacts. Organization: between active members of that organization. Never across a block. Opening again returns the existing one. |
| Role changes | Owner / administrator / member. Administrators rename, describe, add and remove members; owners also change roles and remove administrators or owners. The last owner cannot be demoted; if the last owner leaves, the longest-standing participant becomes owner. |
| Who can be added | Personal groups: the adder's contacts. Organization groups: active members. Nobody the adder blocked or who blocked the adder (one neutral error code). |
| Blocking in groups | A block does not remove anyone from a shared group. |
| Ordering (ADR-022) | Accepted: transactionally allocated per-conversation sequence for history cursors and unread counts. |

## Delivered behaviour

- Domain: `Conversation` (type, ADR-021 scope, organization, title, description, direct key, last
  sequence), `ConversationParticipant` (role, status, visible-from and read positions, mute) and
  `ConversationEvent` (immutable history entry at a unique sequence).
- API `/api/v1/conversations`: list (most recent first, unread counts), open direct, create group,
  get, update (title/description), history (opaque cursors, newest page first, ≤100 per page),
  mark read (forward only, capped), settings (mute), add/remove participants, change role, leave.
- Leaving or being removed from an organization ends that person's organization conversations;
  rejoining the organization does not restore them.
- Realtime: `ConversationUpdated` (v1) to participants after every committed change (`created`,
  `updated`, `members`, `read`, `removed`); the Phase 4 placeholder access check is replaced by a
  real one (active participation plus, for organization conversations, active membership);
  removal evicts the person's open connections from the conversation destination.
- Web app: Conversations page (list with unread badges and presence, new direct/group with
  personal or organization scope, history with "load older", mark read on view, group details:
  rename, description, roles, add/remove, leave, mute), live updates and reconnect reloads,
  "Message" on contacts, EN/ES.

History entries are conversation events (created, member added/removed/left, renamed, description
changed, role changed). Messages share the same sequence from Phase 6.

## Migration impact

`AddConversations` (both providers) creates `conversations`, `conversation_participants` and
`conversation_events` with: unique `direct_key`; unique `(conversation_id, user_id)`; unique
`(conversation_id, sequence)`; check constraints for scope/organization agreement, direct key per
type, non-negative sequences and read position ≥ visible-from − 1; `last_sequence` is a concurrency
token. Existing tables are unchanged. **Existing installations must run `migrate`.**

## Validation results

| Check | Result | Evidence |
| --- | --- | --- |
| .NET build (Release, warnings as errors) | **Passed** | 0 warnings, 0 errors |
| .NET tests incl. real PostgreSQL 18 | **Passed** — 208 total, 208 passed, 0 skipped (`scripts/validate.ps1 -SkipFrontend`) | Includes `ConversationTests` (13, both providers) and unit `ConversationDomainTests`. 0 leftover test databases. |
| Acceptance: users can create direct conversations | **Passed** | Uniqueness from both sides, personal vs organization separation, eligibility and block refusal. |
| Acceptance: users can create groups | **Passed** | Contacts-only personal groups, organization groups. |
| Acceptance: membership is enforced | **Passed** | Outsiders, removed and departed people get `conversation_not_found` for detail and history; list excludes them; hub join refused. |
| Acceptance: history ordered consistently | **Passed** | 12 concurrent writers produce unique, contiguous sequences; a stale writer reusing a position is rejected by the database (both providers). |
| Acceptance: pagination works | **Passed** | 57 entries over pages of 20: ascending within pages, no gaps or duplicates; invalid cursors and limits refused. |
| Acceptance: unread counts are correct | **Passed** | Counts others' entries after the read position within visible history; read position never moves backwards or past the end. |
| Frontend | **Passed** — 32 tests | List/unread/history/mark-read, group creation request, entry wording EN/ES. |
| Browser, end to end (fresh Vite → fresh API, scratch database) | **Passed** | Group created from contacts; history entries; role change and rename; a second user on another device received `created`, `members`, `updated` live, had 4 unread (history from his join), then received `removed` on removal and his list became empty. Layout checked at the narrow content width. |
| Docker profiles | **Not run** | Docker is not installed. |

## Open items

- Conversation list is not paginated (bounded by a user's participations); history is.
- Workspace-scoped conversations (ADR-021 allows them) are not implemented.
- Change feed / reconnect catch-up (ADR-022 second half) arrives with Phase 6.
- The last-owner rule is enforced in the application (sequence token serializes writers per conversation).

## Next slice

Phase 6 — Messaging engine (messages on the shared sequence, edits, deletes, replies, receipts,
idempotency, transactional outbox, realtime delivery). Gate: edit/delete windows, receipt
semantics (ADR-023) and idempotency retention.
