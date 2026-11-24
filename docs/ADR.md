# Audorix.ChatX — Architecture Decision Records

> **Source and revision policy.** Prepared from the user-supplied `Pasted markdown(4).md`, which contains the ChatX documentation draft. The unavailable shared link was not used as evidence. Source technology choices and feature intent are retained. “Accepted” and “Approved” labels in that draft are not treated as evidence of separate user approval. New implementation recommendations below are proposals, not previously agreed requirements. These documents describe intended behavior; they do not certify implemented or tested software.

**Companion documents:** [ADR](ADR.md) · [Architecture](ARCHITECTURE.md) · [Implementation](IMPLEMENTATION.md).

**Document:** ADR.md\
**Version:** 1.1 — documentation revision, 2026-11-22\
**Status:** Source baseline; additional proposals are identified separately\
**License:** MIT

## ADR-001: Modular Monolith

**Decision:** Use a modular monolith for the initial implementation.

**Context:** The application needs clear module boundaries without the operational complexity of microservices.

**Consequences:**

- Modules have independent responsibilities.
- Business logic is separated from infrastructure.
- Modules communicate through explicit contracts.
- Modules can potentially become independent services later.
- Initial deployment remains straightforward.

Microservices are explicitly excluded from the initial release.

## ADR-002: Backend Technology

**Decision:** Use ASP.NET Core Web API with .NET 10.

**Namespace root:** `Audorix.ChatX`

All backend projects must use the following namespace convention:

- `Audorix.ChatX.Api`
- `Audorix.ChatX.Domain`
- `Audorix.ChatX.Application`
- `Audorix.ChatX.Contracts`
- `Audorix.ChatX.Infrastructure`

Additional projects follow the same convention.

## ADR-003: Frontend Technology

**Decision:** Use React with TypeScript and Vite.

**Package naming:** `@audorix/chatx`

The frontend must be mobile-first and responsive.

Bootstrap 5 provides the initial styling foundation, complemented by custom reusable components and CSS variables.

Frontend architecture must support independent reuse of chat components and embedding functionality.

## ADR-004: Database Provider Abstraction

**Decision:** Support SQLite and PostgreSQL using Entity Framework Core.

SQLite is intended for lightweight installations and development.

PostgreSQL is the recommended production database.

Both providers share the same domain model and application contracts.

Provider-specific migrations must be maintained independently.

Database selection is configuration-driven.

## ADR-005: Realtime Communication

**Decision:** Use SignalR as the default realtime communication provider.

Application services must not depend directly on SignalR Hub implementations.

An abstraction will allow alternative realtime providers in future releases.

Message persistence is independent of realtime delivery.

## ADR-006: Authentication

**Decision:** Use ASP.NET Core Identity for local authentication and support external authentication through OAuth 2.0/OpenID Connect.

Initial authentication options:

- Email/password.
- Google.
- Microsoft.

Authentication providers must be configurable.

Authorization must be policy-based and support installation, organization and workspace scopes.

## ADR-007: File Storage

**Decision:** Introduce a storage provider abstraction.

Initial implementations:

- Local filesystem.
- S3-compatible object storage.

File metadata is stored in the database. Actual file content is stored through the configured storage provider.

## ADR-008: Multi-tenancy

**Decision:** Design for multi-tenancy from the beginning.

The initial implementation uses shared database infrastructure with organization-level data isolation.

The architecture must distinguish:

- Installation.
- Organization.
- Workspace.
- User.
- Membership.
- Resource ownership.

Tenant boundaries must be enforced server-side.

## ADR-009: Localization

**Decision:** Localization is a first-class platform feature.

Initial languages:

- English (en).
- Spanish (es).

React uses i18next.

.NET uses ASP.NET Core Localization and resource files.

User interface language, notification language and message language are separate concepts.

## ADR-010: Application Modes

**Decision:** Support three configurable usage modes through shared application functionality.

1. General-purpose chat.
2. Business communication.
3. Embedded chat.

These modes must not have separate messaging engines.

Feature availability is controlled through configuration and authorization policies.

## ADR-011: Embeddable SDK

**Decision:** Provide JavaScript and React integration packages.

The SDK communicates exclusively through public API and realtime contracts.

The widget must support anonymous and authenticated visitors.

Authenticated visitor identity is established through short-lived server-issued tokens.

Private application credentials must never be exposed to browser clients.

*Phase 10 note:* implemented as the installation-served loader `widget.js` (plain JavaScript API) and the React package `web/packages/chat-embed`; both drive the iframe widget of ADR-024 and use only the public `/api/v1/widget` and `/hubs/widget` contracts.

## ADR-012: Messaging Persistence

**Decision:** Persist messages before publishing realtime events.

Use a transactional outbox pattern for reliable event dispatch.

SignalR delivery is not considered proof of database persistence or recipient delivery.

Message history is retrieved through cursor-based pagination.

## ADR-013: API Architecture

**Decision:** Use versioned REST APIs.

Initial API prefix:

`/api/v1/`

SignalR is used for realtime notifications and messaging events.

REST remains the authoritative interface for operations and message history.

## ADR-014: Licensing

**Decision:** Use the MIT License.

The project must include:

- LICENSE.
- README.md.
- CONTRIBUTING.md.
- SECURITY.md.
- Installation documentation.
- Deployment documentation.
- API documentation.

Third-party dependencies must be reviewed for license compatibility.

*Phase 12 note (2026-11-23):* fulfilled for release 0.1.0. The copyright holder is "Audorix" (owner
decision). `LICENSE`, `README.md`, `CONTRIBUTING.md`, `SECURITY.md` (private reporting through GitHub
security advisories), `docs/installation.md`, `docs/deployment-docker.md` and `docs/api/` are in
place. The dependency review (`docs/third-party-licenses.md`, generated by
`scripts/third-party-licenses.mjs`) found only permissive licenses in what ships.

## ADR-015: Deployment

**Decision:** Support both self-hosted and SaaS deployment models.

Initial deployment uses Docker.

The application must support externalized configuration, persistent storage and independent database configuration.

The architecture must not require a proprietary cloud service.

## ADR-016: Scalability

> Phase 21: multi-instance support is now available as an option — see ADR-035 (Redis backplane, shared presence and rate limits).

