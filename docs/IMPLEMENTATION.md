# Audorix.ChatX — Implementation Specification

> **Source and revision policy.** Prepared from the user-supplied `Pasted markdown(4).md`, which contains the ChatX documentation draft. The unavailable shared link was not used as evidence. Source technology choices and feature intent are retained. “Accepted” and “Approved” labels in that draft are not treated as evidence of separate user approval. New implementation recommendations below are proposals, not previously agreed requirements. These documents describe intended behavior; they do not certify implemented or tested software.

**Companion documents:** [ADR](ADR.md) · [Architecture](ARCHITECTURE.md) · [Implementation](IMPLEMENTATION.md).

**Document:** IMPLEMENTATION.md\
**Version:** 1.1 — documentation revision, 2026-11-22\
**Status:** Development draft derived from supplied conversation

## 1. Development objective

Implement Audorix.ChatX as a working open-source communication platform using the source architecture baseline.

Development must be incremental.

Each phase must produce working, testable functionality.

Do not implement all modules simultaneously.

## 2. Development conventions

### 2.1 Backend

- .NET 10.
- C#.
- Nullable reference types enabled.
- Async APIs.
- CancellationToken propagation.
- Dependency injection.
- Explicit DTOs.
- Structured logging.
- Centralized exception handling.
- OpenAPI documentation.
- Consistent validation.
- No infrastructure dependencies inside Domain.

Namespace root:

`Audorix.ChatX`

### 2.2 Frontend

- React.
- TypeScript strict mode.
- Vite.
- Bootstrap 5.
- Responsive CSS.
- Reusable components.
- i18next.
- Typed API contracts.
- Centralized API error handling.

npm scope: `@audorix`. Public SDK package: `@audorix/chatx`.

Other packages use names such as `@audorix/chatx-ui` and `@audorix/chatx-embed`; `@audorix/chatx` is a complete package name, not a nested npm scope.

### 2.3 Code quality

- Avoid duplicated business logic.
- Avoid unnecessary abstractions.
- Keep modules cohesive.
- Do not expose database entities through API endpoints.
- Do not put business logic in React components.
- Use interfaces where provider replacement is required.
- Write tests for critical business rules.
- Follow the established architecture dependency rules.

## 3. Implementation phases

### Phase 1 — Solution foundation

Deliverables:

1. Create .NET solution.
2. Create initial project structure.
3. Configure dependency injection.
4. Configure application settings.
5. Configure EF Core.
6. Implement SQLite provider.
7. Implement PostgreSQL provider.
8. Configure provider selection.
9. Configure separate migrations.
10. Create React application.
11. Configure TypeScript.
12. Configure Bootstrap.
13. Establish frontend package structure.
14. Configure localization.
15. Create Docker development environment.

Acceptance criteria:

- Solution builds.
- React application runs.
- API starts.
- Both database providers can be selected.
- Database migrations work independently.
- English and Spanish resources load.
- Docker development setup works.

### Phase 2 — Identity and authentication

Deliverables:

- ASP.NET Core Identity integration.
- Registration.
- Login.
- Logout.
- Password change.
- Password reset.
- Email verification.
- User profile.
- Avatar.
- User preferences.
- External authentication contracts.
- Google OAuth.
- Microsoft OAuth.
- Session management.
- Role and permission foundation.

Acceptance criteria:

- Users can register and authenticate.
- Invalid authentication attempts are handled securely.
- External providers can be enabled or disabled.
- User identity is available to application services.
- Authorization policies are enforced.
- User preferences persist.

### Phase 3 — Organizations and contacts

Deliverables:

- Organization management.
- Organization memberships.
- Workspace foundation.
- Workspace memberships.
- User search.
- Contact requests.
- Contact acceptance.
- Contact removal.
- Blocking.
- User discovery.

Acceptance criteria:

- Users can create or join authorized organizations.
- Organization data is isolated.
- Contact relationships are independent of organizational membership.
- Blocked users cannot initiate prohibited interactions.

### Phase 4 — Realtime infrastructure

Deliverables:

