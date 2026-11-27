# Phase 37: Group-based Allow/Deny access

## Goal

Make group membership the only path for assigning granular custom installation roles, and let each
role explicitly allow or deny each implemented permission.

## Delivered

- A user receives custom installation roles through group membership. The custom role API and admin
  UI do not support direct role-to-user assignment.
- Each role/permission pair stores `Allow` or `Deny`; an absent pair is unassigned.
- A `Deny` from any applicable custom role blocks the permission even when another role, including
  a built-in role, allows it.
- `/api/v1/users/me` reports effective allowed permissions. The administrator effective-access
  endpoint also reports denied permissions.
- The new SQLite and PostgreSQL migrations add the permission effect and remove direct custom role
  assignments. Existing direct assignments are preserved by moving them into generated per-user
  groups that receive the corresponding roles.
- The admin Roles screen uses a three-state selector for every implemented installation permission
  and shows the group-based assignment path.

## Boundary

This slice applies to granular custom installation roles. Existing `InstallationAdministrator` and
`Auditor` Identity roles remain the compatibility and bootstrap mechanism. Organization- and
workspace-scoped roles and permissions remain planned work; participant and resource checks remain
authoritative for conversation access.

## Verification

- 276 unit tests passed.
- The focused SQLite integration test passed for group-derived access, Deny precedence, and removal
  of the direct custom-role assignment route.
- 9 admin UI tests passed; frontend typecheck and production build passed.
- The development SQLite database applied migration
  `20261126133648_Phase37GroupBasedAllowDenyAccess`; readback confirmed the effect column, valid
  decisions, and removal of the legacy direct custom-role table.
- PostgreSQL migration is authored but has not been applied to a PostgreSQL database in this run.