**Decision:** Build for horizontal scalability without implementing distributed infrastructure prematurely.

Initial deployment may use a single application instance.

Future multi-instance deployments must support:

- Shared persistent database.
- Distributed realtime delivery.
- Shared presence state.
- Distributed event dispatch.
- Shared attachment storage.

Do not rely on process-local state for durable application functionality.

## ADR-017: Message Identifiers and Ordering

**Decision:** Use GUID-based entity identifiers and stable message ordering.

Message creation uses UTC timestamps.

Pagination uses a stable cursor based on creation timestamp and identifier.

Message sending supports idempotency keys to prevent accidental duplicate submissions.

## ADR-018: Security

**Decision:** Security must be enforced at the application boundary and within domain operations.

Requirements include:

- Authentication.
- Authorization.
- Tenant isolation.
- Rate limiting.
- Secure session handling.
- Input validation.
- Secure attachment processing.
- Origin validation for embedded widgets.
- Audit logging for security-sensitive actions.

## ADR-019: Initial Product Scope

**Decision:** Deliver a working general-purpose chat application first, with business and embedded capabilities developed on the same foundation.

The initial release includes:

- Authentication.
- Users and profiles.
- Contacts.
- Private conversations.
- Group conversations.
- Realtime messaging.
- Presence.
- Message receipts.
- Attachments.
- Localization.
- Responsive UI.
- Basic administration.

Advanced channels, support workflows, AI integrations, voice and video are future extensions.

---

**ADR maintenance policy**

New architectural decisions must be documented using numbered ADRs.

Existing accepted decisions should not be silently changed. Superseded decisions must retain their original record and reference the replacement ADR.

## Review findings and proposed additional decisions

The nineteen source ADRs above preserve the supplied draft's direction. The following records resolve gaps found during analysis. Their status is **Proposed**. They must not silently override the source baseline or be represented as already accepted.

### ADR-020: Explicit release boundaries

**Status:** Accepted — 2026-11-23, by the project owner, as proposed below. By then R1 (Phases 1–9), R2 (Phase 10) and R3 (Phase 11) had each passed their phase, and release 0.1.0 (Phase 12) shipped all three.

**Context:** ADR-019 prioritizes general chat, while the implementation roadmap also includes widgets and business channels. The draft's sample configuration enabled embedded chat before its implementation phase.

**Decision proposed:** Define R1 as core chat (Phases 1–9 plus applicable release work from Phase 12), R2 as embedded chat (Phase 10), and R3 as business extensions (Phase 11). Keep channels and widgets disabled until their release gates pass. Preserve basic organizations and workspaces in R1 for isolation. Treat reactions, support ticketing and automation as deferred.

**Consequence:** Phase numbers remain stable, but documentation, security and deployment work begins in Phase 1 and is required at every release. A feature flag cannot make an unimplemented feature available.

### ADR-021: Personal and organization conversation ownership

**Status:** Accepted — 2026-11-22, by the project owner, as proposed below (explicit `ScopeKind`; the alternative of a mandatory default organization was not chosen). Phase 3 implements the organization/workspace membership model this relies on; conversation scope fields arrive with Phase 5.

**Context:** General-purpose contact chat and organization isolation coexist, but the draft does not define ownership of conversations between personal users.

**Decision proposed:** Use an explicit `ScopeKind` with `Personal` and `Organization`. Personal conversations authorize by participation. Organization conversations require both active organization membership and conversation access; workspace-scoped conversations also require workspace access. Do not infer personal access solely from a null organization ID. Widgets belong to an organization, including a designated organization for a single-installation deployment.

**Alternative:** Require every conversation to belong to an organization and supply a default organization for consumer deployments. This is simpler structurally but changes the personal-chat model and needs an explicit product decision.

**Consequence:** Identical participants may have distinct personal and organization chats. Direct-conversation uniqueness includes scope. No implicit cross-organization sharing is introduced.

### ADR-022: Durable ordering and reconnect recovery

**Status:** Accepted — 2026-11-22, by the project owner, for Phase 5 (extends ADR-017 without changing its source record). Phase 5 implements the per-conversation sequence (`conversations.last_sequence` protected by a concurrency token, unique `(conversation_id, sequence)` on history entries) for history cursors and unread counts; the durable change sequence and change feed were completed in Phase 22 (ADR-036).

**Context:** Timestamp/ID ordering gives stable history, but it does not alone recover missed edits, deletions, receipts or out-of-order realtime notifications.

**Decision proposed:** Retain GUID identifiers and UTC timestamps. Add a transactionally allocated per-conversation message sequence and a separate durable change sequence. Use the message sequence for history cursors, and the change sequence for reconnect catch-up. Allocate sequences with database concurrency control, not an unprotected `MAX + 1`. A retained change feed contains only information authorized for the caller; expired cursors require a fresh snapshot.

**Consequence:** This supersedes timestamp/ID as the authoritative history cursor only if accepted. Clients deduplicate by event ID and reconcile mutations by entity version. Outbox publication remains at-least-once, not exactly-once. The change feed is a synchronization aid, not a conversion to event sourcing.

### ADR-023: Receipt semantics and membership revocation