- SignalR integration.
- Authentication for Hub connections.
- Connection lifecycle.
- Multiple connections per user.
- Presence service.
- Reconnection handling.
- Typing indicators.
- Realtime event contracts.

Acceptance criteria:

- Authenticated clients can connect.
- Unauthorized clients cannot join protected conversations.
- Presence updates correctly.
- Multiple devices are supported.
- Reconnection does not duplicate application state.
- Typing indicators expire automatically.

### Phase 5 — Conversation engine

Deliverables:

- Conversation entity.
- Conversation participants.
- Direct conversations.
- Group conversations.
- Conversation settings.
- Participant management.
- Conversation listing.
- Conversation history.
- Unread counters.

Acceptance criteria:

- Users can create direct conversations.
- Users can create groups.
- Conversation membership is enforced.
- Conversation history is ordered consistently.
- Pagination works.
- Unread counts are correct.

### Phase 6 — Messaging engine

Deliverables:

- Message creation.
- Message persistence.
- Message retrieval.
- Message editing.
- Message deletion.
- Replies.
- Delivery receipts.
- Read receipts.
- Idempotency.
- Transactional outbox.
- SignalR message delivery.

Acceptance criteria:

- Messages are persisted before realtime publication.
- Duplicate requests do not create duplicate messages.
- Authorized participants receive realtime events.
- Message history remains available after reconnection.
- Read and delivery states are consistent.
- Failed event publication can be retried.

### Phase 7 — Attachment management

Deliverables:

- Storage abstraction.
- Local storage.
- S3-compatible storage.
- Upload API.
- Download API.
- Attachment metadata.
- File size restrictions.
- MIME validation.
- File signature validation.
- Authorization.

Acceptance criteria:

- Both storage providers work.
- File metadata persists.
- Unauthorized users cannot access attachments.
- Invalid file types are rejected.
- Large uploads are handled according to configured limits.

### Phase 8 — React chat application

Deliverables:

- Login and registration screens.
- User profile.
- Contact list.
- Conversation list.
- Direct chat.
- Group chat.
- Message composer.
- Message history.
- Typing indicators.
- Presence indicators.
- Read receipts.
- Attachment previews.
- Notifications.
- Light theme.
- Dark theme.
- Language selector.

Acceptance criteria:

- All core messaging functionality works.
- Desktop and mobile interfaces are usable.
- Message history is virtualized where necessary.
- Realtime events update the UI correctly.
- English and Spanish translations are complete.
- No critical layout issues exist on mobile.

### Phase 9 — Administration

Deliverables:

- Installation configuration.
- User management.
- Organization management.
- Role management.
- Provider configuration.
- Feature configuration.
- Localization configuration.
- Basic audit logs.

Acceptance criteria:

- Installation administrators can configure the platform.
- Unauthorized users cannot access administrative functionality.
- Provider configuration is validated.
- Feature settings are respected by the application.

### Phase 10 — Embedded chat

Deliverables:

- Widget configuration.
- Widget administration.
- Widget JavaScript loader.
- Embedded chat interface.
- React integration.
- Anonymous visitor sessions.
- Authenticated visitor tokens.
- Domain restrictions.
- Widget branding.
- Language selection.
- Responsive widget layout.

Acceptance criteria:

- Widget can be embedded on an external website.
- Anonymous visitors can initiate authorized conversations.
- Authenticated visitors can be identified securely.
- Widget configuration is loaded from the server.
- Unauthorized domains are rejected.
- Widget styling does not interfere with the host website.
- Mobile widget experience works.

### Phase 11 — Business communication

Deliverables:

- Advanced workspaces.
- Teams.
- Channels.
- Organization-specific configuration.
- Business permissions.
- Guest access.
- Support-agent foundations.

Acceptance criteria:

- Organizations can manage workspaces.
- Workspace membership is enforced.
- Channel permissions work.
- Organization configuration does not affect other organizations.

### Phase 12 — Open-source release

Deliverables:

- MIT license.
- README.
- Architecture documentation.
- ADR documentation.
- Installation guide.
- Docker deployment guide.
- API documentation.
- Configuration reference.
- Developer contribution guide.
- Security policy.
- Sample environment configuration.
- Database migration instructions.
- Example embedded integration.

