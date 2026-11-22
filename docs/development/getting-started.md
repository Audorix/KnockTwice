# Getting started (local, without Docker)

## Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | 10.0.404 or a later 10.0 feature band | Pinned in `global.json`. |
| Node.js | 22.12+ (24 LTS used during development) | npm ships with Node. |
| PostgreSQL | 15+ (optional) | Only for the PostgreSQL profile. 18 was used for validation. |

Ports used: API `http://localhost:5080`, web dev server `http://localhost:5173`.

Commands below are run from the repository root unless stated otherwise. PowerShell syntax is
shown; on bash replace `$env:NAME = "value";` with `export NAME="value"`.

## 1. Restore tools and build

```powershell
dotnet tool restore
dotnet build Audorix.ChatX.slnx
```

## 2a. SQLite profile

`appsettings.Development.json` already selects SQLite with the file
`src/Api/Audorix.ChatX.Api/App_Data/chatx-dev.db` (git-ignored).

Create or update the schema (explicit step; the API never migrates on startup):

```powershell
dotnet run --project src/Api/Audorix.ChatX.Api --launch-profile sqlite -- migrate
```

Start the API:

```powershell
dotnet run --project src/Api/Audorix.ChatX.Api --launch-profile sqlite
```

## 2b. PostgreSQL profile

Create an empty database and a login for it first (any administration tool). Then supply the
connection string through user secrets so it never lands in a file in the repository:

```powershell
dotnet user-secrets --project src/Api/Audorix.ChatX.Api set "Database:ConnectionString" "Host=localhost;Port=5432;Database=chatx_dev;Username=chatx;Password=<your password>"
```

The `postgresql` launch profile sets `Database__Provider=PostgreSql`:

```powershell
dotnet run --project src/Api/Audorix.ChatX.Api --launch-profile postgresql -- migrate
dotnet run --project src/Api/Audorix.ChatX.Api --launch-profile postgresql
```

Running `migrate` again is safe: it applies only pending migrations and never creates a second
installation record.

## 3. Web application

```powershell
cd web
npm ci
npm run dev
```

Open <http://localhost:5173>. Vite forwards `/api`, `/health` and `/openapi` to the API (set
`CHATX_DEV_API_URL` to use another API address). The status card shows **API ready** when the
API is running and migrated, **API not ready** when it runs but the database is unavailable or
not migrated, and **API unavailable** when it cannot be reached.

### VS Code

Open `chatx-web.code-workspace` (File › Open Workspace from File…). It contains the
`chat-web` app and the npm workspace root, tasks for dev/typecheck/test/build, Edge/Chrome
debug launch configurations (they start the Vite dev server first) and recommended extensions,
including TypeScript Native Preview, because the project compiles with TypeScript 7.

## Useful URLs

| URL | Purpose |
| --- | --- |
| `http://localhost:5080/health/live` | Liveness: 200 while the process serves requests (no dependency checks). |
| `http://localhost:5080/health/ready` | Readiness: 200 when the database is reachable, migrated and initialized; 503 otherwise. |
| `http://localhost:5080/openapi/v1.json` | OpenAPI document (Development only, see `OpenApi:Enabled`). |
| `http://localhost:5080/swagger` | Swagger UI for that document (Development only; also via `http://localhost:5173/swagger`). |
| `http://localhost:5080/api/v1/meta` | Public API description (no build or infrastructure details). |

## Stopping and resetting

Stop either process with `Ctrl+C`; data is kept.

**Destructive — deletes the local SQLite development database:**

```powershell
Remove-Item src/Api/Audorix.ChatX.Api/App_Data/chatx-dev.db*
```

**Destructive — deletes a PostgreSQL development database** (only run against a database you
created for development):

```powershell
psql -h localhost -U postgres -c "DROP DATABASE chatx_dev WITH (FORCE);" -c "CREATE DATABASE chatx_dev OWNER chatx;"
```

## Migrations

Generate a new migration for **both** providers (no database required):

```powershell
./scripts/add-migration.ps1 -Name <MigrationName>
```

For controlled production rollouts, either run the image's `migrate` command as a one-off job
before deploying the new API version, or hand a DBA an idempotent SQL script:

```powershell
dotnet ef migrations script --idempotent --project src/Infrastructure/Audorix.ChatX.Persistence.PostgreSql --startup-project src/Infrastructure/Audorix.ChatX.Persistence.PostgreSql --output chatx-postgresql.sql
```

Next: [Docker profiles](docker.md) · [Configuration](configuration.md) · [Testing](testing.md).
