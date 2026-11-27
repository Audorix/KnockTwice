# Phase 50: Verified storage cutover and rollback

Phase 48 copies attachment content while each attachment continues to reference its source. Phase
50 adds an explicit cutover and a rollback procedure.

## Cutover

1. Run the storage preflight and start a copy job as in Phase 48.
2. Wait for the job to reach `Completed`. Failed or skipped files prevent cutover.
3. In the job card, select **Switch to destination**. The API compares every current ready or
   quarantined attachment with the copied manifest and reads the destination object again to verify
   its size and SHA-256. New, pending, changed, missing, or unreadable content blocks the switch.
4. When verification passes, the API closes the upload gate, updates attachment provider references,
   saves the new default provider, and marks the job `AwaitingRestart`.
5. Restart the application. Its worker confirms the destination is active and changes the job to
   `CutoverComplete`; uploads then resume. The previous provider configuration and bytes remain
   available during the rollback window.

The upload and attachment-forwarding paths check the cutover gate before creating new attachment
records. The inventory check also blocks on unfinished uploads. Existing conversations and
attachment IDs remain unchanged.

## Rollback

1. From a `CutoverComplete` job, select **Copy files back for rollback**. This creates a normal
   verified copy job from the active destination back to the original source, including attachments
   uploaded since cutover.
2. Wait for that reverse job to reach `Completed`. Select **Roll back to source** on the original
   job.
3. The API verifies that the reverse job covers the current destination inventory and verifies the
   restored source bytes by size and SHA-256. If new or changed files are missing from the reverse
   job, the rollback is blocked; start another reverse copy and retry.
4. The API closes uploads, restores the attachment references and default provider, and marks the
   original job `RollbackAwaitingRestart`. Restart the application; the worker confirms the source
   provider is active, marks the job `RolledBack`, and resumes uploads.

The old provider's objects are not automatically deleted. Source cleanup remains a separate
administrator action after the rollback window.

If the process exits during the provider-setting change, the background worker reconciles the
durable transition state with the configured provider on restart and either completes or releases
the cutover gate.

## API

- `POST /api/v1/admin/storage-migrations/{jobId}/cutover`
- `POST /api/v1/admin/storage-migrations/{jobId}/rollback` with `{ "reverseCopyJobId": "..." }`

Both endpoints require `providers.manage`. The copy and cutover records are durable in the existing
SQLite and PostgreSQL migration-job tables; no schema change is needed for this stage.
Cutover and rollback each add a separate audit entry containing the job IDs, provider direction,
and number of attachment references changed.
