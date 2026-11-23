# Phase 11 — Business communication: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Applicable ADRs: 008, 010, 012, 018, 021, 022, 023, 024, **026 (accepted with this phase)**.

## Gate decisions (Implementation §12.2), owner answers of 2026-11-23

| Gate | Decision |
| --- | --- |
| Channel access model | Public channels (any workspace member finds and joins them) and private channels (invitation only). History from the join, like groups. Channel managers moderate; workspace and organization administrators manage every channel. |
| Guest history | Channel-scoped guests: invited to a channel by owners/administrators, accepted by the invitee, history from their join; no browsing, direct conversations or member directory; each organization can switch guests off. |
| Workspace/team permission inheritance | Teams are member groups with Team Managers. Adding a team to a channel adds its present and future members. Administration flows organization → workspace → channel; teams grant membership only. |
| Support-agent foundations | A Support Agent role, an inbox of website conversations with open/closed status, claim and assignment to one agent. No SLAs, tags or automation. |

## Delivered behaviour

- **Channels** (`/channels`, `/channel?id=`): browse a workspace's channels, join public ones, create channels (optionally with teams), and open them in the normal chat view. Managers can rename a channel, describe it, change its visibility, add people or teams, make someone a manager, remove people, unlink teams and invite guests. Names are unique per workspace. Leaving a workspace ends its channels.
- **Teams** (organization page → workspace → Members): workspace and organization administrators create and delete teams. Team Managers add and remove members and change their roles. Team members must be workspace members.
- **Guests:** invitations by email address to one channel (*Channels* page lists your own guest invitations). Guests appear with a Guest badge and a notice in the conversation view. The organization page lists guests with their channels, plus pending guest invitations. Owners and administrators can remove a guest, which ends all of their channels.
- **Organization settings** (organization page → *Channels and guests*): allow guests, and who creates channels (members or administrators). They apply to that organization only.
- **Workspaces:** rename and change a member's role (API). Leaving a workspace also removes its channels and team memberships.
- **Support** (`/support`, shown to support agents and owners/administrators):
  - Inbox filters: open, closed, all, and assigned to me.
  - Actions: take (join with the whole history), close and reopen. The same actions appear on website conversations in the chat view.
  - Assignment through the API.
  - Support-agent toggle per member on the organization page.
  - A visitor's new message reopens a closed conversation.
  - Assignment and status changes appear in the team's history, but not in the visitor's widget.
- Channel managers can delete others' messages, like group administrators.

### API

| Endpoint | Notes |
| --- | --- |
| `GET/POST /api/v1/organizations/{org}/workspaces/{ws}/channels` | List visible channels; create. |
| `GET/PUT /api/v1/channels/{id}`, `POST …/join`, `POST …/leave`, `POST …/members`, `PUT/DELETE …/members/{user}`, `DELETE …/teams/{team}` | Channel detail and management. Messages use `/api/v1/conversations/{id}`. |
| `GET/POST /api/v1/organizations/{org}/workspaces/{ws}/teams`, `PUT/DELETE …/{team}`, `GET/POST …/{team}/members`, `PUT/DELETE …/{team}/members/{user}` | Teams. |
| `GET/DELETE /api/v1/organizations/{org}/guests[/{user}]`, `GET/POST/DELETE …/guests/invitations[/{id}]`, `GET /api/v1/guest-invitations`, `POST …/{id}/accept`, `POST …/{id}/decline` | Guests. |
| `PUT /api/v1/organizations/{org}/settings`, `PUT …/members/{user}/support-agent`, `PUT …/workspaces/{ws}`, `PUT …/workspaces/{ws}/members/{user}` | Organization configuration, support agents, workspace rename and role. |
| `GET /api/v1/organizations/{org}/support?status=&mine=`, `POST /api/v1/support/conversations/{id}/claim`, `PUT …/assignee`, `PUT …/status` | Support inbox. |

Contracts were extended additively:

- The conversation summary gains `workspaceId`, `visibility` and `supportStatus`, and a `myRole` of `guest`.
- Organizations gain `supportAgent`, `guestsEnabled` and `channelCreation`.
- History gains the `assigned`, `status_changed` and `visibility_changed` entries.
- `ConversationUpdated` gains `change: "inbox"`.

## Migration impact

Migration `AddBusiness` for both providers, additive:

- New columns:
  - `organizations`: `guests_enabled` (default true) and `channel_creation` (default `Members`).
  - `organization_members.is_support_agent`.
  - `conversations`: `workspace_id`, `visibility`, `channel_key` (unique), `support_status` and `assigned_to_user_id`.
  - `conversation_participants.is_guest`.
- New tables: `teams`, `team_members`, `channel_teams`, `organization_guests` and `guest_invitations`.
- Existing website conversations are set to `Open`.

## Validation results

- .NET: **365 tests passed** (352 before this phase) with PostgreSQL and SQLite, Release build, 0 warnings. New tests:
  - `BusinessTests` (PostgreSQL and SQLite): channel visibility and workspace enforcement, teams, guests, support inbox, per-organization settings.
  - `BusinessRulesTests`: domain rules.
- Web: type-check, production build and 56 tests in 18 files, all passing. The setup wizard test times out when run alongside the .NET suite and passes on its own. New `Business.test.tsx` covers browsing and joining, guest invitations, the support claim, and settings.
- Browser, on a scratch instance:
  - As the owner: channel list, the manage page (adding a team added its member), the channel conversation with a guest's message, taking and closing in the support inbox, and the organization sections (support agents, guests, settings, teams).
  - As the guest: only her channel, with a Guest badge and history from her join, and no Support item in the navigation.

## Open items

- Support staff who are not in a conversation do not get live notice of each visitor message; the inbox refreshes on assignment/status/creation events and every 30 seconds.
- Joining an organization does not add people to any workspace; administrators add them, so channels need that step.
- No archiving or deleting of channels yet; no SLAs, tags, canned answers or routing rules in support.

## Next slice

Phase 12 — open-source release, per the [roadmap](../IMPLEMENTATION.md).
