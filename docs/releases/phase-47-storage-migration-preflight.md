# Phase 47: Storage migration preflight

The admin provider page includes a migration preflight for Local, S3-compatible, and Azure Blob
storage. Choose a source and destination, then run the dry run. It does not copy attachments or
change the active storage provider. It writes, reads, and deletes a temporary probe object at the
destination to verify access.

The API inventories ready and quarantined attachments that still have stored content and match the
selected source. Legacy attachments with no recorded provider are treated as belonging to the
current provider. The report includes the total file count and bytes, up to 2,000 file rows, and
blockers for missing or unreadable source objects. It checks destination write, read, and delete
access with a short-lived random probe object.

Storage providers do not currently expose available capacity or quota through the shared interface,
so the report marks capacity as unknown. It does not start a copy job, pause uploads, switch
attachment references, or clean up source objects. The endpoint is
`POST /api/v1/admin/storage-migrations/dry-run` and requires `providers.manage`.

Phase 48 adds a persistent background copy job with progress, pause/resume, safe retries, and per-file
verification. Verified cutover, rollback, and source cleanup remain separate stages.
