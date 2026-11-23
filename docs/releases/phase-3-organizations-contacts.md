# Phase 3 — Organizations and contacts: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-22.
Applicable ADRs: 008 (multi-tenancy), 010, 013, 018, and **ADR-021, accepted by the owner** for this
phase (explicit personal/organization scope; recorded in [ADR.md](../ADR.md)). ADR-020, 022, 023,
024 remain Proposed.

## Gate decisions (Implementation §12.2)

| Gate | Decision |
| --- | --- |
| ADR-021 ownership choice | Accepted as proposed (owner instruction). Conversation scope fields arrive in Phase 5. |
| Discovery defaults | Exact-email lookup for any signed-in user; name search only among people sharing an active organization; emails never returned. Configurable (`Discovery:*`). Recorded in the ADR decision register as a revisable default. |
| Blocking behaviour | A block stops requests both ways, removes the contact, closes pending requests, hides both users from each other's search; the blocked user sees "not found". Organization membership is unaffected. Group effects remain open for Phase 5. |
| Joining organizations | By invitation to an email address, accepted by the invitee. Any signed-in user may create organizations (`Organizations:AllowCreation`). |

## Delivered behaviour

- Organizations: create (creator becomes owner, a "General" workspace is created), list, get, rename.
- Memberships: roles `owner` / `administrator` / `member` with permissions; role changes by owners;
  removal and leaving; last-owner protection; revocation also ends workspace memberships.
- Invitations: create/revoke (owners and administrators), list/accept/decline (invitee only),
  14-day expiry, no account enumeration.
- Workspaces: create (organization administrators), list (members see their own, administrators
  see all), member add/remove (workspace or organization administrators; only organization members).
- Users: `GET /users/search` and `GET /users/{id}` (visible only with a relationship).
- Contacts: list, request, accept, reject, cancel, remove; crossing requests become a contact.
- Blocking: block, unblock, list blocked.
- Web app: Contacts page (search, requests, contacts, blocked) and Organizations pages (list,
  create, invitations, members, roles, invitations management, workspaces and their members), EN/ES.
- Fix: `vite.config.ts` now reads `.env` files from the app directory regardless of working directory.

## Migration impact

`AddOrganizationsAndContacts` (both providers) creates `organizations`, `organization_members`,
`organization_invitations`, `workspaces`, `workspace_members`, `contacts`, `contact_requests`,
`user_blocks`, including composite foreign keys for workspace membership, a filtered unique index
for pending requests and check constraints. Existing tables are unchanged.

## Validation results

| Check | Result | Evidence |
| --- | --- | --- |
| .NET build (Release, warnings as errors) | **Passed** | `scripts/validate.ps1 -SkipFrontend`: 0 warnings, 0 errors |
| .NET tests incl. real PostgreSQL 18 | **Passed** — 153 total, 153 passed, 0 skipped | Organization lifecycle, isolation, contacts and blocking run on SQLite **and** PostgreSQL; DB-level rules verified on both; 0 leftover test databases. |
| Frontend type-check, tests, build | **Passed** — 20 tests | Run individually (`npm ci` blocked, see below). |
| Browser, end to end (fresh Vite → fresh API on 5081 → SQLite) | **Passed** | Sign-in; create organization; invite by email (listed as pending, not as member); exact-email search shows a result without email; send contact request; second user sees invitation, joins as Member, accepts the request; both are contacts; name search now finds the colleague. Spanish and English UIs exercised through account preferences; 446 px width. |
| Phase 3 acceptance: create or join authorized organizations | **Passed** | `OrganizationTests` + browser. |
| Phase 3 acceptance: organization data isolated | **Passed** | Every cross-organization read/write → 404; composite FK rejects forged cross-organization rows (both providers). |
| Phase 3 acceptance: contacts independent of membership | **Passed** | Contact survives joining and leaving a shared organization (both providers). |
| Phase 3 acceptance: blocked users cannot initiate prohibited interactions | **Passed** | `ContactTests` (both providers). |
| Docker profiles | **Not run** | Docker not installed. |
| `npm ci` | **Not run (environment)** | Orphaned dev processes from earlier previews (PIDs 25260 on 5173 and 11664 on 5080) still lock files and ports; they could not be stopped from this session. |

## Open items

- Invitation emails are not sent (no notification module yet); invitees see invitations in the app.
- Organization deletion, workspace rename/archive and ownership transfer workflows beyond role
  changes are not implemented.
- The last-owner rule is enforced in the application only (rare concurrent demotions could race).
- Blocking inside group conversations is a Phase 5 decision.

## Next slice

Phase 4 — Realtime infrastructure (SignalR, authenticated hub connections, presence, typing).
Its gate: authenticated connections and expiring ephemeral state, using a protected test fixture
until conversations exist in Phase 5.