Acceptance criteria:

- A developer can install the application using documentation alone.
- SQLite installation works.
- PostgreSQL installation works.
- Docker deployment works.
- API documentation is available.
- No secrets are included in the repository.
- Dependency licenses are reviewed.

### Phase 13 — 1.0 hardening

Added by the project owner on 2026-11-23 after release 0.1.0, to close what the release left open
(ADR-027).

Deliverables:

- Account deletion by the person and by installation administrators.
- Organization deletion by owners and installation administrators.
- Data retention: inactive anonymous visitors, never confirmed accounts, optional message retention.
- Owner decisions on the remaining "before public release" items (ADR-020, discovery, presence).
- Backup and restore procedure with a repeatable restore drill on both databases.
- Publish-ready React package for the website chat.

Acceptance criteria:

- Deleting an account erases personal data while conversations keep their history.
- Sole organization owners and the last administrator cannot leave the installation ownerless.
- Retention removes only what its settings select, on SQLite and PostgreSQL.
- A backup restores accounts, messages and referenced attachments coherently.

### Phase 14 — Message search

Added by the project owner on 2026-11-23 (ADR-028); "advanced search" leaves the deferred list
(§9) in this form.

Deliverables:

- Provider-based search: database full-text (SQLite, PostgreSQL) by default, OpenSearch optional.
- Search API with filters (conversation, sender, dates, files) and cursor paging.
- Indexing queue and background indexer; index rebuild for administrators.
- Search page in the web app with highlighted results and a conversation quick jump.

Acceptance criteria:

- People find only messages they can read, from their join, never deleted ones.
- Edits, deletions and retention reach the index.
- Both database providers behave the same; OpenSearch is configurable and testable from the administration.

### Phase 15 — Notifications

Added by the project owner on 2026-11-23 (ADR-029); push integrations leave the deferred list (§9)
in this form. Before this phase the demo data was enlarged on request (see the Phase 14 record).

Deliverables:

- Email notifications for unread messages: batched (default), daily or off per person.
- Web Push with VAPID and encrypted payloads; a service worker; subscriptions per browser and sign-in.
- Preferences in the web app; live switches for administrators.

Acceptance criteria:

- Unread messages are emailed once, batched, honouring the person's choice and the preview setting.
- Push reaches subscribed browsers only while the person is not connected, and stops when they sign out.
- Muted conversations, deleted and demo accounts, and people without access are never notified.

### Phase 16 — Reactions and threads

Added by the project owner on 2026-11-23 (ADR-030). Before this phase the demo data gained 500
channels on request (see the Phase 16 record).

Deliverables:

- Emoji reactions on messages (any emoji, at most 20 different per message), live for everyone in the conversation.
- Threads on messages: a side panel, optional echo to the conversation, follow / stop following, own unread count.
- Thread followers notified about replies; quote replies unchanged.

Acceptance criteria:

- Reactions toggle per person, are validated and limited, and reach others in real time.
- Thread replies stay out of the main timeline and its unread count unless echoed; the first message shows the reply count and the caller's unread replies.
- Both database providers behave the same; website chats have neither reactions nor threads.

### Phase 17 — Mentions

Added by the project owner on 2026-11-23 (ADR-031).

Deliverables:

- `@name` mentions with suggestions in the composer (conversation and thread), plus `@everyone` / `@here`.
- Per-conversation notification levels: all messages, only mentions, nothing (replaces mute).
- A Mentions view with unread state and a navigation badge.

Acceptance criteria:

- Only participants can be mentioned; `@everyone` / `@here` follow the size and role rule.
- Push and email follow the level: mentions reach people on "only mentions", nobody on "nothing".
- The Mentions view lists only messages the person can still read, with correct unread state, on both database providers.

### Phase 18 — Message tools

Added by the project owner on 2026-11-23 (ADR-032).

Deliverables:

- Pinned messages per conversation with a "pinned" history entry and a pins list.
- Saved messages (private bookmarks) in a tab next to mentions.
- Forwarding a copy (text and files, credited) with an optional comment.
- Opening mentions, search results, saved and pinned messages at the message itself.

