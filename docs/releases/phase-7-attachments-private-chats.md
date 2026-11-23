# Phase 7 — Attachments and private chats: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-22.
Applicable ADRs: 005, 007 (storage abstraction), 012, 021, 023, 024 (upload half implemented; the
ADR stays Proposed for its widget half), **025 (private chats, accepted by the owner for this
phase)**.

## Gate decisions (Implementation §12.2), owner answers of 2026-11-22

| Gate | Decision |
| --- | --- |
| Quotas and limits | 25 MB per file, 10 attachments per message, 1 GB per person (`Attachments:*`, all configurable; quota 0 = unlimited). |
| Allowed types | Allow-list: JPEG, PNG, GIF, WebP, PDF, DOCX, XLSX, PPTX, TXT, CSV, ZIP. The content signature must match the extension, the declared type must match or be generic; active content (HTML, SVG, scripts, executables) is never accepted. |
| Scan policy | Pluggable scanner with a ClamAV (clamd) adapter, off by default. When on, files stay quarantined until a clean verdict, also while the scanner is unavailable. |
| Cleanup lifecycle | Uploads never sent are removed after 24 hours; deleting a message deletes its files; people who leave lose access immediately. |
| Private chats (new request) | ADR-025: one-to-one, the invitee must accept; lives as long as the initiator's sign-in session; messages and files only in server memory, erased at the end; a distinct UI that states the rules. |

## Delivered behaviour

### Attachments

- Storage abstraction (`IAttachmentStorage`) with **Local** (files below `Storage:Local:Path`, keys
  validated so nothing escapes the root, atomic writes) and **S3-compatible** providers (Signature
  V4 over HTTPS, path-style or virtual-hosted, optional prefix, private objects, no SDK).
- Upload API: `POST /api/v1/conversations/{id}/attachments` (multipart, field `file`), streamed to a
  temporary file with the size limit enforced while reading; checks: participation (and no block in
  direct chats), allowed extension, declared type, content signature (Office packages must contain
  their main part; text must be UTF-8 without binary control characters), quota.
- Lifecycle (Architecture §17.5): the record is committed as `PendingUpload` before the bytes are
  written under a random key, then `Quarantined` → `Ready` (or `Rejected`) → `Deleted`; content
  removal is tracked and retried; storage failures never leave an available attachment (503
  `attachment_storage_unavailable`).
- Messages: `attachmentIds` on send (text optional with files); only the sender's ready, unsent
  uploads of the same conversation (also enforced by a composite foreign key); idempotent retries
  compare the files too. History and `MessageCreated` events carry the attachments.
- Download API: `GET /api/v1/attachments/{id}/content` authorized on every request through the
  conversation (participation, visible history, message not deleted); `Content-Disposition`
  (raster images inline, everything else as attachment), sandboxing CSP, `nosniff`,
  `Cache-Control: private, no-cache` with an ETag (304 only after authorization). Also
  `GET /attachments/{id}` (metadata), `DELETE /attachments/{id}` (unsent uploads),
  `GET /attachments/limits`.
- Cleanup job (`AttachmentCleanupService`): abandoned and unfinished uploads, content removal
  retries, rescans of quarantined files.
- Web app: attach button (file picker filtered by the allowed types), client-side size/type/count
  checks, upload chips with progress state and removal, image previews and download cards in
  messages, EN/ES.

### Private chats

- API under `/api/v1/private-chats`: start (invitation), list, get, accept, end/decline
  (`DELETE`), messages (idempotent by client id), in-memory files (10 MB each, 50 MB per chat) and
  their download (`no-store`).
- Lifetime: ends when the initiator ends it, signs out, or the initiator's session is revoked or
  expires; when an invitation expires (10 minutes) or is declined; when one blocks the other; on
  shutdown. Ending erases texts and zeroes file buffers and notifies both (`PrivateChatChanged`
  with the reason). The initiator sees the chat only from the session that started it.
- Realtime: `PrivateChatChanged` and `PrivateMessageCreated` go only to the initiator's session
  connections and the invitee's connections.
