# Setup wizard: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-22. Requested by the
owner before Phase 4: choose the database type and connection, administrator credentials and
SMTP settings in a wizard, plus a development flag that removes the email-confirmation
requirement; clean up the existing development database.

Applicable ADRs: 004 (database providers), 006 (authentication), 015 (deployment), 018 (security).

## Delivered behaviour

- **Setup mode.** Without `Setup:Completed` the API starts a minimal host: `/health/live`,
  `/health/ready` (503 `setup_required`), the setup API under `/api/v1/setup`, and 503
  `setup_required` for every other API path. No database, Identity or application services are
  started. `migrate` and `grant-role` always run in normal mode.
- **Setup code.** A random code (`XXXX-XXXX-XXXX`, 60 bits) is printed to the server log at each
  start of setup mode, or taken from `Setup:Token` for automated installs. Every setup call except
  `GET status` requires it in `X-Setup-Token` (constant-time comparison, case and separators
  ignored, 60 requests per minute per IP).
- **Wizard steps** (web app, EN/ES, shown instead of the application while setup is required):
  setup code → database (SQLite file or PostgreSQL server/port/database/user/password/SSL, with
  *Test connection*) → email (SMTP or Postmark, with *Send test email*) → administrator →
  options (development mode, web address, default language, Turnstile keys or off) → review.
- **Completion** (`POST /api/v1/setup/complete`), one at a time: field validation; the candidate
  settings layered over the host configuration run through the same service graph and option
  validators as the real host; the administrator's input is checked; the database is probed,
  migrated (a missing PostgreSQL database is created) and initialized; the first installation
  administrator is created with a confirmed email and the `InstallationAdministrator` role; only
  then is the settings file written atomically. The process then restarts in normal mode, and the
  wizard waits for readiness before offering sign-in.
- **Settings file** `App_Data/chatx.settings.json` (configurable `Setup:SettingsPath`), loaded
  after user secrets and before environment variables. It contains database and SMTP passwords,
  is git-ignored, and is created owner-only on Unix. It never contains the setup code or the
  administrator password.
- **Locked database.** If environment variables or arguments configure the database (Docker
  profiles), the wizard shows it as locked and writes no `Database` settings.
- **Refusals.** A database that already has an installation administrator → 409
  `setup_administrator_exists`, nothing written. A second completion → 409
  `setup_already_completed`. Unreachable servers and bad credentials are reported as
  `unreachable` / `authentication_failed` without host names or provider messages.
- **Development mode** = `Identity:AutoConfirmEmail=true` and `Identity:RequireConfirmedEmail=false`:
  registration creates confirmed accounts and sends no email at all (also not to existing
  addresses, so the response still reveals nothing). The sign-up screen then says the account is
  ready; the API logs a startup warning.
- **Profiles.** The `sqlite`/`postgresql` launch profiles are replaced by one `chatx` profile;
  `appsettings.Development.json` no longer selects a database. Docker profiles keep their database
  in the environment and store the settings file in the data volume.

## Database clean-up

- `src/Api/Audorix.ChatX.Api/App_Data/chatx-dev.db` (with `-wal`/`-shm`) was deleted; `App_Data` is
  empty, so the next start shows the wizard.
- PostgreSQL (`localhost:5432`): no `chatx*` databases existed; all `chatx_test_*` databases
  created by tests are dropped (0 remain).

## Validation

| Check | Result | Evidence |
| --- | --- | --- |
| Setup API, SQLite and PostgreSQL | **Passed** | `SetupTests` (7) and the setup-mode CLI test: gating, setup code, validation writes nothing, provider details not leaked, complete setup then normal host from the file alone, administrator role, development-mode registration, existing administrator refused, locked database. |
| Unit tests | **Passed** | Setup code, settings file format, connection-string building, administrator validation. |
| Frontend | **Passed** | 4 wizard tests (gate, wrong code, full walk-through with request check, return to failing step). |
| Browser, end to end (fresh Vite → fresh API in setup mode, scratch settings path) | **Passed** | Setup code typed lower-case with spaces; SQLite test connection ("will be created"); SMTP defaults; administrator with password confirmation; options; review; install; automatic restart into normal mode; sign-in; `/api/v1/admin/installation` 200; email shown as verified; development mode reported by `/auth/config`; a second user registered and signed in without any email. The scratch instance was deleted afterwards. |
| Docker profiles | **Not run** | Docker is not installed. |

## Open items

- The setup code is regenerated at every start of setup mode; it is visible to anyone who can
  read the server log, by design.
- The wizard does not edit an existing installation; delete the settings file to run it again.
  Settings administration belongs to Phase 9.
