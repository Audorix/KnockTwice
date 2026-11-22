# Phase 1 — Solution foundation: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-22.
Applicable ADRs: 001, 002, 003, 004, 009, 013, 014, 015. ADR-020 to ADR-024 remain **Proposed**
and were not implemented.

## Delivered behaviour

- .NET 10 solution `Audorix.ChatX.slnx` with Domain, Application, Contracts, Infrastructure,
  Persistence.Sqlite, Persistence.PostgreSql and Api projects, plus unit, architecture and
  integration test projects ([boundaries](../development/architecture-boundaries.md)).
- Strongly typed, start-up-validated configuration; `Database:Provider` accepts only `Sqlite`
  or `PostgreSql` and has no fallback ([configuration](../development/configuration.md)).
- Shared EF Core model with separate migrations/snapshots per provider; internal `installation`
  foundation record (single row, enforced by the database); explicit, idempotent `migrate` command.
- API host: DI, cancellation propagation, structured (JSON) logging, centralized exception
  handling, RFC 9457 problem responses with stable `code` and `traceId`, EN/ES localized error
  titles, `/health/live`, `/health/ready`, OpenAPI (Development), `/api/v1/meta`, no CORS (same-origin via proxy).
- React 19 + TypeScript 7 (strict) + Vite 8 + Bootstrap 5.3 foundation screen
  (`@audorix/chatx-web`) with EN/ES (i18next), persisted preference, locale-aware formatting,
  readiness indicator (loading / ready / not ready / unavailable), keyboard and mobile support,
  system dark mode. Original design; no code, styles or assets from the local demo template.
- Docker: API and web images, SQLite and PostgreSQL Compose profiles with persistent volumes
  ([docker](../development/docker.md)).
- Scripts: `scripts/validate.ps1|.sh`, `scripts/add-migration.ps1|.sh`.

## Migration impact

New migration `InitialFoundation` for each provider, creating table `installation`
(`id`, `default_language`, `initialized_at_utc`, `updated_at_utc`, `singleton` with unique
index and `singleton = 1` check). No existing databases were modified.

## Validation results

Environment: Windows 11, .NET SDK 10.0.404 / runtime 10.0.15, Node 24.21.0, npm 11.19.0.
Docker and Git are **not installed** on the validation machine.

| # | Check | Result | Evidence |
| --- | --- | --- | --- |
| 1 | Restore and build .NET solution | **Passed** | `scripts/validate.ps1`: Release build, 0 warnings, 0 errors (warnings are errors). |
| 2 | Frontend `npm ci`, type-check, production build | **Passed** | `npm ci` from lockfile; `tsc -b` clean (and verified to fail on an injected type error); `vite build` succeeded. |
| 3 | Clean real SQLite: migrate, write, read after reopening | **Passed** | `PersistenceTests` (Sqlite) on fresh temp files; dev DB created via `migrate` command. |
| 4 | Clean real PostgreSQL: same | **Passed** | `PersistenceTests` (PostgreSql) against local PostgreSQL 18 on freshly created `chatx_test_<guid>` databases, all dropped afterwards (0 left, verified). |
| 5 | Re-migrate: no duplicates, no pending changes | **Passed** | `Rerunning_migrations_...` and the real executable's `migrate` run twice, on both providers; single-row constraint verified on both; `HasPendingModelChanges() == false` for both snapshots. |
| 6 | Provider-specific migration discovery and selection | **Passed** | `Configured_provider_selects_its_own_migrations_assembly` for both providers. |
| 7 | Invalid provider fails helpfully without leaking credentials | **Passed** | Real executable exits with code 2 and names the setting, for HTTP and `migrate` modes; unit tests cover unknown/missing provider, malformed connection strings and pasted secrets. |
| 8 | Liveness/readiness incl. database unavailable | **Passed** | Unreachable PostgreSQL: live 200, ready 503 with no host/credential text; SQLite: ready 503 before migrate, 200 after. |
| 9 | Dependency boundaries | **Passed** | `ArchitectureTests` (project graph, packages, compiled references, contract types). |
| 10 | Both Docker profiles, persistence across recreation | **Not run** | Docker is not installed on the validation machine. Files are written but unexercised. |
| 11 | UI in EN/ES, persistence, unavailable state, keyboard, mobile width | **Passed** | Browser check against Vite + API: unavailable state with API stopped; ready state with API running; Tab reaches the language select, ArrowDown switches to Spanish, `lang`/title update, choice survives reload; 375 px width without horizontal scroll; dark scheme. Plus 11 Vitest tests. |

Test totals (`dotnet test`, Release, with `CHATX_TEST_POSTGRES` set): 67 total, 67 passed,
0 failed, 0 skipped. Without the variable the 4 PostgreSQL cases report as skipped.

### Completing the open check

Docker (#10), on a machine with Docker:

```powershell
docker compose -f docker/compose.sqlite.yaml up --build -d
docker compose -f docker/compose.sqlite.yaml down
docker compose -f docker/compose.sqlite.yaml up -d
```

Then confirm `http://localhost:8080` reports **API ready** and that the `migrate` logs of the
second start say "0 migration(s) applied; installation unchanged". Repeat with
`docker/compose.postgres.yaml`.

## Open items and decisions for the owner

- **Copyright holder** in `LICENSE` is a placeholder; confirm before public release.
- **Repository hosting/CI** is not established (no Git repository on the validation machine), so
  no CI workflow was added; `scripts/validate.*` is ready to be called from one.
- `CONTRIBUTING.md` and `SECURITY.md` (with a real contact) remain Phase 12 items.
- Server logs keep exception stack traces for diagnosis; responses never do.

## Next slice

Phase 2 — Identity and authentication (not started).