Acceptance criteria:

- Pins follow the roles and the limit; deleted messages lose their pin.
- Saved messages are private and disappear when the message can no longer be read.
- Forwarded copies carry the original author, have their own files, and never expose the source conversation; both database providers behave the same.

### Phase 19 — Rich text

Added by the project owner on 2026-11-23 (ADR-033).

Deliverables:

- Markdown-style formatting rendered safely in messages, threads and the visitor widget, with highlighted mentions.
- A formatting toolbar and keyboard shortcuts in the composers.
- Plain text (no markers) in emails, push notifications, reply previews and lists.

Acceptance criteria:

- No HTML from a message is ever interpreted; only http, https and mailto links are clickable.
- Ordinary text (underscores in names, arithmetic) is not mistaken for formatting.
- Email and push show the words without markers.

### Phase 20 — Drafts, scheduled messages and reminders

Added by the project owner on 2026-11-23 (ADR-034).

Deliverables:

- Drafts synced across devices per conversation and thread, marked in the conversation list.
- "Send later" with quick times and a custom time; a Scheduled list to edit, cancel or remove.
- "Remind me" on messages; a Reminders list; due reminders in the app, by push and by email.

Acceptance criteria:

- Scheduled messages go out with the normal rules at their time; refused ones stay listed with the reason.
- Files of waiting scheduled messages are not cleaned up.
- Reminders come due once, notify as chosen, and disappear when marked done; both database providers behave the same.

### Phase 21 — Release readiness

Added by the project owner on 2026-11-23 after the status audit (ADR-035).

Deliverables:

- A GitHub Actions CI workflow (.NET with SQLite and PostgreSQL, web type-check, tests and build).
- Purging of ended sign-in sessions (and their push subscriptions) in the retention job.
- Optional Redis scale-out: SignalR backplane, shared presence and shared rate limits.

Acceptance criteria:

- Without Redis nothing changes; with Redis two instances agree on presence and share sign-in limits.
- Ended sessions older than the retention period are deleted; active ones are untouched.
- CI runs the same checks as `scripts/validate.ps1`.

### Phase 22 — Performance

Added by the project owner on 2026-11-23 (ADR-036).

Deliverables:

- Reconnect catch-up: a change log written with every history change, `GET /api/v1/changes` with a cursor, and the web app applying the changes after a reconnect (reload over 500 changes or after 24 hours).
- Route-level code splitting of the web app and a bundle budget checked in CI.
- A paged conversation list (`GET /api/v1/conversations/page`) used by the web app with "Show more conversations".
- `tools/Audorix.ChatX.LoadTest`, a repeatable load test with the agreed targets, and a measured baseline.

Acceptance criteria:

- After a reconnect, edits, deletions and reactions made while offline appear without a reload; people see only changes in conversations they may still see; more than 500 changes ask for a reload.
- The first download of the web app stays within its budget, enforced in CI.
- Pages of the conversation list together equal the full list, in order.
- The load test runs at the targets (2,000 connected people, 100 messages a second, p95 send < 300 ms, p95 history < 200 ms, no errors) and its results are recorded.

## 4. Initial API contracts

Base path:

`/api/v1/`

### Authentication

```text
POST   /auth/register
POST   /auth/login
POST   /auth/logout
POST   /auth/refresh                 # only if a separate token refresh flow is implemented
POST   /auth/forgot-password
POST   /auth/reset-password
GET    /auth/external/{provider}
```

### Users

```text
GET    /users/me
PUT    /users/me
GET    /users/{id}
GET    /users/search
```

### Contacts

```text
GET    /contacts
POST   /contacts/requests
GET    /contacts/requests
POST   /contacts/requests/{id}/accept
POST   /contacts/requests/{id}/reject
DELETE /contacts/{id}
POST   /contacts/{id}/block
DELETE /contacts/{id}/block
```

### Organizations

```text
GET    /organizations
POST   /organizations
GET    /organizations/{id}
PUT    /organizations/{id}
GET    /organizations/{id}/members
POST   /organizations/{id}/members
```

### Conversations

