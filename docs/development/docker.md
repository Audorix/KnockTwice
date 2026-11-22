# Docker development profiles

Two self-contained Compose files, each a separate Compose project with its own named volume:

| Profile | File | Project name | Database | Persistent volume |
| --- | --- | --- | --- | --- |
| SQLite | `docker/compose.sqlite.yaml` | `chatx-sqlite` | SQLite file `/app/data/chatx.db` | `chatx-sqlite_sqlite-data` |
| PostgreSQL | `docker/compose.postgres.yaml` | `chatx-postgres` | PostgreSQL 18 | `chatx-postgres_postgres-data` |

Services in each profile:

- `migrate` — the API image running `migrate` once; exits when done.
- `api` — starts only after `migrate` succeeded. Published on `http://localhost:5080`.
- `web` — nginx serving the React build on `http://localhost:8080` and proxying `/api`,
  `/health`, `/openapi` and `/swagger` to `api` (same origin, so no CORS). Swagger UI:
  `http://localhost:8080/swagger` while `CHATX_ENVIRONMENT=Development`.
- `postgres` (PostgreSQL profile only) — not published to the host by default.

Requirements: Docker Engine with Compose v2. No registry account or cloud service is needed
beyond pulling public base images (`mcr.microsoft.com/dotnet/*`, `node`, `nginxinc/nginx-unprivileged`, `postgres`).

Run every command from the repository root. Run only one profile at a time, or change
`CHATX_WEB_PORT`/`CHATX_API_PORT` in `docker/.env`, because both default to the same host ports.

## Environment file

```powershell
Copy-Item docker/.env.example docker/.env
```

Edit `docker/.env` (git-ignored). `CHATX_POSTGRES_PASSWORD` is required by the PostgreSQL
profile; pick any local value without `;` characters (it is embedded in a connection string).

## SQLite profile

```powershell
docker compose -f docker/compose.sqlite.yaml up --build -d
```

```powershell
docker compose -f docker/compose.sqlite.yaml ps
```

Open <http://localhost:8080>. Re-apply migrations explicitly at any time (idempotent):

```powershell
docker compose -f docker/compose.sqlite.yaml run --rm migrate
```

## PostgreSQL profile

```powershell
docker compose -f docker/compose.postgres.yaml up --build -d
```

```powershell
docker compose -f docker/compose.postgres.yaml run --rm migrate
```

## Logs

```powershell
docker compose -f docker/compose.sqlite.yaml logs -f api
```

## Normal shutdown (keeps data)

`down` removes containers and the network but **keeps named volumes**, so the database
survives and the next `up` reuses it:

```powershell
docker compose -f docker/compose.sqlite.yaml down
```

```powershell
docker compose -f docker/compose.postgres.yaml down
```

## Reset (DESTRUCTIVE — deletes development data)

`-v` deletes the profile's named volume and every record in it. Only the volume of the named
Compose project is affected.

```powershell
docker compose -f docker/compose.sqlite.yaml down -v
```

```powershell
docker compose -f docker/compose.postgres.yaml down -v
```

## Notes

- The API containers run as the non-root `app` user. `ASPNETCORE_ENVIRONMENT` defaults to
  `Development` here so `/openapi/v1.json` is available; set `CHATX_ENVIRONMENT=Production` to
  check production behaviour.
- PostgreSQL is the production-oriented path; SQLite is the lightweight single-instance path.
  These files are development profiles: a production deployment should terminate TLS at a
  reverse proxy, supply secrets from a secret store, and run `migrate` as a controlled step.
- No Redis, realtime backplane, background worker or object storage is included; those arrive
  with the phases that need them.
