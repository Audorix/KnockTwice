# Phase 51: Install, upgrade, and recovery gate

## Completed verification

- Built `Audorix.KnockTwice.slnx` in Release configuration with zero warnings and errors.
- Confirmed EF reports no pending model changes for either the SQLite or PostgreSQL migration snapshot.
- Ran `scripts/restore-drill.ps1 -Provider Sqlite` against a disposable installation. The drill
  migrated a clean database, created accounts and a conversation, backed up the database and
  attachment, restored both into a new location, and verified sign-in, contacts, message history,
  attachment reference, attachment download, matching SHA-256, and sending a new message. All seven
  checks passed.
- Improved restore-drill timeout diagnostics to report the last readiness response.

## Release environment checks still required

- Upgrade a database created by the previous published release, then verify `/health/ready` and the
  existing account, organization, conversation, and attachment data.
- Exercise the documented failed-upgrade recovery procedure from a pre-upgrade backup.
- Run the same disposable restore drill with PostgreSQL and a dedicated test account/database.
- Complete the rich-text paste, send, edit, and reopen acceptance check.
- Test Azure Blob connection and attachment read/write/delete against a real Azure test container.

These environment-dependent checks are not represented as complete. Release 1 remains open until
the rich-text acceptance and Azure connection gates are closed and the upgrade/recovery path is
validated in the release environment.

## Operational references

- [Database migrations](../database-migrations.md)
- [Backup and restore](../backup-restore.md)
- [Phase 19 rich text](phase-19-rich-text.md)
- [Phase 45 Azure Blob storage](phase-45-azure-blob-storage.md)
