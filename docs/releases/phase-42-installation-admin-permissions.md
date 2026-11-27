# Phase 42: Installation administration permissions

Installation administration now separates access to roles, permission grants, groups, providers, and settings:

- `roles.read` and `roles.manage` govern installation role administration.
- `permissions.read` and `permissions.manage` govern the permission catalog, effective-access review, and role grants.
- `groups.read` and `groups.manage` govern installation groups and membership.
- `providers.read` and `providers.manage` govern provider configuration and connection tests.
- `settings.read` and `settings.manage` govern live installation settings.

The admin tabs and editing controls use the same permissions enforced by the API. Role creation and permission changes require both role-management and permission-management access. Assigning roles to groups requires both role-management and group-management access.

For compatibility, each granular key falls back to the corresponding existing installation permission until that granular key is configured on a role assigned to an installation group. Once configured, the granular group-based decision controls access. Explicit Deny takes precedence over Allow.

No database migration is required; this phase uses the existing role, group, and permission tables.
