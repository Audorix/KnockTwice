# Phase 38: Organization-scoped roles for private chats

## Goal

Start applying the group-based permission model inside an organization while keeping personal
contact chats independent. The first scoped capability is `private_chats.use`.

## Delivered

- Roles and groups carry an installation, organization, or workspace scope and optional scope IDs.
- Organization owners can create organization roles/groups, assign roles to groups, and manage
  active organization members in groups from the organization's Access tab.
- A role may assign the organization-scope `private_chats.use` permission as Allow or Deny.
- Until a decision is attached to a group, the existing organization private-chat policy remains in
  effect. Once configured, both participants need an effective Allow; Deny overrides all Allows.
- The policy is checked when starting a chat and on the existing revalidation path for live chats.
  Personal contact chats omit `organizationId` and are not affected.
- Existing shared-team policy remains an additional restriction when enabled.
- SQLite and PostgreSQL migrations add scope and resource IDs to access roles and groups, with
  organization/workspace foreign keys and scope-aware indexes.

## Verification

- API build passed with 0 warnings and 0 errors.
- Unit test suite passed: 276 tests.
- Focused integration test passed against a fresh migrated SQLite database. It checks the legacy
  default, scoped Allow, Deny overriding Allow, and personal chat independence.
- Frontend typecheck passed; the full frontend suite passed: 124 tests across 30 files.
- The Phase 38 migration was applied successfully to the configured development SQLite database.
- PostgreSQL migration was generated and provider project built; it has not been applied to a live
  PostgreSQL database in this stage.

## Next

Add scoped role/group management for support, organization membership and settings, and workspace
channel creation. Then add effective-access review and verify cross-organization isolation for every
scope.
