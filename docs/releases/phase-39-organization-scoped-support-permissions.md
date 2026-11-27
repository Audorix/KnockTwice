# Phase 39: Organization-scoped support permissions

## Outcome

Support access is controlled by organization-scoped roles assigned to groups. Users receive those
roles through group membership. Permission decisions use `Allow`, `Deny`, or unassigned, and a Deny
from any effective role overrides Allow.

## Permissions

- `support.access` controls entry to the organization's support workspace. When configured, an
  explicit Deny blocks both inbox access and case participation routes.
- `support.cases.read` controls reading the inbox, case history, and internal notes. When unassigned,
  the prior support-agent and case-participant behavior remains in effect.
- `support.cases.manage` controls claiming, assignment, joining, editing case subjects/status, and
  adding internal notes. When unassigned, the prior support-agent and participant behavior remains
  in effect.

No active decision in an organization preserves its prior support policy. Permission decisions are
resolved from the user's installation and organization groups for the organization in the route or
case. No database migration is required.

## Verification

The integration scenario checks legacy behavior before configuration, support inbox and case access
through a group role, and explicit Deny overriding an Allow from another group role. The frontend
build verifies the organization access form exposes all three support decisions.
