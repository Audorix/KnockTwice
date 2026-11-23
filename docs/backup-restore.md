# Backup and restore

An installation keeps its state in three places. Back them up **together**, from the same moment:

| What | Where (defaults) | Contains |
| --- | --- | --- |
| Database | SQLite file `App_Data/chatx.db`, or the PostgreSQL database | Accounts, organizations, conversations, messages, settings changed in the administration, audit log, **Data Protection keys** (they protect sign-in cookies and widget signing secrets) |
| Attachment files | `App_Data/attachments` (`Storage:Local:Path`), or your S3 bucket | File contents; the database only holds their records |
| Settings file | `App_Data/chatx.settings.json` (`Setup:SettingsPath`) | What the setup wizard and **Admin → Providers** saved, including database and email passwords. Keep this backup private |

Files referenced by messages that are missing after a restore stay listed but cannot be
downloaded; records for files that are not in the database are ignored. Restoring both from the
same backup keeps them consistent.

The **search index** needs no backup of its own. With the default database provider it lives in
the database; with OpenSearch, rebuild it after a restore (**Admin → Providers → Search index →
Rebuild index**).

## SQLite (without Docker)

SQLite runs in WAL mode, so copy the files while the API is stopped (or use SQLite's own backup).

```sh
systemctl stop chatx
tar czf chatx-$(date +%F).tgz -C /opt/chatx/api App_Data   # chatx.db*, attachments, settings file
systemctl start chatx
```

Without stopping the API, take a consistent copy of the database with the `sqlite3` tool and copy
the rest of `App_Data` next to it:

```sh
sqlite3 /opt/chatx/api/App_Data/chatx.db ".backup '/backup/chatx.db'"
```

**Restore:** stop the API, put `App_Data` back (database file, `attachments`, settings file), run
`dotnet Audorix.ChatX.Api.dll migrate` (needed when the backup is older than the installed
version; safe otherwise) and start the API.

## PostgreSQL (without Docker)

```sh
pg_dump -h db.example.org -U chatx -d chatx --format=custom -f chatx-$(date +%F).dump
tar czf chatx-files-$(date +%F).tgz -C /opt/chatx/api App_Data
```

`pg_dump` gives a consistent snapshot while the API keeps running; files uploaded during the dump
may be in the archive without a record, which is harmless.

**Restore** into an empty database:

```sh
createdb -h db.example.org -U postgres -O chatx chatx
pg_restore -h db.example.org -U chatx -d chatx --no-owner chatx-2026-11-23.dump
tar xzf chatx-files-2026-11-23.tgz -C /opt/chatx/api
dotnet Audorix.ChatX.Api.dll migrate
```

To restore over an existing database instead, add `--clean --if-exists` to `pg_restore`.

## Docker

See [deployment-docker.md](deployment-docker.md#operating-it): `pg_dump` inside the
`postgres` container plus an archive of the `api-data` volume.

## S3-compatible storage

With `Storage:Provider=S3` the files live in the bucket. Use the provider's versioning or
replication, and restore the bucket to the same point in time as the database.

## Deleted data and backups

Deleting an account, an organization or old messages (retention) removes them from the live
installation only. Older backups still contain them until they expire, so keep backups only as
long as you need them and include their lifetime in your privacy notice.

## Practising a restore

`scripts/restore-drill.ps1` runs the procedure above on throwaway data: it creates accounts, a
conversation, a message and an attachment, takes a backup, restores it into a new location and
checks that people sign in, the history and contacts are there and the attachment downloads
unchanged (SHA-256).

```powershell
dotnet build Audorix.ChatX.slnx -c Release
./scripts/restore-drill.ps1 -Provider Sqlite
```

```powershell
$env:CHATX_TEST_POSTGRES = "Host=localhost;Port=5432;Username=postgres;Password=...;Database=postgres"
./scripts/restore-drill.ps1 -Provider PostgreSql -PgBin "C:\Program Files\PostgreSQL\18\bin"
```

It never touches a real installation: everything is created in a temporary folder (and, for
PostgreSQL, in two new databases that are dropped at the end).
