# Phase 9 — Administration: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Applicable ADRs: 005, 007, 009, 010 (authorization), 013 (configuration), 021, 024, 025.

## Gate decisions (Implementation §12.2), owner answers of 2026-11-23

| Gate | Decision |
| --- | --- |
| Safe configuration changes | Feature switches, limits and languages are live database settings that apply immediately; providers (email, captcha, storage, scanning) are validated with the startup rules, tested on demand and saved to the settings file with a "restart required" notice; secrets are write-only; anything set by environment variables or the command line is locked. |
| Registration and organization creation | Open registration and organization creation by anyone remain the defaults; administrators can switch registration to invitation-only or closed and restrict organization creation to administrators. |
| Scope-specific admin rights | Administrators view accounts, disable/enable them (signed out everywhere), sign them out, confirm emails, resend confirmation and grant/revoke installation roles. No password setting, no deletion, no content access. New read-only **Auditor** role. |
| Audit and redaction | Administrative and security events (sign-in successes and failures, sign-out, registration, confirmation, password change/reset, session revocation, blocks, every admin action) with actor, target, IP and outcome; never message text, files, tokens, passwords or secret values; kept 365 days (configurable). |

## Delivered behaviour

- **Roles and permissions**: `InstallationAdministrator` (everything) and `Auditor`
  (`installation.read`, `users.read`, `organizations.read`, `audit.read`). Every admin endpoint has
  its own permission, checked against the account's current roles on each request, so granting or
  revoking takes effect immediately. The installation always keeps one enabled administrator;
  administrators cannot disable themselves.
- **API** `/api/v1/admin`: `overview`; `settings` (GET/PUT); `providers` (GET, PUT `{group}`,
  POST `{group}/test`); `users` (search/filter, detail, `disable`, `enable`, `revoke-sessions`,
  `confirm-email`, `resend-confirmation`, `roles` POST/DELETE); `roles`; `organizations` (search,
  detail, rename, member role, remove member, which also ends their organization conversations);
  `audit` (filters by action or prefix, actor, target, outcome; cursor by time).
- **Live settings** (database layer between the settings file and the environment, reloaded every
  30 s on each instance, applied per request): registration mode, organization creation,
  discovery, private chats and attachments switches, attachment/message/group/private-chat limits,
  default and available languages (request language follows them immediately), audit retention.
  Saves are validated per field and across settings (option classes' own rules).
- **Feature settings respected**: registration closed/invitation-only, organization creation for
  administrators only, private chats off, attachments off, message length, languages — all
  enforced on the next request (integration tests).
- **Disabled accounts** are locked out indefinitely in Identity and marked `disabled_at_utc`; all
  their sessions end, realtime connections close and private chats they started end.
- **Audit log** table with retention job; details are reduced to safe facts (setting names, role
  names, reasons) by name-based redaction.
- **Web app**: Admin item in the navigation for people with any admin permission; tabs Overview,
  Users, Organizations, Roles, Settings, Providers and Audit log; read-only for auditors; locked
  settings show their source; reset to default; provider secrets shown only as "set/not set";
  test buttons; EN/ES.

## Migration impact

`AddAdministration` (both providers): `audit_entries` (time stored as UTC ticks, indexes by time,
action, actor and target), `installation_settings` (key/value), `users.disabled_at_utc`.
Development installations apply it at startup; others run `migrate`.

## Validation results

| Check | Result | Evidence |
| --- | --- | --- |
| .NET build (Release, warnings as errors) | **Passed** | 0 warnings, 0 errors |
| .NET tests incl. real PostgreSQL 18 | **Passed** — 320 of 320 (9 min 22 s) | `dotnet test` with `CHATX_TEST_POSTGRES` |
| Acceptance: administrators can configure the platform | **Passed** | Live settings saved and applied on the next request; provider settings validated, written to the settings file, tested (storage probe, scanner). |
| Acceptance: unauthorized users cannot reach administration | **Passed** | Anonymous 401 and ordinary users 403 on every admin read; auditors read but get 403 on changes; granted/revoked roles apply on the next request; last administrator and self-disable refused. |
| Acceptance: provider configuration is validated | **Passed** | Postmark without token and SMTP user without password refused with the startup messages, nothing written; locked (environment) keys refused; secrets never returned or audited. |
| Acceptance: feature settings are respected | **Passed** | Registration closed and invitation-only, organization creation for administrators only, private chats off, attachments off, message length, default language — all enforced immediately; reset returns to defaults. |
| Audit log | **Passed** | Sign-in success/failure (disabled account reason, unknown accounts without address), admin actions with actor names; no passwords in records; prefix filters. |
| Frontend | **Passed** — 42 tests, type-check, production build | Administrator disables a user; settings save and locked display; read-only view for auditors. |
| Browser (scratch instance) | **Passed** | Administrator granted with `grant-role`; Admin navigation appeared; overview, settings (registration switched to invitation-only, marked "changed here"), providers (secrets shown as not set) and audit log (the change with actor and IP). |
| Docker profiles | **Not run** | Docker is not installed. |

## Open items

- Provider changes need a restart; the overview lists which groups are waiting. Live settings are
  shared through the database; provider settings live in each instance's settings file.
- No account deletion, password reset by administrators, custom roles or organization-scoped audit
  views yet. Organization administrators keep managing their own organizations as before.
- Failed sign-ins for unknown addresses are recorded without the address (privacy); repeated
  attempts are visible by IP only.
- Behind a reverse proxy the audit IP is the proxy's unless forwarded headers are configured.
- Docker profiles not run (Docker is not installed).

## Next slice

Phase 10 — Embedded chat (gate: ADR-024 widget isolation and identity flow).