```text
GET    /conversations
POST   /conversations
GET    /conversations/{id}
PUT    /conversations/{id}
GET    /conversations/{id}/participants
POST   /conversations/{id}/participants
DELETE /conversations/{id}/participants/{userId}
```

### Messages

```text
GET    /conversations/{id}/messages
POST   /conversations/{id}/messages
PUT    /messages/{id}
DELETE /messages/{id}
POST   /messages/{id}/read
```

### Attachments

```text
POST   /attachments
GET    /attachments/{id}
DELETE /attachments/{id}
```

### Widgets

```text
GET    /widgets
POST   /widgets
GET    /widgets/{id}
PUT    /widgets/{id}
POST   /widgets/{id}/visitor-session
```

These routes are initial contracts and may be refined as individual modules are implemented. All endpoints require appropriate authorization, validation and rate limiting.

## 5. Realtime contract

Initial SignalR endpoint:

`/hubs/chat`

Client events:

```text
JoinConversation
LeaveConversation
SendTypingStatus
```

Server events:

```text
MessageCreated
MessageUpdated
MessageDeleted
MessageDelivered
MessageRead
UserPresenceChanged
TypingStatusChanged
ConversationUpdated
NotificationReceived
```

All events must use versioned, documented contracts.

## 6. Configuration

Example application configuration:

```json
{
  "Database": {
    "Provider": "PostgreSql",
    "ConnectionString": ""
  },
  "Storage": {
    "Provider": "Local",
    "Local": {
      "Path": "./data/uploads"
    },
    "S3": {
      "Bucket": "",
      "Region": "",
      "Endpoint": ""
    }
  },
  "Realtime": {
    "Provider": "SignalR"
  },
  "Localization": {
    "DefaultLanguage": "en",
    "SupportedLanguages": [
      "en",
      "es"
    ]
  },
  "Features": {
    "Registration": true,
    "PrivateMessaging": true,
    "GroupMessaging": true,
    "Organizations": true,
    "Channels": false,
    "EmbeddedChat": false,
    "Attachments": true
  }
}
```

Secrets and production credentials must be supplied through environment variables or a suitable secret-management system.

Feature settings must be validated and enforced server-side.

## 7. Testing strategy

### Backend unit tests

Test:

- Domain rules.
- Message validation.
- Conversation membership.
- Permissions.
- Organization isolation.
- Contact rules.
- Idempotency.

### Integration tests

Run against:

- SQLite.
- PostgreSQL.

Test:

- Authentication.
- Database migrations.
- Message persistence.
- Conversation operations.
- Attachment metadata.
- Authorization.
- Outbox processing.

### Realtime tests

Test:

- Connection authentication.
- Group membership.
- Message delivery.
- Reconnection.
- Presence.
- Multiple devices.
- Unauthorized access.

### Frontend tests

Test:

- Components.
- Message rendering.
- Conversation navigation.
- Responsive behavior.
- Localization.
- API error handling.
- Realtime synchronization.

### Architecture tests

Verify:

- Domain has no infrastructure dependencies.
- Application boundaries are respected.
- Provider implementations are isolated.
- API contracts do not expose persistence entities.

## 8. Non-functional requirements

### Performance

- Cursor-based pagination.
- Efficient database indexes.
- Realtime event delivery.
- Lazy loading.
- Message virtualization.
- Optimized attachments.
- Minimal unnecessary frontend dependencies.

### Security

- HTTPS.
- Secure authentication.
- Authorization.
- Tenant isolation.
- Rate limiting.
- Secure uploads.
- Origin validation.
- XSS protection.
- CSRF protection where applicable.
- Audit logging.

### Reliability

- Persistent message storage.
- Transactional outbox.
- Idempotent message creation.
- Reconnection support.
- Background processing.
- Graceful error handling.

### Maintainability

- Modular project structure.
- Documented contracts.
- Provider abstractions.
- Automated tests.
- ADR maintenance.
- Consistent naming.

## 9. Explicitly deferred functionality

The following are outside the initial MVP:

