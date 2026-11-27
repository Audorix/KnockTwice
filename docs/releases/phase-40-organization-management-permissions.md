# Phase 40: Organization management permissions

## Outcome

Organizations can delegate discovery and management through roles assigned to groups. Users receive
those roles through group membership. Each permission retains the existing owner/admin/member policy
until that key is configured for the organization. Once configured, an explicit Allow is required;
Deny overrides Allow from every effective role.

## Implemented permissions

- `organization.discover`: organization listing and access to organization-scoped operations.
- `organization.manage`: settings, branding, verified domains, automatic enrollment, SSO, and
  organization deletion.
- `organization.members.manage`: invitations, member removal, guest management, and support-agent
  designation.
- `organization.roles.manage`: organization role changes and organization access-role/group
  administration.
- `organization.workspaces.manage`: workspace creation and organization-level workspace
  administration. Existing workspace administrators keep their workspace-local actions.

The organization response includes effective management permissions so the web UI can show controls
to delegated managers. No schema migration was required.

## Verification

The integration scenario checks the legacy policy before configuration, a group-based grant to
manage organization settings, and Deny overriding Allow. The organization page test verifies role
creation sends organization permission decisions, and the web build checks the form types.
