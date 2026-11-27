# Phase 35: organization-context private-chat access

## Delivered

- Organization owners and administrators can require members to share a workspace team before
  starting a private chat from that organization.
- Organization member chat links pass the organization context to the private-chat API.
- Chats started from the contact flow have no organization context and remain unaffected by this
  organization setting.
- The API rechecks access when an organization-context chat is accepted, listed or read, sent to,
  uploaded to, or used to download an attachment. Losing the shared-team relationship ends the
  live chat and wipes its in-memory messages and files.
- The setting is persisted in SQLite and PostgreSQL; existing organizations default to allowing
  organization-context private chats without a shared-team requirement.

## Verification

- Integration test proves the organization rule denies a start without a common team, allows a
  start after both people join the same team, and does not block a personal contact chat.
- The same test verifies team removal makes the organization-context chat unavailable.
- Related private-chat integration tests and frontend TypeScript checking pass.

## Scope still open in Phase 35

This is the first permission slice. Existing workspace teams act as the approved group for this
setting. The general scoped permission catalog and role/group grant editor, effective-access review,
audited permission changes, support permissions, organization-management permissions, channel
creation permissions, installation-admin permissions, and expiry controls remain to be implemented.

The next increment now publishes a stable permission catalog in Admin > Roles and through the
protected `GET /api/v1/admin/permissions/catalog` endpoint. It labels entries by scope and marks
future permissions as planned; the next implementation step is persisted scoped role/group grants
and effective-access evaluation.
