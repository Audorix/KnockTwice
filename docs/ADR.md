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

## ADR-015: Deployment

**Decision:** Support both self-hosted and SaaS deployment models.

Initial deployment uses Docker.

The application must support externalized configuration, persistent storage and independent database configuration.

The architecture must not require a proprietary cloud service.

## ADR-016: Scalability

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

**Status:** Proposed.

**Context:** ADR-019 prioritizes general chat, while the implementation roadmap also includes widgets and business channels. The draft's sample configuration enabled embedded chat before its implementation phase.

**Decision proposed:** Define R1 as core chat (Phases 1–9 plus applicable release work from Phase 12), R2 as embedded chat (Phase 10), and R3 as business extensions (Phase 11). Keep channels and widgets disabled until their release gates pass. Preserve basic organizations and workspaces in R1 for isolation. Treat reactions, support ticketing and automation as deferred.

**Consequence:** Phase numbers remain stable, but documentation, security and deployment work begins in Phase 1 and is required at every release. A feature flag cannot make an unimplemented feature available.

### ADR-021: Personal and organization conversation ownership

**Status:** Proposed; resolve before Phase 3 schema completion.

**Context:** General-purpose contact chat and organization isolation coexist, but the draft does not define ownership of conversations between personal users.

**Decision proposed:** Use an explicit `ScopeKind` with `Personal` and `Organization`. Personal conversations authorize by participation. Organization conversations require both active organization membership and conversation access; workspace-scoped conversations also require workspace access. Do not infer personal access solely from a null organization ID. Widgets belong to an organization, including a designated organization for a single-installation deployment.

**Alternative:** Require every conversation to belong to an organization and supply a default organization for consumer deployments. This is simpler structurally but changes the personal-chat model and needs an explicit product decision.

**Consequence:** Identical participants may have distinct personal and organization chats. Direct-conversation uniqueness includes scope. No implicit cross-organization sharing is introduced.

### ADR-022: Durable ordering and reconnect recovery

**Status:** Proposed; extends ADR-017 without changing its source record.

**Context:** Timestamp/ID ordering gives stable history, but it does not alone recover missed edits, deletions, receipts or out-of-order realtime notifications.

**Decision proposed:** Retain GUID identifiers and UTC timestamps. Add a transactionally allocated per-conversation message sequence and a separate durable change sequence. Use the message sequence for history cursors, and the change sequence for reconnect catch-up. Allocate sequences with database concurrency control, not an unprotected `MAX + 1`. A retained change feed contains only information authorized for the caller; expired cursors require a fresh snapshot.

**Consequence:** This supersedes timestamp/ID as the authoritative history cursor only if accepted. Clients deduplicate by event ID and reconcile mutations by entity version. Outbox publication remains at-least-once, not exactly-once. The change feed is a synchronization aid, not a conversion to event sourcing.

### ADR-023: Receipt semantics and membership revocation

**Status:** Proposed.

**Decision proposed:** “Sent” means committed to the database. “Delivered” requires an authenticated recipient client acknowledgement. “Read” requires a recipient read acknowledgement. Track monotonic participant watermarks and aggregate across that participant's devices. Dispatch success alone never creates a delivery receipt. Participants cannot acknowledge another participant's messages or positions they cannot access.

**Consequence:** Receipt state remains separate from notification delivery. Membership revocation invalidates API access, download access and realtime subscriptions; reconnecting does not restore revoked access. Exact history visibility on joining or leaving a group remains a product decision.

### ADR-024: Widget authentication and upload lifecycle

**Status:** Proposed.

**Decision proposed:** First-party web authentication uses secure HttpOnly cookies and CSRF protection. Embedded visitors use separate, short-lived, narrowly scoped tokens. A host backend signs or obtains identity assertions; the browser never receives a signing secret. Domain restrictions are an abuse-control layer, not proof of visitor identity. Prefer an iframe widget for isolation; a Shadow DOM implementation remains an alternative requiring its own browser security review.

**Consequence:** The iframe recommendation differs from the draft's Shadow DOM default and requires acceptance before Phase 10. Anonymous sessions need quotas and abuse limits. Attachments remain private and unavailable until validation completes; if configured scanning is unavailable, they remain quarantined. Object storage and database changes require compensating cleanup because they do not share one transaction.

## Decision register requiring product input

| Decision | Current source / uncertainty | Recommended point of resolution |
| --- | --- | --- |
| Personal chat ownership | Not defined; ADR-021 proposes an explicit personal scope | Before Phase 3 persistence design |
| Joining/leaving group history | Source does not define historical visibility | Before Phase 5 acceptance |
| Blocking within an existing group | Blocking exists, but interaction rules are unspecified | Before Phase 3/5 integration |
| Edit and delete policy | Windows, moderator rights and retention are unspecified | Before Phase 6 acceptance |
| Public user discovery | Search exists; discoverability defaults are unspecified | Before Phase 3 UI |
| Attachment limits | Configurable limits required; values and allowed types unspecified | Before Phase 7 release |
| Account deletion and retention | Advanced policies deferred; minimum deletion behavior still needed | Before public release |
| Registration and organization creation | SaaS and self-hosted defaults may differ | Before Phase 9 release |
| Widget isolation and identity flow | ADR-024 is a proposal | Before Phase 10 |
| Performance and capacity targets | No measured or agreed workload in the source | Before load-test acceptance |

These questions do not block creating the Phase 1 foundation. Resolve each before implementing the behavior it governs.