- Web app: a deliberately different, dark and sealed look (violet, padlock, dashed frame); the
  rules are shown before inviting and before accepting (not saved, lifetime bound to the
  initiator's session, files erased, which devices can see it, screenshots cannot be prevented); a
  permanent banner while chatting; an invitation toast on every page; an "ended" screen with the
  reason that clears everything; nothing is written to browser storage.

## Migration impact

`AddAttachments` (both providers): new table `attachments` (metadata, status, generated storage key
unique, SHA-256, `created_at_ticks` for cleanup queries, `content_removed`), foreign keys to the
conversation, owner and — composite — the message of the same conversation; check constraints for
size and "linked implies ready or deleted". No existing table changes. Development installations
apply it at startup (`Database:MigrateOnStartup`); others run `migrate`.

Private chats need no schema.

## Validation results

| Check | Result | Evidence |
| --- | --- | --- |
| .NET build (Release, warnings as errors) | **Passed** | 0 warnings, 0 errors |
| .NET tests incl. real PostgreSQL 18 | **Passed** — 301 of 301 (7 min 46 s) | `scripts/validate.ps1` with `CHATX_TEST_POSTGRES` |
| Acceptance: both storage providers work | **Passed** | Local: upload, download, delete, path-traversal refusal. S3: upload/download/delete through an in-process S3 endpoint that recomputes Signature V4; signer matches the published AWS example; S3 outage → 503 and the record ends `Deleted`. |
| Acceptance: file metadata persists | **Passed** | Owner, conversation, message link, generated key, SHA-256 and status read back from the database (both providers). |
| Acceptance: unauthorized users cannot access attachments | **Passed** | Outsiders, anonymous callers, people who left a group, others' unsent uploads and files of deleted messages → 404/401; conditional requests authorize before 304. |
| Acceptance: invalid file types are rejected | **Passed** | Executables, SVG, HTML, missing extensions, signature mismatches (PDF named .png, ZIP named .docx, binary .txt), declared-type mismatch, empty files. |
| Acceptance: large uploads follow the limits | **Passed** | 413 `attachment_too_large` (streamed check) and `attachment_quota_exceeded`; per-message count; deleting an upload frees quota. |
| Quarantine, cleanup | **Passed** | Unavailable scanner keeps files quarantined (not sendable or downloadable) until a clean rescan; infected files rejected and removed; abandoned uploads removed after 24 h; idempotent second run. |
| Private chats | **Passed** | Invitation/acceptance, session-bound visibility, live messages and in-memory files, ends on sign-out, session revocation, decline, cancel and block; nothing in the database, storage or files afterwards. |
| Frontend | **Passed** — 37 tests, type-check, production build | Upload chips and file previews; client-side type refusal; invitee rules, acceptance, chat ending clears the screen; initiator invitation flow. |
| Browser, end to end (scratch instance, SQLite, local storage) | **Passed** | Received PDF card and image preview; uploaded and sent a file; private chat started from a direct chat, accepted by the other person, live messages and image, no trace in storage or the database files (raw scan); ended screen appeared live when the initiator's session was revoked; the invitation toast appeared on another page. |
| Docker profiles | **Not run** | Docker is not installed. |

Found and fixed during validation: initiators also receive their own "invited" event (test
expectation), the invitation toast overflowed its column, and the private chat's memory figure was
not refreshed after files arrived. The validation script's `npm ci` could not replace the Rolldown
binary while a separately started Vite dev server held it open; dependencies were restored with
`npm install` (lockfile unchanged) and the frontend steps were rerun.

## Open items

- S3 compatibility is verified against an in-process S3 endpoint that checks Signature V4 and the
  published AWS signing example, not against AWS, MinIO or R2 themselves (no network/containers in
  this environment).
- ClamAV is verified with its protocol parser and an unreachable daemon, not a running clamd.
- No resumable or direct-to-storage uploads; no thumbnails (images are shown scaled); no per-file
  upload progress bar (uploads show a state).
- Quota checks are not serialized: concurrent uploads by one person can exceed the quota slightly.
- Private chats: no typing indicator, edits or deletes; single API instance only; the server sees
  plaintext in memory (not end-to-end encrypted, as the UI states).
- Docker profiles not run (Docker is not installed).

## Next slice

Phase 8 — React chat application (layout, lists, history, composer polish, previews,
responsiveness, accessibility).
