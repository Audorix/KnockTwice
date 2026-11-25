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
dotnet build Audorix.KnockTwice.slnx
```

## 2. Start the API

```powershell
dotnet run --project src/Api/Audorix.KnockTwice.Api --launch-profile chatx
```

On the first start there is no completed setup, so the API runs in **setup mode** and prints a
setup code in its console, in the log and as a banner:

```text
================================================================
  Knock Twice setup code:  K7QM-2XRA-9PDN
  Open http://localhost:5173 to complete setup.
  The code is also saved to ...\src\Api\Audorix.KnockTwice.Api\App_Data\setup-code.txt
================================================================
```

The code changes at every start. It is also saved to `App_Data/setup-code.txt` (next to the
settings file), which is removed when setup completes or the API stops.

In setup mode the API serves only `/health/*` and the setup API (`/api/v1/setup/*`); every other
API path answers 503 `setup_required`. The `chatx` launch profile opens Swagger UI
(`http://localhost:5080/swagger`) in Development, in setup mode as well as in normal mode; in setup
mode it lists only the setup API.

## 3. Web application and the setup wizard

In a second terminal:

```powershell
cd web
npm ci
npm run dev
```

Open <http://localhost:5173>. While setup is required the web app shows the **setup wizard**
instead of the application:

1. **Setup code** — copy it from the API console. It proves you operate this server.
2. **Database** — SQLite (a file, default `App_Data/chatx.db`) or PostgreSQL (server, port,
   database, user, password, SSL). *Test connection* checks it; a missing PostgreSQL database is
   created during setup if the account may create databases.
3. **Email** — SMTP (defaults: `localhost:1025`, no security, for a local catcher such as Mailpit)
   or Postmark. *Send test email* tries the settings.
4. **Administrator** — name, email and password of the first installation administrator. The
   address is confirmed automatically.
5. **Options** — **development mode** (new accounts are confirmed immediately and no
   confirmation email is sent; preselected in the Development environment), the web app address,
   default language and human verification (Cloudflare Turnstile keys, or off).
6. **Review** — *Install* migrates the database, creates the administrator, writes
   `src/Api/Audorix.KnockTwice.Api/App_Data/chatx.settings.json` (git-ignored; it contains your
   database and SMTP passwords) and restarts the API in normal mode. Then sign in.

Nothing is written before *Install*. To run setup again, stop the API and delete the settings file
(and the database if you want a fresh one; see "Stopping and resetting").

Without an SMTP server, emails (confirmation outside development mode, password reset) fail with
"could not send the email" (503). Captcha uses Cloudflare's public **test keys** in Development;
they always pass but need internet access to `challenges.cloudflare.com`. Choose "Off" in the
wizard when working offline.

Further administrators are assigned from the command line after they registered:

```powershell
dotnet run --project src/Api/Audorix.KnockTwice.Api --launch-profile chatx -- grant-role you@example.org InstallationAdministrator
```

Schema updates after pulling new code are applied automatically when the API starts in
Development (`Database:MigrateOnStartup` in `appsettings.Development.json`). Elsewhere they are an
explicit step (the API logs the pending migrations and reports "not ready" until then); running it
again is safe:

```powershell
dotnet run --project src/Api/Audorix.KnockTwice.Api --launch-profile chatx -- migrate
```

### Web application details

Vite forwards `/api`, `/hubs`, `/health` and `/openapi` to the API (set
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
| `ws://localhost:5080/hubs/chat` | Realtime hub (SignalR; requires the sign-in cookie). |

## Stopping and resetting

Stop either process with `Ctrl+C`; data is kept.

**Destructive — forgets the setup and deletes the local SQLite development database** (stop the
API first; the next start shows the setup wizard again):

```powershell
Remove-Item src/Api/Audorix.KnockTwice.Api/App_Data/chatx.settings.json, src/Api/Audorix.KnockTwice.Api/App_Data/chatx.db*
```

**Destructive — deletes a PostgreSQL development database** (only run against a database you
created for development; the wizard recreates it):

```powershell
psql -h localhost -U postgres -c "DROP DATABASE chatx WITH (FORCE);"
```

## Migrations

Generate a new migration for **both** providers (no database required):

```powershell
./scripts/add-migration.ps1 -Name <MigrationName>
```

For controlled production rollouts, either run the image's `migrate` command as a one-off job
before deploying the new API version, or hand a DBA an idempotent SQL script:

```powershell
dotnet ef migrations script --idempotent --project src/Infrastructure/Audorix.KnockTwice.Persistence.PostgreSql --startup-project src/Infrastructure/Audorix.KnockTwice.Persistence.PostgreSql --output chatx-postgresql.sql
```

Next: [Docker profiles](docker.md) · [Configuration](configuration.md) · [Testing](testing.md) ·
[Installing on a server](../installation.md) · [Contributing](../../CONTRIBUTING.md).
