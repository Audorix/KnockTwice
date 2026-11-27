# Phase 46: Installation effective-access review

The administration user details drawer now has a **Review effective access** view. Administrators
can choose the installation, an organization where the user is active, or one of that user's active
workspaces. Organization reviews include installation inheritance; workspace reviews include both
installation and organization inheritance. Each context lists the user's groups and their assigned
roles and shows effective permission outcomes and decision sources.

For custom assignments, the review names the group and role decisions that apply. A Deny from any
of the user's assigned group roles wins over Allows. When no custom decision applies, the review
shows the legacy installation-role policy that supplies the result or indicates that no grant
exists. Existing organization and workspace policy behavior is identified as the source when no
granular permission key has been configured. The endpoints are
`GET /api/v1/admin/users/{userId}/access-review-contexts` and
`GET /api/v1/admin/users/{userId}/access-review?scope=...`; both require `permissions.read`.
