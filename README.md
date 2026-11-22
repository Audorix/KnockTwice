# Audorix.ChatX

Audorix.ChatX is an open-source communication platform built with ASP.NET Core (.NET 10) and
React. It is planned to provide general-purpose chat, business communication and embeddable
website chat on one shared messaging engine.

> **Status: Phase 1 — solution foundation.** This repository currently contains the platform
> foundation only: the API host, SQLite/PostgreSQL persistence with migrations, health checks,
> an English/Spanish web shell and Docker development profiles. Accounts, messaging, realtime,
> attachments and widgets are **not implemented yet** — see the [roadmap](docs/IMPLEMENTATION.md).

## What works today

- ASP.NET Core API (`/api/v1/`) with structured logging, RFC 9457 problem responses with stable
  error codes, localized (EN/ES) error titles, OpenAPI document and Swagger UI (`/swagger`)
  in Development.
- `GET /health/live` (process up) and `GET /health/ready` (database reachable, migrated, initialized).
- Configuration-selected database provider (`Sqlite` or `PostgreSql`) with strict startup
  validation and separate provider migrations; schema changes are applied by an explicit
  `migrate` command, never implicitly at startup.
- React + TypeScript (strict) + Vite + Bootstrap 5 foundation screen with English/Spanish,
  persisted language preference, locale-aware formatting and an API readiness indicator.
- Docker Compose development profiles for SQLite and PostgreSQL with persistent volumes.

## Quick start

### Docker (SQLite)

```powershell
docker compose -f docker/compose.sqlite.yaml up --build -d
```

Open <http://localhost:8080>. Stop with `docker compose -f docker/compose.sqlite.yaml down`
(data is kept). For PostgreSQL, copy `docker/.env.example` to `docker/.env`, set
`CHATX_POSTGRES_PASSWORD`, and use `docker/compose.postgres.yaml`. Details, including the
**data-deleting reset commands**, are in [docs/development/docker.md](docs/development/docker.md).

### Local (SQLite)

```powershell
dotnet tool restore
dotnet run --project src/Api/Audorix.ChatX.Api --launch-profile sqlite -- migrate
dotnet run --project src/Api/Audorix.ChatX.Api --launch-profile sqlite
```

In a second terminal:

```powershell
cd web
npm ci
npm run dev
```

Open <http://localhost:5173>. PostgreSQL and other options:
[docs/development/getting-started.md](docs/development/getting-started.md).

## Repository layout

```text
src/
  Api/Audorix.ChatX.Api                       composition root, HTTP pipeline, migrate command
  Core/Audorix.ChatX.Domain                   entities and invariants
  Core/Audorix.ChatX.Application              use cases and ports
  Core/Audorix.ChatX.Contracts                wire DTOs and error codes
  Infrastructure/Audorix.ChatX.Infrastructure provider-neutral EF Core model and adapters
  Infrastructure/Audorix.ChatX.Persistence.Sqlite      SQLite adapter + migrations
  Infrastructure/Audorix.ChatX.Persistence.PostgreSql  PostgreSQL adapter + migrations
tests/                                        unit, architecture and integration tests
web/apps/chat-web                             @audorix/chatx-web (React application)
docker/                                       Dockerfiles, nginx config, Compose profiles
scripts/                                      validation and migration helpers
docs/                                         ADRs, architecture, implementation plan, dev docs
```

## Documentation

- [Architecture decisions](docs/ADR.md) · [Architecture](docs/ARCHITECTURE.md) · [Implementation plan](docs/IMPLEMENTATION.md)
- [Getting started](docs/development/getting-started.md) · [Docker profiles](docs/development/docker.md)
- [Configuration reference](docs/development/configuration.md) · [Testing and validation](docs/development/testing.md)
- [Solution structure and dependency boundaries](docs/development/architecture-boundaries.md)
- [Phase 1 delivery record](docs/releases/phase-1-foundation.md)

## Validation

```powershell
./scripts/validate.ps1
```

## License

Planned license: MIT (ADR-014). The copyright holder line in [LICENSE](LICENSE) must be
confirmed by the project owner before the first public release.