- Voice calls.
- Video calls.
- Screen sharing.
- AI assistants.
- AI message translation.
- Advanced search (basic message search arrived in Phase 14; relevance ranking, typo tolerance and file contents remain deferred).
- Advanced analytics.
- Bots.
- Workflow automation.
- Advanced customer support ticketing.
- Push notification integrations (Web Push and notification emails arrived in Phase 15; native mobile push remains deferred).
- Advanced retention and compliance policies.

The architecture must allow these features to be introduced without replacing the messaging engine.

## 10. Definition of Done

A feature is considered complete when:

1. Domain rules are implemented.
2. Application use cases are implemented.
3. API contracts are available where required.
4. Authorization is enforced.
5. Localization is implemented.
6. Meaningful automated checks cover critical rules and failure cases.
7. Integration tests cover persistence, authorization and external boundaries where relevant; do not add tests that merely mirror implementation.
8. Frontend integration works where applicable.
9. Error handling is implemented.
10. Documentation is updated.

## 11. Recommended first development milestone

Start with Phase 1 only.

Do not immediately generate all business modules, messaging functionality or the embedded widget.

The first milestone should produce:

- Audorix.ChatX solution.
- Correct namespace conventions.
- Clean project dependencies.
- SQLite provider.
- PostgreSQL provider.
- Configuration-based database selection.
- EF Core migrations.
- React + TypeScript application.
- English and Spanish localization.
- Docker development environment.
- Basic API health endpoint.
- Automated build and initial tests.

Once the foundation is stable, proceed to Identity.

---

**Project identity:** Audorix.ChatX\
**Root namespace:** Audorix.ChatX\
**npm scope:** @audorix; **SDK package:** @audorix/chatx\
**License:** MIT\
**Architecture:** Modular monolith\
**Initial release:** General-purpose chat with business-ready and embeddable foundations.

## 12. Proposed release and execution gates

This section makes the draft actionable without claiming the additional proposals are accepted. Preserve the numbered phases above. Phase 12 is release work that runs alongside development, not a reason to postpone all documentation until Phase 11 is complete.

| Release | Included phases | Exit requirement |
| --- | --- | --- |
| Foundation checkpoint | Phase 1 | Both database profiles, API and localized UI build and start |
| R1 core chat | Phases 1–9 and applicable Phase 12 tasks | Direct/group chat, contacts, attachments, admin and EN/ES validated |
| R2 embedded chat | Phase 10 plus release documentation | Anonymous and authenticated widget flows validated on an external origin |
| R3 business extensions | Phase 11 plus release documentation | Teams, channels, guest rules and workspace isolation validated |

Channels, teams, support-agent workflows and widgets are unavailable until their phases pass. General/business/embedded modes select capabilities of the same messaging engine. Disabled features must be blocked in API policy as well as hidden in navigation.

### 12.1 Foundation work order

1. Inspect the repository and existing instructions; preserve unrelated work. Confirm this is documentation-only until implementation is explicitly requested.
2. For implementation, create the minimal project dependencies in Architecture section 17.1 and apply `Audorix.ChatX` namespaces.
3. Add configuration validation and database provider selection; unsupported provider values fail startup with a useful error.
4. Establish provider-specific migration assemblies. Exercise a minimal persisted foundation record on both providers without creating all future entities.
5. Add API error envelopes, health endpoints and structured logs with sensitive-field exclusion.
6. Add the React/TypeScript/Vite shell, Bootstrap styles and EN/ES language switching.
7. Supply Docker development profiles, example configuration with no secrets, persistent volumes and migration instructions.
8. Run backend/frontend builds, architecture dependency checks and real-provider migration/startup checks. Record commands and results.

**Out of scope for this checkpoint:** full Identity, conversation/message tables, widget loader, channels, AI and distributed infrastructure. A migration provider being selectable is not enough; both profiles must actually run.

### 12.2 Decisions and dependency gates by phase

