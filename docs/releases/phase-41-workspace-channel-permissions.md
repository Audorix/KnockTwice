# Phase 41: Workspace channel permissions

## Goal

Add workspace-scoped group roles for channel creation and administration without assigning custom
roles directly to users.

## Shipped behavior

- Workspace access-control APIs list, create, update, and delete roles and groups; assign roles to
  groups; and manage active workspace group members.
- The organization workspace panel has a dedicated Channel permissions section, separate from
  organization-level roles and groups.
- `channels.create` controls creation once configured for a workspace. If it is not configured,
  the existing organization channel-creation policy remains in effect.
- `channels.manage` controls channel administration once configured. It replaces the existing
  participant, workspace-administrator, and organization-administrator paths for that workspace.
- An effective Deny overrides any Allow and any legacy channel-manager role. A workspace decision
  does not carry to another workspace.
- Only active members of the selected workspace can be added to that workspace's groups.

## Verification

The integration test `Workspace_channel_permissions_override_legacy_policy_and_channel_manager_roles`
checks the configured create grant, group-based management grant, Deny override, and workspace
boundary. TypeScript strict type checking and the application build should be run before release.

## Follow-up

- Installation administration permission granularity.
- Effective-access review.
- Guest and invitation expiry.
