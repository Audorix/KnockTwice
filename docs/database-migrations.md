# Database migrations

Knock Twice supports **SQLite** and **PostgreSQL** through Entity Framework Core. Each provider
has its own migration set, in `src/Infrastructure/Audorix.KnockTwice.Persistence.Sqlite/Migrations` and
`src/Infrastructure/Audorix.KnockTwice.Persistence.PostgreSql/Migrations`. Both are generated from the
same model and always change together.

## For operators

**When migrations run**

| Situation | What happens |
| --- | --- |
| First install through the setup wizard | *Install* migrates the new database. |
| Development (`ASPNETCORE_ENVIRONMENT=Development`) | Pending migrations are applied at startup (`Database:MigrateOnStartup` in `appsettings.Development.json`). |
| Anywhere else | Nothing automatic. The API starts, logs the pending migrations and reports `/health/ready` as not ready until you migrate. |

**Applying migrations** (idempotent; safe to run again):

```sh
dotnet Audorix.ChatX.Api.dll migrate                     # published binaries, from the publish folder
dotnet run --project src/Api/Audorix.KnockTwice.Api -- migrate  # from source
docker compose -f docker/compose.production.yaml --env-file docker/.env.production run --rm migrate
```

The command uses the same configuration as the API (settings file, environment variables), so it
targets the configured database.

**Recommended upgrade order**

1. Back up the database (and `App_Data`/the data volume).
2. Stop the API.
3. Deploy the new version.
4. Run `migrate`.
5. Start the API and check `/health/ready`.

Migrations in a release are additive where possible. When one is not (dropping or reshaping data),
`CHANGELOG.md` says so.

**SQL scripts for review.** If a DBA applies schema changes, generate an idempotent script from a
checkout of the release:

```sh
dotnet tool restore
dotnet ef migrations script --idempotent \
  --project src/Infrastructure/Audorix.KnockTwice.Persistence.PostgreSql \
  --startup-project src/Infrastructure/Audorix.KnockTwice.Persistence.PostgreSql \
  --output chatx-postgresql.sql
```

Use `Audorix.KnockTwice.Persistence.Sqlite` for the SQLite equivalent.

**Backups**

- PostgreSQL: `pg_dump --format=custom`; restore with `pg_restore --clean`.
- SQLite: stop the API (or use `sqlite3 chatx.db ".backup chatx-backup.db"`), then copy the file
  together with its `-wal`/`-shm` files.

Downgrades are not supported: restore the backup taken before the upgrade instead.

## For developers

Change the model (entities in `src/Core/Audorix.KnockTwice.Domain`, mappings in
`src/Infrastructure/Audorix.KnockTwice.Infrastructure/Persistence/Configurations`), then generate the
migration for **both** providers at once (no database needed):

```powershell
./scripts/add-migration.ps1 -Name AddSomething
```

```sh
./scripts/add-migration.sh AddSomething
```

Review both generated files.

- Defaults and data fixes need explicit SQL that works on both providers (see `AddBusiness` for an
  `UPDATE` example).
- Enum columns are stored as strings, and check constraints use the stored names.
- Do not edit migrations that have shipped. Add a new one.

Then run the integration tests against both providers (see [development/testing.md](development/testing.md)).