| Phase | Required clarification or dependency |
| --- | --- |
| 2 Identity | Session lifecycle, email delivery adapter, account recovery and external-login callbacks |
| 3 Organizations/contacts | ADR-021 ownership choice, discovery defaults and blocking behavior |
| 4 Realtime | Authenticated connections and expiring ephemeral state; use a protected fixture until Phase 5 |
| 5 Conversations | Historical visibility on join/leave, role changes and direct-chat uniqueness |
| 6 Messaging | ADR-022 ordering decision, receipts, mutation windows and idempotency retention |
| 7 Attachments | Actual quotas, allowed types, scan policy and cleanup lifecycle |
| 8 Web UI | Integration of all core flows, accessibility, optimistic-state reconciliation and failure states |
| 9 Admin | Scope-specific admin rights, audit redaction and safe configuration changes |
| 10 Widget | Isolation choice, signed identity flow, visitor limits and domain policy |
| 11 Business | Channel access model, guest history and workspace/team permission inheritance |

Phase 5 may define history contracts and placeholder read state, but final unread accuracy and receipt acceptance depend on Phase 6. UI integration should accompany each backend slice; Phase 8 completes the experience rather than delaying all user-visible validation.

### 12.3 Contract completion checklist

The source route list is a starting inventory, not a complete OpenAPI specification. Before each module is complete, specify request/response schemas, auth policy, status codes, limits, pagination and error codes. Add the following missing operations when their phase is implemented:

| Capability | Proposed contract addition |
| --- | --- |
| Identity | Email verification, password change, external callback and session revocation |
| Workspace access | Workspace and membership operations scoped to the owning organization |
| Receipts | Delivery acknowledgement and conversation read-watermark update |
| Reconnect | Conversation change feed if ADR-022 is adopted |
| Notifications | List and mark-read operations; keep notification state separate from message read state |
| Attachments | Upload intent/finalization or equivalent server upload lifecycle with readiness status |
| Widgets | Public configuration, visitor session refresh/revocation and server-authenticated identity exchange |

Keep `/api/v1/` and `/hubs/chat` from the source baseline. Cookie sessions do not require inventing a JWT refresh endpoint. Token flows must define rotation and revocation explicitly if added.

### 12.4 Required acceptance scenarios

| Scenario | Expected result | Verification layer |
| --- | --- | --- |
| Same send retried concurrently | One persisted message; equivalent successful response | SQLite and PostgreSQL integration |
| Same idempotency key, different body | Conflict; original message unchanged | API integration |
| Crash before transaction commit | No message or outbox record survives | Persistence integration |
| Crash after commit, before dispatch | Message recoverable; outbox later dispatches | Worker integration |
| Publish succeeds, completion write fails | Retry possible; UI shows one message | Realtime/client integration |
| Recipient offline during send/edit/delete | Correct state after recovery, including tombstones | Browser integration |
| Member removed with open socket | No subsequent protected delivery or download access | Realtime/API integration |
| Resource ID from another organization | No data disclosure or mutation | API integration |
| Reply or attachment references another chat | Rejected despite a valid ID | API integration |
| Two devices send read acknowledgements out of order | Read state never moves backwards | Integration |
| Forged widget identity or wrong audience | Session/token rejected | Widget integration |
| Invalid or oversized upload | Rejected or quarantined; no public object exposed | Storage integration |
| Scan service unavailable when required | File remains unavailable | Storage integration |
| Restore from backup | Accounts, messages and referenced attachments recovered coherently | Disposable restore drill |
| EN/ES and keyboard use | Core flow usable; no missing keys or inaccessible critical controls | Frontend/browser review |

Use actual SQLite and PostgreSQL databases for provider-sensitive checks, not EF in-memory substitutes. Verify Local and S3-compatible adapters with the same application contract tests. Use controlled faults for reliability checks, never live production data. Do not report an acceptance scenario as passed without running it.

### 12.5 Release evidence and handoff

For each completed slice record: delivered behavior; changed projects; applicable ADRs; migration impact; exact validation commands and outcomes; unresolved blockers; and next slice. Keep performance results tied to measured hardware, workload, message size, connections and storage profile. No numeric SLA is implied by the original draft.

Before R1 release, include install and update instructions, backup/restore procedure, API reference, configuration reference, MIT license with confirmed copyright attribution, contribution guide and a real security-reporting contact. Do not invent contact addresses. Review dependency licenses and public default settings. This documentation task creates the three specifications; the remaining repository files are implementation deliverables.
