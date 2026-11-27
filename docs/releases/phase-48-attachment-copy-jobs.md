# Phase 48: Persistent attachment copy jobs

The admin provider page can start a durable copy job after the Phase 47 preflight reports no
blockers. Jobs can copy between Local, S3-compatible storage (including Cloudflare R2), and Azure
Blob Storage. The same opaque storage key is used at the destination, so a retry safely overwrites
an incomplete or previous copy.

The worker copies one attachment at a time. It checks that the source metadata still matches the
snapshot, streams the bytes to the destination, verifies size and SHA-256 on both the source stream
and destination readback, then records the result. Transient failures retry with capped exponential
backoff up to five attempts. Administrators can view recent job progress and pause or resume active
jobs. Job and per-file state are stored in the database, and worker leases allow recovery after an
API process exits.

The copy stage does not modify attachment provider references, delete source bytes, or change the
active storage provider. Attachments continue to be served from their recorded source. A later
stage must reconcile changes and perform verified cutover; rollback and source cleanup remain
separate audited operations.

Endpoints require `providers.manage`:

- `POST /api/v1/admin/storage-migrations` starts a job after rerunning preflight.
- `GET /api/v1/admin/storage-migrations` lists recent jobs.
- `GET /api/v1/admin/storage-migrations/{jobId}` returns job counts and status.
- `POST /api/v1/admin/storage-migrations/{jobId}/pause` pauses a queued or running job.
- `POST /api/v1/admin/storage-migrations/{jobId}/resume` resumes a paused job.

SQLite and PostgreSQL each receive a Phase 48 migration that adds the job and per-file tables.