**Status:** Accepted — 2026-11-22, by the project owner, for Phase 6. Implemented as per-participant `last_delivered_sequence` and `last_read_sequence` watermarks (forward only, capped at the conversation's last position, read implies delivered); direct chats show sent/delivered/read, groups "read by N of M".

**Decision proposed:** “Sent” means committed to the database. “Delivered” requires an authenticated recipient client acknowledgement. “Read” requires a recipient read acknowledgement. Track monotonic participant watermarks and aggregate across that participant's devices. Dispatch success alone never creates a delivery receipt. Participants cannot acknowledge another participant's messages or positions they cannot access.

**Consequence:** Receipt state remains separate from notification delivery. Membership revocation invalidates API access, download access and realtime subscriptions; reconnecting does not restore revoked access. Exact history visibility on joining or leaving a group remains a product decision.

### ADR-024: Widget authentication and upload lifecycle

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 10: iframe widget with a small loader script (the Shadow DOM alternative is not used); host servers sign identity tokens as HS256 JWTs with a per-widget secret; responders are organization members chosen per widget; "balanced" visitor limits.

**Decision proposed:** First-party web authentication uses secure HttpOnly cookies and CSRF protection. Embedded visitors use separate, short-lived, narrowly scoped tokens. A host backend signs or obtains identity assertions; the browser never receives a signing secret. Domain restrictions are an abuse-control layer, not proof of visitor identity. Prefer an iframe widget for isolation; a Shadow DOM implementation remains an alternative requiring its own browser security review.

**Consequence:** The iframe recommendation differs from the draft's Shadow DOM default and requires acceptance before Phase 10. Anonymous sessions need quotas and abuse limits. Attachments remain private and unavailable until validation completes; if configured scanning is unavailable, they remain quarantined. Object storage and database changes require compensating cleanup because they do not share one transaction.

**Phase 7 note (2026-11-22):** the upload half is implemented as described for signed-in users (server-streamed uploads, `PendingUpload → Quarantined → Ready/Rejected → Deleted`, record committed before the bytes are written, tracked and retried content removal, no public or signed URLs). The widget half and the status of this ADR remain open until Phase 10.

**Phase 10 implementation (2026-11-23):**

- *Isolation.* `widget.js` (served by the installation) creates a sandboxed iframe of `/embed.html` and only resizes it. Messages between the two are checked both ways (`event.source` is the other window, `event.origin` the expected origin; replies are posted to that origin only). The embed page is the only frameable page: the web server sends `Content-Security-Policy: frame-ancestors` with the widget's exact allowed origins (from `GET /api/v1/widget/{id}/frame-policy`; nginx `auth_request` in Docker, a Vite middleware in development), `'none'` for unknown or disabled widgets. Every other page keeps `X-Frame-Options: DENY`.
- *Visitors.* A visitor is a stand-in account (`users.is_visitor`, undeliverable `.invalid` address, permanently locked out, excluded from people search and organization lookups) plus a `visitors` row. Sessions are opaque random bearer tokens (only their SHA-256 is stored) sent as `Authorization: Bearer`, or as `access_token` on `/hubs/widget` only; they last 30 days and slide while used. No cookies are involved, so the widget API (`/api/v1/widget`) has no antiforgery filter and cannot be driven cross-site, and a visitor token never satisfies a signed-in user policy.
- *Identity.* `IdentityTokens.Verify` accepts exactly `alg: HS256`, compares the signature in constant time, requires `aud` = widget id, `sub`, `iat` not in the future (1 minute skew), `exp` in the future and `exp − iat` ≤ `Widgets:IdentityTokenMaxLifetime` (1 hour). The secret is generated by the server, shown once, stored with Data Protection, and can be replaced (old tokens stop at once). The same `sub` maps to the same visitor and conversation.
- *Abuse limits (owner's "balanced" choice).* Anonymous sessions only if the widget allows them, at most 5 per hour per client address; 20 messages per minute per session; the reported embedding origin must be an allowed origin; one conversation per visitor; attachments for identified visitors only, through the same upload pipeline as members. Sessions are created only when a visitor opens the chat (or the host identifies them), never on page view. Limits are process-local (one API instance, ADR-016); per-address limits need `Network:TrustedProxies` behind a reverse proxy.
- *Conversations.* `ConversationType.Website` in the widget's organization, created with the first message, with the widget's active responders as participants. The visitor participant (`is_visitor`) passes access checks without organization membership; responders use their normal chat. `/hubs/widget` delivers only the visitor's conversation events (never presence or private chats) and re-checks the session on every call and periodically.

### ADR-025: Private (ephemeral) chats

**Status:** Accepted — 2026-11-22, by the project owner, who requested the feature for Phase 7 and chose: lifetime bound to the initiator's sign-in session; one-to-one with an invitation the other person must accept; attachments held in server memory only.

**Context:** People want conversations that leave nothing behind. ADR-012 (persist before publishing) and ADR-022/023 (durable sequence, receipts) assume every message is stored; a "private" mode cannot reuse them without storing what it promises not to store.

**Decision:** Private chats are a separate, non-persistent model. They live in the API process memory (`PrivateChatRegistry`) and are never written to the database, attachment storage, outbox or logs. Eligibility is the same as for a direct conversation (contacts or members of a shared active organization, never across a block). The initiator invites; nothing can be sent before the invitee accepts. The chat belongs to the initiator's sign-in session: the initiator reaches it only from that session; it ends when the initiator ends it, signs out, or the session is revoked or expires (checked on sign-out/revocation immediately and by a sweep), when the invitation expires or is declined, when a block appears, and on shutdown. Ending wipes message texts and zeroes file buffers, then tells both people (`PrivateChatChanged` with a reason). Messages are delivered only to the initiator's session connections and the invitee's connections (`PrivateMessageCreated`). Limits bound memory (messages, bytes per chat, files, chats per person). Files use the same type allow-list and signature checks as attachments; with scanning configured an unavailable scanner refuses the file.

**Consequences:** No history, receipts, edits, offline delivery or reconnect catch-up: a client that was offline sees what is still in memory when it returns, and nothing after the chat ended. A restart ends every private chat. The model is single-instance; scale-out needs sticky routing or a shared ephemeral store (never a durable one). The server still sees the plaintext in memory — this is "not saved", not end-to-end encryption, and the UI says so, including that screenshots cannot be prevented. Downloads are `no-store`.

### ADR-026: Business communication — channels, teams, guests and support

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 11, choosing: public and private channels with history from the join; channel-scoped guests; teams as member groups with permissions inheriting downwards; a Support Agent role with an inbox and assignment.

**Context:** Implementation §12.2 asks for the channel access model, guest history and workspace/team permission inheritance before Phase 11. Architecture §5.2–5.3 lists teams, channels and support conversations; §10 lists Team Manager, Guest and Support Agent roles.

**Decision:**

- *Channels* are conversations (`ConversationType.Channel`) of one workspace, using the shared messaging engine (messages, receipts, attachments, realtime). Names are unique per workspace (case-insensitive). Public channels can be found and joined by any active member of the workspace; private ones only by being added. History is visible from the join, as for groups. Access needs active participation, organization membership and **workspace membership**; leaving a workspace ends its channels, and the access queries also check it.
- *Permissions inherit downwards:* organization owners/administrators → workspace administrators → channel managers (participants with the administrator role) → members. Higher levels manage every channel below them, also private ones they are not in. Channel managers moderate messages like group administrators.
- *Teams* are named groups of workspace members with Team Managers who manage the team's membership. Adding a team to a channel adds its members and links it, so people who join the team later join the channel too; leaving the team or unlinking it removes nobody. Teams grant membership only, never administration.
- *Guests* are separate `OrganizationGuest` records, deliberately **not** organization memberships, so every membership rule (directory, presence, direct conversations, groups, workspaces, browsing channels) excludes them by construction. Owners/administrators invite an email address into one channel; the invitee accepts (an invitation reveals nothing about accounts). Guests see only those channels, from their join, and never manage. Each organization can switch guests off (`organizations.guests_enabled`), which suspends all of its guests at once.
- *Organization-specific configuration* lives on the organization row (guests on/off, who creates channels) and affects that organization only.
- *Support agents:* a role flag on organization membership that combines with member/administrator/owner. Website conversations (Phase 10) carry a status (open/closed) and at most one assignee. Agents and owners/administrators see the organization's whole inbox and assign agents, administrators or responders who take part; responders see and work the conversations they are in. Whoever takes a conversation joins it with the **whole** history (support needs the visitor's earlier messages). A visitor's new message reopens a closed conversation. Assignment and status changes are history entries; the visitor's widget does not show them.

**Consequences:** One more access dimension (workspace) is checked on every channel operation and in the realtime recipient queries. Guests in a channel see its member names. Support staff who are not in a conversation learn about it through `ConversationUpdated` with `change: "inbox"` on creation, assignment and status changes, not on every visitor message (the inbox also refreshes every 30 seconds). No SLAs, tags, canned answers or automation yet.

### ADR-027: Account deletion and data retention

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 13, choosing: messages stay and show "Deleted user"; deletion waits while the person is the only owner of an organization; retention of inactive anonymous visitors, never confirmed accounts and (optionally) old messages.

**Context:** The decision register left "account deletion and retention" open until before public release; ARCHITECTURE §16 lists retention policies as an extension point; the audit log already expires (Phase 9). Messages, receipts and history entries refer to their author, so removing account rows outright would either break history or delete other people's conversations.

**Decision:**

- *Erase, keep the row.* Deleting an account erases what identifies the person in one transaction: email and user name (replaced by an undeliverable placeholder, so the address can register again), password, external logins, roles, tokens, sessions, contacts, contact requests, blocks, team and guest records, widget responder entries, support assignments, unsent uploads and, for website visitors, the visitor record. The account and profile rows stay with `deleted_at_utc` set and the stored name "Deleted user"; clients show a localized label (`deleted: true` on user summaries).
- *Leave through the normal paths.* Before erasure the person leaves every organization and every group, channel and support conversation with the usual history entries and group owner succession, and private chats end. Direct conversations keep the participation so the other person still reads the history; nobody can send into them any more (`recipient_deleted`).
- *Who and how.* People delete their own account after confirming the password (or, for accounts without one, typing their email address). Installation administrators delete other accounts. Refused while the person is the only owner of an organization (`sole_owner`; nothing is handed over automatically) or the last enabled administrator. Every step can run again, so an interrupted deletion completes by repeating it. Audited as `auth.account_deleted` / `admin.user.deleted`.
- *Organizations can be deleted* by their owners (typing the name) and by installation administrators, so a sole owner always has a way out: conversations, messages, files, workspaces, teams, guests, invitations and widgets go, file content first; the organization's website visitors are erased.
- *Retention* (`Retention:*`, live settings, applied every six hours): anonymous visitors inactive for 90 days are erased; accounts never confirmed are removed after 30 days (only while confirmation is required; deleted outright when nothing refers to them); messages older than N days are deleted with their files when an administrator switches it on (off by default). Message ages are compared on a `created_at_ticks` column so SQLite and PostgreSQL behave the same.

**Consequences:** History keeps its shape and authorship without identifying deleted people. Message text a deleted person wrote stays readable by the people who received it — the owner's choice; it is stated in the deletion dialog. Backups keep deleted data until they expire (docs/backup-restore.md). Retention of whole conversations, per-organization policies, legal hold and exports remain out of scope (Implementation §9).

### ADR-028: Message search

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 14: search is provider based, with the database's own full-text search as default and OpenSearch as an alternative. The recommended matching (every word as a prefix, accent- and case-blind) and scope (message text and attachment file names, filters, conversation quick jump) were applied as defaults because the owner did not choose otherwise.

**Context:** Implementation §9 deferred "advanced search". Search must never show what a person cannot read: conversation access depends on organization, workspace and guest rules, and group history is visible only from the join (ADR-023 and the Phase 5 decision). Installations range from a single SQLite file to PostgreSQL deployments that may already run OpenSearch.

**Decision:**

- *Port and providers.* `ISearchProvider` (index, remove, clear, search, check) with two implementations: **Database** (default) — SQLite FTS5 (`unicode61`, diacritics removed) or a PostgreSQL generated `tsvector` (configuration `simple`) with a GIN index, both in a `message_search` table; **OpenSearch** — one index over its REST API (no client library), a lower-case/ASCII-folding analyzer and `prefix` queries. `Search:Provider` selects one (settings file or environment; restart).
- *One matching rule.* The application normalizes indexed text and query words the same way (lower case, accents removed, non-alphanumerics as separators); every query word must match the start of a word. Results are newest first with a cursor (creation time, message id), identical across providers.
- *Authorization outside the engine.* The search service computes the caller's scopes from the conversation list (same access rules) as (conversation, first visible position) pairs and passes them as filters. Every match is re-read from the database and dropped if it is no longer a live, visible message, so a stale index cannot disclose anything.
- *Indexing through a queue.* Every created, edited or deleted message (and attachment link) adds a `search_queue` row in the same transaction (EF `SaveChanges` hook); bulk deletions (retention, organization deletion) add theirs explicitly. A background indexer applies the queue every `Search:IndexInterval` (2 s) and completes rows only after the provider accepted them; an unavailable engine just delays indexing. The migration queues every existing message; a newly created OpenSearch index re-queues everything; administrators can rebuild the index.
- *Feature switch.* `Search:Enabled` is a live setting; when off the API answers `403 search_disabled` while indexing continues.

**Consequences:** Search results lag message changes by a few seconds. OpenSearch receives message text, so it must be secured like the database (TLS, credentials, private network) and is not covered by the database backup; it can always be rebuilt. Switching providers requires a rebuild (the old index is not updated while unused). Ranking is by recency, not relevance; no typo tolerance, stemming or search inside file contents.

### ADR-029: Email and Web Push notifications

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 15: batched emails soon after messages stay unread (people can choose daily or off); Web Push that follows the existing preview setting; every conversation that is not muted notifies.

**Context:** Implementation §9 deferred push integrations. Phase 8 added in-app desktop notifications, which only work while ChatX is open. People who are away need to learn that messages wait, without leaking message text more widely than they allowed.

**Decision:**

- *Email.* A message that is still unread `Notifications:EmailDelay` (15 min) after it arrived, for a person who is not connected, goes into one email listing the conversations waiting (at most ten, with counts), at most one email per `Notifications:EmailInterval` (1 h). "Daily" sends at most one per 24 h; "off" none. Each message is emailed once: `conversation_participants.last_notified_sequence`. Message excerpts only with the person's preview setting on. Recipients follow the access rules: active participants, not muted, not the author, not visitors, confirmed and not deleted accounts; in support conversations the assignee (or, unassigned, all responders). Demo accounts and demo conversations never email. The upgrade counts everything existing as already notified.
- *Web Push* (RFC 8030) with message encryption (RFC 8291, `aes128gcm`) and VAPID (RFC 8292), implemented on the platform's ECDH/ECDSA/HKDF/AES-GCM, no new dependency. The installation's VAPID key pair is created on first use and stored in the database with the private key protected by Data Protection. Browsers subscribe from the account page; a subscription belongs to the sign-in session that created it and stops when that session ends. New messages are queued with the message (`push_queue`) and pushed to recipients who have no open connection; the payload is title, body (sender and, with previews, an excerpt), conversation link and tag. Push services answering 404/410 drop the subscription; five failures in a row too.
- *Service worker* `/sw.js` shows push notifications and opens the conversation; it caches nothing.
- *Switches.* `Notifications:EmailEnabled` and `Notifications:PushEnabled` are live settings; the delay and interval too.

**Consequences:** Email and push go to third parties (the email provider; the browser vendor's push service, which sees only encrypted payloads, timing and size). Presence is process-local (ADR-016), so a second API instance would not know who is online. Mentions, per-conversation notification levels, quiet hours and mobile push (APNs/FCM apps) are not included.

### ADR-030: Emoji reactions and threads

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 16: any emoji, at most 20 different ones per message; threads in a side panel with an optional "also send to the conversation"; threads have their own unread state and notify their followers; quote replies stay.

**Context:** Phase 6 gave messages quote replies. Busy groups and channels need lighter feedback than a message ("👍") and side discussions that do not flood the main conversation.

**Decision:**

- *Reactions* are rows `message_reactions` (message, person, emoji), unique per message, person and emoji. An emoji is one Unicode text element containing a symbol and no letters, digits or spaces (skin tones, flags and joined sequences count as one), at most 32 UTF-16 units; anything else is `400 reaction_invalid`. A message carries at most 20 different emoji (`409 too_many_reactions`); an emoji already present can always be added by others. Reactions are not part of the message version: the realtime event `ReactionChanged` carries the message's complete new set, and history returns them with each message. Reactions are removed with the message; they are not available in website conversations, private chats (ADR-025) or for visitors.
- *Threads.* A reply in a thread is an ordinary message of the conversation (same sequence, receipts, search, retention, edit and delete rules) with `thread_root_id` pointing to a top-level message. Threads are one level deep. A reply is hidden from the main timeline and from the conversation's unread count unless it was sent with "also send to the conversation" (`shown_in_conversation`). The first message keeps a reply count and the last reply time.
- *Thread state* per person lives in `thread_follows`: following (authors of the first message and of replies follow automatically; anyone can follow or stop) and a thread read position. History returns each thread's summary with the caller's unread count and following state.
- *Notifications* (ADR-029): a hidden thread reply notifies the thread's followers only (push and email, with the conversation's mute still applying); an echoed reply notifies like any message.

**Consequences:** History queries filter on `thread_root_id`/`shown_in_conversation` (indexed). Older clients see echoed replies as normal messages and do not see hidden ones. Thread-level mute, nested threads and reaction notifications are not included.

### ADR-031: Mentions and notification levels

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 17: named mentions; `@everyone` and `@here` for anyone in groups and channels of up to 20 participants, above that for owners and administrators only; per-conversation levels All / Only mentions / Nothing with All as default and existing mutes becoming Only mentions; a Mentions view.

**Context:** Phases 15 and 16 notify about every message of a conversation that is not muted, and thread replies to followers. In busy groups and channels people mute and then miss what is meant for them.

**Decision:**

- *Format.* Message text stays plain; the composer inserts `@Display Name` and the client sends `mentionUserIds` with the message. The server keeps a mention only for active participants of the conversation (`400 mention_invalid` otherwise, at most `Messaging:MaxMentions`, 50) and ignores the author. Display names in the text are not parsed, so renames and older clients do no harm.
- *Broadcasts.* `@everyone` and `@here` are read from the text as whole words (not inside an address such as `x@everyone.example`), in groups and channels only. `@everyone` mentions every participant; `@here` those online when the message is sent. With more than `Messaging:EveryoneMentionLimit` (20) participants only owners and administrators may use them (`403 mention_everyone_forbidden`).
- *Storage.* `message_mentions` holds one row per mentioned person (named, everyone or here), ordered by send time in ticks; rows go with the message (deletion, retention) and the account. Mentions are fixed when the message is sent; edits do not change them.
- *Levels.* `conversation_participants.notification_level`: `All` (default), `Mentions` (the former mute) or `Nothing`. Push and email: All as before; Mentions only for messages mentioning the person; Nothing never. A mention also reaches someone who does not follow the thread it was written in. The API keeps `muted` (true for anything but All) and accepts `muted` in the settings request.
- *Mentions view.* `GET /api/v1/mentions` lists the caller's mentions in messages they can still read, newest first, each unread while its message is unread (in the conversation, or for a thread reply kept in the thread, in the thread); `GET /api/v1/mentions/unread` counts them for the navigation badge.

**Consequences:** `@everyone` in a large channel writes one row per participant. Names are not highlighted in the timeline (owner decision: Mentions view only). Mentions are not available in website chats or private chats. Not included: mention-specific email timing, user groups (`@team`), keyword alerts.

### ADR-032: Pins, saved messages and forwarding

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 18: pins by either person in direct conversations and by owners and administrators in groups and channels, at most 50 per conversation; forwarding as a copy credited to the original author; saved messages as bookmarks shown only while readable.

**Context:** Important messages get lost in busy conversations; people want to keep messages for themselves and share them elsewhere. Search (Phase 14) and mentions (Phase 17) open a conversation but not the message.

**Decision:**

- *Pins.* `message_pins` (one per message, unique), up to `MessagePin.MaxPerConversation` (50, `409 too_many_pins`); only messages shown in the conversation (not thread replies kept in their thread); not in website chats (`400 pins_unavailable`). Pinning records a `Pinned` history entry that refers to the message like a reply (so a later deletion shows as deleted); unpinning records nothing. Participants get `ConversationUpdated` with change `pins`. A deleted message loses its pin.
- *Saved messages.* `saved_messages` per person (unique per message, at most 1000). The list shows only messages the person can still read (participation, visible history, not deleted, access re-checked); nothing is copied. Bookmarks go with the message, and with the account when it is deleted.
- *Forwarding.* `POST …/messages/{id}/forward` makes a new message from the caller in another conversation they take part in: the text and copies of the ready files (new attachment records and stored objects, counted against the caller's quota; the originals were already checked and scanned), with `forwarded_from_user_id` set to the original author. No reference to the source conversation is stored or shown, so forwarding reveals nothing beyond what the forwarder chose to share. An optional comment is sent just before the copy. Idempotent by the client message id. Not from or to website chats (`400 forward_unavailable`); private chats have no server copy to forward.
- *Jump to message.* Web links carry `message` (and `thread` for replies kept in their thread); the conversation loads older pages until the message is there and highlights it.

**Consequences:** Forwarded files take storage again (no deduplication). The history check constraint now allows a reply reference on `Pinned` entries. Not included: pinned-message notifications, folders or notes for saved messages, forwarding to several conversations at once.

### ADR-033: Rich text as Markdown source

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 19: Markdown-style formatting stored as typed; no link previews; formatting by keyboard shortcuts and a toolbar.

**Context:** Messages have been plain text since Phase 6. People want emphasis, code, lists and links, without the security and privacy cost of storing or rendering HTML, and without the server fetching URLs.

**Decision:**

- *Storage.* The message body stays the text the person typed; no HTML, no new columns, no migration. Length limits are unchanged. Older clients show the markers.
- *Subset.* `**bold**` / `__bold__`, `*italic*` / `_italic_` (only at word boundaries, so `snake_case` and `2*3*4` stay text), `~~strike~~`, `` `code` ``, fenced code blocks, `> ` quotes, `- ` / `* ` / `1. ` lists, `[text](url)` and bare `http(s)://` links. Only `http`, `https` and `mailto` links become clickable, opening in a new tab with `rel="noopener noreferrer nofollow"`. Mentions of the people a message mentions (ADR-031), and `@everyone` / `@here` when used, are highlighted, yours more strongly.
- *Rendering.* The web app (and the visitor widget) parse the subset into React elements; no HTML string is ever inserted, so any text is safe to render.
- *Plain places.* Emails, push notifications and reply previews use `MessageText.ToPlain` (markers dropped, link texts followed by their address); the web app does the same for toasts, search results, mentions, saved and pinned lists. Search indexes the source text; the tokenizers ignore the markers.
- *No link previews.* The server never fetches linked pages (no SSRF exposure, no tracking of what people share).

**Consequences:** Formatting is per line (no nested lists, tables, headings or images). A future phase can add a different renderer for other clients from the same source text.

### ADR-034: Drafts, scheduled messages and reminders

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 20: drafts (text and reply target) kept on the server per conversation and thread; scheduled messages that, if they cannot be sent, stay listed with the reason; reminders delivered in the app, by push and by email.

**Context:** People switch devices mid-message, want to send at a better time, and want to come back to a message later. All three must keep the access rules that apply when something is finally sent or shown.

**Decision:**

- *Drafts* (`message_drafts`): one per person, conversation and thread (`thread_key` is the thread's first message or the empty id), saved by the web app about a second after typing stops; empty text without a reply target removes it. No files. Drafts unchanged for 30 days are removed hourly. Private to their owner; removed with the account.
- *Scheduled messages* (`scheduled_messages`, `scheduled_message_files`): text, reply target, thread placement, mentions and the sender's unsent uploads, sent up to 30 days ahead (at most 50 waiting per person). At their time `PlanningDispatchService` (every `Planning:DispatchInterval`, 15 s) sends each one as its author through `MessagingService.SendAsync`, in its own scope and with a fixed client message id (`scheduled:{id}`), so every rule applies then and a retry never posts twice. Sent schedules are removed; a refused send is kept with its error code (`failed`) until the person removes it. Uploads attached to a waiting schedule are excluded from the 24-hour cleanup of unsent files.
- *Reminders* (`reminders`): on a message the person can read, up to a year ahead (at most 100 pending). When due: marked `due`, a `ReminderDue` realtime event to the person's open apps, and when they are not online a push notification and an email (unless their email notifications are off; text only with previews on). Reminders on messages that can no longer be read are dropped silently. They stay listed until marked done.
- *Realtime.* `PlanningChanged { kind }` tells the person's other devices to refresh their lists.

**Consequences:** Scheduled sends are as reliable as the dispatcher (a stopped API sends them late, not never); times are compared in UTC ticks on every database. Not included: recurring messages, snoozing, drafts with files.

### ADR-035: Optional Redis scale-out and session purging

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 21 (release readiness): an optional Redis backplane with shared presence and shared rate limits, off by default; ended sign-in sessions purged; a CI workflow. Supersedes the single-node limitation of ADR-016 when Redis is configured.

**Context:** Realtime connections, presence and rate limits lived in one process, so a second API instance would miss messages, show wrong presence and multiply sign-in limits. Ended sessions were never removed, and nothing ran the tests automatically.

**Decision:**

- *Default unchanged.* Without `ScaleOut:Redis` the API is a single server exactly as before (`LocalClusterPresence`, `LocalRateCounter`).
- *With `ScaleOut:Redis`* (a StackExchange.Redis connection string; keys and channels under `ScaleOut:Prefix`, `chatx`):
  - SignalR uses the Redis backplane, so events reach users and conversation groups on every instance.
  - Presence: each instance keeps its connections locally and records "this instance has user X online" in a Redis sorted set per user (members are instances, scores are expiry times), renewed every 20 s and expiring after 60 s, so a crashed instance's entries vanish. "Online" (notifications, `@here`, presence snapshots) means online on any instance; online/offline changes are announced only when the first instance gains or the last loses the person.
  - Rate limits: sign-in limits and website-visitor limits count in Redis fixed windows shared by every instance; if Redis is unavailable they fail open (the readiness check reports Redis).
  - Readiness includes a Redis check; startup logs that scale-out is on.
- *Not shared:* private chats (ADR-025) stay in one instance's memory, so with several instances they need everyone on one instance or must be switched off (a startup warning says so). Revoking a session closes its sockets on other instances at their next session check (30 s).
- *Session purge:* `Retention:ClosedSessionDays` (30, live setting) deletes sessions that expired or were signed out that long ago, with those browsers' push subscriptions, in the retention job.
- *CI:* `.github/workflows/ci.yml` builds and tests the .NET solution (SQLite and a PostgreSQL service) and type-checks, tests and builds the web app on every push and pull request.

**Consequences:** Running several instances needs Redis and a load balancer with WebSocket support (sticky sessions are not required for the backplane, but long-polling clients need them). The Redis code is exercised in tests through an in-memory stand-in, not a real Redis.

### ADR-036: Reconnect catch-up, capacity targets and the web bundle budget

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 22 (performance). Completes the change feed of ADR-022 and resolves the "performance and capacity targets" question in the register below.

**Context:** After a dropped connection the web app reloaded the open conversation's latest page, so edits, deletions and reactions to older messages were missed until a full reload. There were no agreed capacity targets and no way to measure them, the first page load downloaded every page of the app, and the conversation list was returned whole.

**Decision:**

- *Change feed.* Every new or changed history entry, and every entry whose reactions changed, adds a row to `change_log` (identity id, conversation, entry, time) in the same transaction as the change (`ChatXDbContext`, like the search and push queues). `GET /api/v1/changes/cursor` returns an opaque cursor for "now"; `GET /api/v1/changes?since=` returns the current state of every changed entry the caller may still see (membership and visible-from position re-checked), the ids of conversations touched, and a new cursor.
  - *Limits (owner decision):* more than **500** changes, or a cursor older than the **24-hour** retention, returns `reset: true` and the client reloads; the purge runs with the hourly planning cleanup.
  - *Ordering:* identity values can commit out of order, so the returned cursor only advances past changes older than 5 seconds; clients may receive a recent change twice and deduplicate by entry id and version.
- *Capacity targets (owner decision, "mid-size", one server):* 2,000 connected people, 100 messages a second, p95 send under 300 ms, p95 history page under 200 ms, no errors. `tools/Audorix.ChatX.LoadTest` measures them against any installation through the public API (it needs a test installation in development mode); it also reports delivery latency to each recipient (target p95 under 1 s).
- *Web bundle.* Pages other than the conversations, home and sign-in pages are loaded on demand (`React.lazy`), the SignalR client is loaded when realtime starts, and `scripts/check-bundle.mjs` fails CI when the first download exceeds 180 kB gzip or any chunk 80 kB.
- *Presence audience (owner decision, after the first baseline).* Presence goes to contacts and to people sharing an active direct chat or group, minus blocks, instead of everyone sharing an organization. Channels and website chats do not count (their size is unbounded; groups are capped by `Conversations:MaxGroupParticipants`). The audience was the whole organization, so connecting everyone in a 2,000-person organization meant about 4 million announcements; it is now bounded by a person's chats.
- *Conversation list pages.* `GET /api/v1/conversations/page?limit=&after=` returns the list in the same order 50 at a time (at most 200); the existing unpaged endpoint stays for other callers. The page boundary is (activity time, id), so a conversation that becomes active moves to the top and arrives through realtime updates rather than being repeated.

**Consequences:** The change log grows with message volume (one small row per change) and is pruned after a day. The feed covers history entries and reactions; receipts, membership and conversation settings are refreshed by reloading the conversation and list, as before. The list endpoint still reads all of a person's memberships and pages after sorting, which keeps the response and profile lookups small but not the query; a database-side page is a later step if lists of thousands of conversations appear. **The targets are not met yet:** the first baseline ([Phase 22 record](releases/phase-22-performance.md)) sustained about 25 messages a second with 2,000 people on a shared workstation. It also showed that presence announcements grew with the square of an organization's size, which the presence decision above removes; throughput remains follow-up work. People who only share an organization no longer see each other online; they see each other once they talk.

### ADR-037: A lighter send path

**Status:** Accepted — 2026-11-23, by the project owner, for Phase 23 (send-path speed; scope: fix the known costs, deeper changes only with the owner's go-ahead).

**Context:** The Phase 22 baseline sustained about 25 messages a second with 2,000 people. One send ran 22 statements inside the request: the session check, access checks, the save, then the inline publish re-read the outbox row and the message and described it, and the response described it a second time. Under load, concurrent sends in one group lost the sequence race three times and reached clients as 409.

**Decision:**

- *Describe once.* After saving, a new message is described once and that view is both published and returned; the outbox row stays as the safety net and is removed after publishing, exactly as before. A message saved a moment ago cannot have reactions, pins or thread replies, and has files or mentions only when the send added them, so those lookups are skipped (`ConversationService.DescribeNewAsync`). History, edits and the background dispatcher still describe in full.
- *Session check cache.* `SessionCheckCache` (singleton) trusts a session validated within the last 15 seconds, never past its expiry and never when its activity write (every 5 minutes) is due. Revoking through `SessionService` (sign-out, "sign out other sessions", password change, administration, account deletion) removes the entry at once. With several API instances another instance may accept a revoked session for up to 15 seconds; realtime connections were already re-checked every 30 seconds.
- *Sequence conflicts.* A send retries up to 8 times (was 3) with a random back-off of 5–20 ms doubling up to 200 ms, before answering 409.

**Consequences:** A plain text send runs 8 statements (was 22). On the Phase 22 workstation, 2,000 people now meet every target at 50 messages a second (send p95 80 ms) and reach 42.8 of 100 messages a second when overloaded ([Phase 23 record](releases/phase-23-send-path.md)); 100 is not met yet. Revocation on other instances is delayed by up to 15 seconds when scale-out is used. The background push and search queues still work per message and are the next candidates.

## Decision register requiring product input

| Decision | Current source / uncertainty | Recommended point of resolution |
| --- | --- | --- |
| Personal chat ownership | **Resolved** — ADR-021 accepted (explicit personal scope) | Done (2026-11-22) |
| Joining/leaving group history | **Resolved** (owner, 2026-11-22): members see history from the entry recording their join; people who leave or are removed lose access entirely; rejoining starts history again. | Done (Phase 5) |
| Direct conversations | **Resolved** (owner, 2026-11-22): personal direct chats only between contacts, organization direct chats between active members; one per pair per scope; never across a block. | Done (Phase 5) |
| Group roles and membership | **Resolved** (owner, 2026-11-22): owner / administrator / member; administrators manage members and settings, owners also roles; the longest-standing member becomes owner when the last owner leaves; personal groups add the adder's contacts, organization groups active members; leaving an organization ends its conversations. | Done (Phase 5) |
| Blocking within an existing group | **Resolved** (owner, 2026-11-22): a block never removes anyone from a shared group, but nobody can add a person they blocked or who blocked them, and no direct conversation can be opened across a block. Per-viewer filtering of a blocked person's messages is not required. | Done (Phase 5) |
| Edit and delete policy | **Resolved** (owner, 2026-11-22): authors edit their own messages for 15 minutes (`Messaging:EditWindow`) and delete them any time; deleted messages leave a tombstone; group owners and administrators may delete others' messages. Plain text up to 4000 characters with replies. | Done (Phase 6) |
| Idempotency retention | **Resolved** (owner, 2026-11-22): client message ids are unique per conversation and sender for ever (no expiry); a retry returns the original, a different body is a conflict. | Done (Phase 6) |
| Public user discovery | **Resolved** (owner, 2026-11-23): the Phase 3 default is kept — exact-email lookup for any signed-in user; name search only among people sharing an active organization; emails never returned; both switchable in the administration. | Done (Phase 13) |
| Presence visibility | **Resolved** (owner, 2026-11-23): the Phase 4 default was kept in Phase 13, then **changed in Phase 22** (owner, 2026-11-23, ADR-036) — presence is visible to contacts and people sharing an active direct chat or group (not a channel or website chat), never across a block; organization membership alone no longer shows presence; no "appear offline" option. | Done (Phase 22) |
| Installation setup | Owner-requested setup wizard (2026-11-22): protected by a setup code from the server log; writes a settings file below environment variables; offers a development mode without email confirmation. Unattended installs may preset `Setup:Token` or configure everything by environment. | Done (revisit with Phase 9 administration) |
| Attachment limits | **Resolved** (owner, 2026-11-22): 25 MB per file, 10 per message, 1 GB per person (all configurable); allow-list of images, PDF, Office, text/CSV and ZIP with content-signature checks; pluggable ClamAV scanning, off by default (quarantine until clean when on); unsent uploads removed after 24 hours; deleting a message deletes its files. | Done (Phase 7) |
| Private chats | **Resolved** (owner, 2026-11-22): ADR-025 accepted — one-to-one by accepted invitation, in memory only, living as long as the initiator's sign-in session, files in memory and wiped at the end. | Done (Phase 7) |
| Account deletion and retention | **Resolved** (owner, 2026-11-23): ADR-027 accepted — messages stay as "Deleted user"; deletion waits while someone is the only owner of an organization; retention of inactive anonymous visitors (90 days), never confirmed accounts (30 days) and, optionally, old messages. Advanced policies stay deferred. | Done (Phase 13) |
| Registration and organization creation | **Resolved** (owner, 2026-11-23): open registration and anyone may create organizations by default; administrators switch registration to invitation-only or closed and restrict organization creation to administrators, live from the administration. | Done (Phase 9) |
| Installation administration | **Resolved** (owner, 2026-11-23): feature switches, limits and languages are live database settings; providers are validated and saved to the settings file (restart required, secrets write-only); administrators manage accounts (disable, sign out, confirm, roles) without content access; a read-only Auditor role; audit log of administrative and security events kept 365 days. | Done (Phase 9) |
| Widget isolation and identity flow | **Resolved** (owner, 2026-11-23): ADR-024 accepted — iframe and loader; HS256 identity tokens signed by the host server with the widget secret; chosen organization members answer; balanced limits (anonymous sessions 30 days sliding, one conversation per visitor, 20 messages/minute, 5 anonymous sessions/hour per address, attachments for identified visitors only). | Done (Phase 10) |
| Channel access, guest history, team inheritance | **Resolved** (owner, 2026-11-23): ADR-026 accepted — public/private channels with history from the join; channel-scoped guests (history from their join, switchable per organization); teams as member groups, administration inheriting from organization to workspace to channel; Support Agent role with inbox, assignment and open/closed status. | Done (Phase 11) |
| Performance and capacity targets | **Resolved** (owner, 2026-11-23): ADR-036 — one server for 2,000 connected people and 100 messages a second, p95 send under 300 ms and history page under 200 ms, no errors; reconnect catch-up up to 500 changes or 24 hours, otherwise reload. | Done (Phase 22) |

These questions do not block creating the Phase 1 foundation. Resolve each before implementing the behavior it governs.
