# Testing and validation

## One command

```powershell
./scripts/validate.ps1
```

```bash
./scripts/validate.sh
```

This restores, builds (Release) and tests the .NET solution, then runs `npm ci`, type-check,
frontend tests and the production build. No CI provider is configured in this repository yet;
the script is the reproducible check to run locally or to call from CI later.

## .NET test projects

`dotnet test` uses Microsoft.Testing.Platform (selected in `global.json`):

```powershell
dotnet test --solution Audorix.ChatX.slnx
```

| Project | What it proves |
| --- | --- |
| `Audorix.ChatX.UnitTests` | Domain invariants of the installation record; database and localization configuration validation, including that failure messages never echo credentials. |
| `Audorix.ChatX.ArchitectureTests` | The documented project graph, package restrictions, compiled-assembly references and that Contracts expose no domain/persistence types. |
| `Audorix.ChatX.IntegrationTests` | Real SQLite and PostgreSQL: migrations, write/read after reopening, idempotent re-migration, single-row constraint, provider-specific migration discovery, no model/snapshot drift; API health (including database outage), problem responses, OpenAPI exposure; the real executable's `migrate` command and invalid-configuration exit code. |

EF Core's in-memory provider is deliberately not used.

## PostgreSQL integration tests

They run only when `CHATX_TEST_POSTGRES` holds a connection string for a login that may
`CREATE DATABASE`. Each test creates a database named `chatx_test_<guid>` and drops it
afterwards (`DROP DATABASE ... WITH (FORCE)`); no other database is touched. Without the
variable these tests are reported as **skipped**, never as passed.

A dedicated test login is recommended, for example (run as a PostgreSQL superuser):

```sql
CREATE ROLE chatx_test LOGIN CREATEDB PASSWORD '<choose a password>';
```

```powershell
$env:CHATX_TEST_POSTGRES = "Host=localhost;Port=5432;Username=chatx_test;Password=<password>;Database=postgres"
dotnet test --solution Audorix.ChatX.slnx
```

## Frontend

```powershell
cd web
npm ci
npm run typecheck
npm test
npm run build
```

Vitest covers readiness state mapping (available / not ready / unreachable / cancellation),
the status card's loading, failure and keyboard retry behaviour, language persistence and
fallback, Spanish plural rules, and that English and Spanish resources have identical keys.
