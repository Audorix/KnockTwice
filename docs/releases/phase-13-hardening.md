# Phase 13 — 1.0 hardening: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Scope: [Implementation, Phase 13](../IMPLEMENTATION.md#phase-13--10-hardening), added by the project
owner after release 0.1.0. Applicable ADRs: 020 (now accepted), 027 (new), 007 (attachments), 008
(user-owned data), 024 (widget visitors).

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 13 is | 1.0 hardening: what 0.1.0 left open (deletion, retention, restore drill, React package) |
| Messages of a deleted account | Stay where they are and show "Deleted user" |
| Deleting the only owner of an organization | Refused until the organization is handed over or deleted; nothing is transferred automatically |
| Retention | Inactive anonymous visitors (90 days), never confirmed accounts (30 days), and optional message retention (off by default) |
| Interim defaults | ADR-020 accepted; people discovery and presence visibility kept as they are |

The question offered "publishing the React package as `@audorix/chatx`". The architecture
(§4.1) names the embed package `@audorix/chatx-embed` and keeps `@audorix/chatx` for the future
client SDK, so the package keeps its documented name.

## Delivered

| Area | What |
| --- | --- |
| Account deletion | `GET`/`POST /api/v1/users/me/deletion` (password, or the email address for accounts without one); `DELETE /api/v1/admin/users/{id}`. `AccountDeletionService` leaves organizations and conversations through the existing services, then `EfAccountErasure` erases identity, profile, sessions, contacts, requests, blocks, team and guest records, responder entries, assignments, unsent uploads and visitor records in one transaction. Refused with `409 sole_owner` / `409 last_administrator`; administrators cannot use the admin route on themselves (`409 cannot_delete_self`). Audited. |
| Deleted people in the API | `deleted` on user summaries; deleted accounts hidden from administration lists, people search and email lookup; direct conversations with them reject new messages (`403 recipient_deleted`). |
| Organization deletion | `POST /api/v1/organizations/{id}/delete` (owners) and `POST /api/v1/admin/organizations/{id}/delete`, both with `confirmName`. File content is removed from storage first (a storage failure stops it with nothing deleted); history, files records, conversations and then the organization with everything that cascades from it; the organization's website visitors are erased. Members get `ConversationUpdated: removed`. |
| Retention | `DataRetentionService` and a hosted job every six hours; `Retention:VisitorInactiveDays` (90), `Retention:UnconfirmedAccountDays` (30, only while confirmation is required), `Retention:MessageDays` (0 = off). Live settings in **Admin → Settings → Data retention**. Audited as `retention.applied`. |
| Migration `AddAccountDeletion` | `users.deleted_at_utc`, `user_profiles.deleted_at_utc`, `conversation_events.created_at_ticks` with an index, backfilled from `created_at_utc` (provider-specific SQL; verified against .NET ticks on both databases). Set on insert by the context, so retention compares ages in SQL on SQLite too. |
| Web app | Account page: **Delete account** (blockers with links, password field, sign-out with a confirmation on the sign-in page). Organization page: **Delete organization** for owners (type the name). Administration: delete user, delete organization, retention settings. Deleted people appear as a localized "Deleted user" everywhere (one rule in the API client); a direct chat with them shows a notice instead of the composer. English and Spanish. |
| Backup and restore | [backup-restore.md](../backup-restore.md) (SQLite, PostgreSQL, Docker, S3, deleted data in backups) and `scripts/restore-drill.ps1`. Installation, Docker and testing guides link to it. |
| React package | `@audorix/chatx-embed` 0.1.0 is publish-ready: `prepack` build, `publishConfig.access: public`, license file, keywords, declared build tools. `npm pack --dry-run` lists `README.md`, `LICENSE`, `dist/index.js`, `dist/index.d.ts`, `package.json`. Not published. |
| Decisions | ADR-020 accepted; ADR-027 (deletion and retention); decision register: discovery, presence, deletion and retention resolved. |
| Documentation | Configuration reference (`Retention:*`), installation guide (deletion and retention, checklist), API guide (deleted accounts and deletion), OpenAPI reference re-exported (125 paths), changelog (Unreleased, with upgrade notes), README. |

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| Deleting an account erases personal data while conversations keep their history | **Pass** (both databases): erased account has no email, password, logins or roles; the address registers again as a new account; contacts and memberships gone; history and authorship kept as "Deleted user"; group owner succession; direct chat readable and closed. Checked in the browser as well. |
| Sole owners and the last administrator cannot leave the installation ownerless | **Pass**: `sole_owner` until ownership is handed over or the organization deleted; `last_administrator`; `cannot_delete_self` on the admin route. |
| Retention removes only what its settings select | **Pass** (both databases): nothing is due today; 550 days later exactly one anonymous visitor and one unconfirmed account are removed, the messages and their files are deleted, the conversations stay. |
| A backup restores accounts, messages and attachments coherently | **Pass**: restore drill on SQLite and on PostgreSQL 18 (pg_dump/pg_restore), 7 checks each, including attachment SHA-256. |

## Validation results

- .NET (Release, 0 warnings), SQLite and PostgreSQL 18: **371 tests** (213 unit, 20 architecture,
  138 integration including the 6 new `AccountDeletionTests`). In the first full run one
  integration test failed under load and was not identified (its output was filtered away); the
  integration suite then passed completely (138/138) on a re-run, as did unit and architecture tests.
- Web: type-check clean; **59 tests** (19 files) pass; production build OK; `@audorix/chatx-embed`
  builds and packs.
- Migration backfill expressions checked against .NET ticks on SQLite and PostgreSQL.
- Restore drill: SQLite and PostgreSQL, 7/7 checks each; no drill databases or folders left behind.
- Browser check (throwaway API and database): blocked deletion with the organization link,
  organization deletion with a wrong and then the correct name, account deletion and sign-out,
  and the other person's view of the direct chat.
- Every relative link in the 35 Markdown files resolves; no credentials, personal data or local
  paths in the repository.

## Open items

- Publish `@audorix/chatx-embed` to npm (needs the `@audorix` npm scope).
- Run `docker/compose.production.yaml` once on a Docker host (from Phase 12).
- Not in scope: whole-conversation retention, per-organization policies, legal hold, data export
  (Implementation §9).
- Realtime message events about old messages still carry the stored name of a deleted author; the
  next history load shows the localized label.
