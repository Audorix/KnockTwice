# Phase 36: Installation access control

## Goal

Let installation administrators grant the currently implemented installation capabilities through
custom roles and groups, with effective access visible and every access change auditable.

## Delivered

- Create, rename, describe, and delete custom installation roles; role names cannot conflict with
  the built-in `InstallationAdministrator` and `Auditor` roles.
- Assign implemented installation-scope permissions to a custom role. Planned, organization-scope,
  workspace-scope, and unknown permission keys are rejected by the domain model.
- Create groups, maintain membership, and assign roles directly to users or to groups.
- Resolve direct and group-based permission grants from persisted state for each authorization
  request. Changes apply on the next request without restarting a session.
- Include the effective permission set in `/api/v1/users/me` and provide an administrator endpoint
  to inspect effective grants for a user.
- Record role, group, membership, and assignment mutations in the audit log.
- Provide an admin Roles screen for creating roles and groups, adding users to groups, and granting
  roles directly to users or to groups.
- Add SQLite and PostgreSQL migrations and tests for permission validation, effective access,
  immediate revocation, and UI visibility.

## Boundaries

- Existing built-in role grants continue to work as a compatibility baseline.
- Installation role grants do not authorize reading conversation content or bypass workspace,
  organization, participant, or resource membership checks.
- Organization- and workspace-scoped role assignment is not delivered in this phase. The existing
  organization private-chat shared-team rule remains the access boundary for that flow.
- Guest and invitation expiry, effective-access explanations by source, and role deletion/cleanup
  workflows remain follow-up work.

## Verification

- Unit tests validate custom role permission scope and group name rules.
- Integration tests grant `users.read` through a group, verify `/admin/users` access and `/users/me`,
  then remove membership and verify access is revoked on the next request.
- Admin UI tests cover rendering the custom role/group controls.
