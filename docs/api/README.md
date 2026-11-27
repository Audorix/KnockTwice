# API documentation

- **Reference:** [`openapi-v1.json`](openapi-v1.json) (OpenAPI 3.1, every REST endpoint with its
  request and response schemas). Open it in any OpenAPI viewer, or run the API in Development and
  browse **Swagger UI** at `/swagger` (the live document is at `/openapi/v1.json`).
- **This page** explains what the reference cannot: authentication, CSRF, errors, paging,
  idempotency and the realtime hubs.

The OpenAPI file is exported from a running instance. To refresh it after changing endpoints,
start the API from source in Development (where OpenAPI is on) and save `/openapi/v1.json`,
removing the `servers` entry.

## Basics

| Topic | Rule |
| --- | --- |
| Base path | `/api/v1/` (version in the path; breaking changes get a new version). Health: `/health/live`, `/health/ready`. |
| Format | JSON (`camelCase`), UTF-8. Timestamps are ISO 8601 UTC (`2026-11-23T10:00:00Z`). Ids are GUIDs. |
| Language | `Accept-Language: en` or `es` localizes error titles; the UI language of an account is part of its preferences. |
| Same origin | The API configures **no CORS**. Browser apps are served from the same origin (Vite in development, nginx in Docker). Other websites integrate through the website-chat widget only. |

## Signing in (browser clients)

Sessions are cookies (`chatx.auth`, HttpOnly, SameSite=Lax, Secure outside Development) backed by a
server-side session you can list and revoke.

1. `GET /api/v1/auth/csrf` → `{ token, headerName }` (also sets the `chatx.csrf` cookie).
2. `POST /api/v1/auth/login` with header `X-CSRF-TOKEN: <token>` and
   `{ "email", "password", "rememberMe" }`.
3. **Get a new CSRF token after signing in or out**: the token is bound to the signed-in identity.

Every `POST`/`PUT`/`PATCH`/`DELETE` under `/api/v1/` needs the `X-CSRF-TOKEN` header. A missing
or stale token answers 400 `csrf_token_invalid`; fetch a new one and retry once. Registration,
confirmation, password reset and external sign-in (Google, Microsoft) are under
`/api/v1/auth/*`; `GET /api/v1/auth/config` tells a client which are enabled.

Website-chat visitors are the exception: `/api/v1/widget/*` uses bearer session tokens and no
cookies or CSRF (see [the widget section](#website-chat-widget)).

## Errors

Failures are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem documents
(`application/problem+json`) with a stable `code`:

```json
{ "type": "...", "title": "The request is not valid.", "status": 400, "code": "validation_failed",
  "errors": { "email": ["email_invalid"] } }
```

- Program against `code` (and the per-field codes in `errors` for 400 `validation_failed`). Never
  parse `title`, which is localized.
- Resources you may not see answer **404** with a resource-specific code (`conversation_not_found`,
  `channel_not_found`, …), whether they exist or not.
- **403** means you can see it but may not do this. **409** means a conflict (`conflict`, a name
  already taken, …). **413** means a file is too large. **429** `too_many_requests` means a rate
  limit. **503** covers `setup_required` (the installation is not set up yet) and unavailable email
  or storage.

## Paging and ordering

Conversation history is ordered by a per-conversation **sequence**.

- `GET /api/v1/conversations/{id}/history?limit=50` returns the newest page, with `items` in
  ascending order and an opaque `nextCursor`.
- Pass `before=<nextCursor>` for older pages. Do not build or parse cursors.
- Read state uses sequences: `POST …/read { "sequence": n }`. The read position only moves forward.

The conversation list comes whole from `GET /api/v1/conversations`, or a page at a time from
`GET /api/v1/conversations/page?limit=50` (default 50, at most 200), most recent activity first:
pass `after=<nextCursor>` while `hasMore` is true. A conversation that becomes active while you page
moves to the top and is not repeated further down; realtime `ConversationUpdated` and message events
tell you to refresh it.

Each summary also carries `pinned` (the caller's own pin; nobody else sees it) and `lastMessage`
(`{ id, author, excerpt, deleted, createdAtUtc }`, the newest main-timeline message the caller can
see, or `null`). `PUT /api/v1/conversations/{id}/pinned { pinned }` pins or unpins a conversation and
returns its summary; ordering is left to the client.

`GET /api/v1/conversations/{id}/files?limit=30` lists the files shared in a conversation, newest
first (`limit` 1–100): `{ items: [{ file, messageId, sequence, author, sharedAtUtc }], nextCursor,
hasMore }`, then `before=<nextCursor>`. Files of deleted messages and files from before the caller
joined are left out; all files of one message stay on the same page.

Contacts in `GET /api/v1/contacts` include `sharedOrganizations` (`[{ id, name }]`), the
organizations both people are active members of.

Other lists that are naturally small (contacts, organizations, channels) are returned whole.
Administration searches take `skip` and `take` (1–100). `GET /api/v1/admin/users` also takes
`filter` (`all`, `enabled`, `disabled`, `unconfirmed`), `role` (`InstallationAdministrator`,
`Auditor`, or `none` for accounts without an installation role) and `sort` (`nameAsc`, `nameDesc`);
`GET /api/v1/admin/organizations` takes `sort` (`nameAsc`, `nameDesc`, `members` for the most
members first). An unknown sort is `400`.

`GET /api/v1/admin/audit` filters by `action` (a prefix), `actor` (a user id), `target` (an id), `succeeded`,
`from` and `to` (UTC instants) and `q` (text in the action, target type or id, or the actor's
name), and orders newest first or `order=oldest` (anything else is `400`). It pages two ways:
`before` + `limit` (newest first, the response's `nextBefore`), or `skip` + `limit`, which also
returns `total`, the number of matching entries. `total` is `null` without `skip`, and
`nextBefore` is `null` when ordering oldest first.

## Sending messages

- `POST /api/v1/conversations/{id}/messages` with `{ "clientMessageId", "body", "replyToId"?, "attachmentIds"? }`.
- `clientMessageId` makes sending **idempotent**: a retry with the same id and body returns the
  original message with 200 (201 the first time). The same id with a different body is 409.
- Attachments are uploaded first (`POST …/attachments`, `multipart/form-data`, field `file`), then
  referenced by id. Download through `GET /api/v1/attachments/{id}/content`, which is authorized on
  every request; there are no public links.
- Messages can be edited by their author within 15 minutes (configurable) and deleted at any time.
  Deletion leaves a tombstone. Every change increments `version`; keep the highest.

Channels are archived and restored with `PUT /api/v1/channels/{id}/archived { archived }` (channel
managers, workspace and organization administrators). An archived channel is read-only: its history
stays, but posting, editing, reacting, joining and adding people answer `409 channel_archived`.
Channels and conversation summaries carry `archivedAtUtc` (null when not archived), and the history
gets an `archived` entry with text `archived` or `restored`.

Channels, groups, direct and website conversations all use these same routes. Channels are managed
under `/api/v1/channels/{id}`; organization, workspace, team, guest and support management under
`/api/v1/organizations/{id}/…`.

## Verified organization domains

Organization owners and administrators manage domain challenges with `GET /api/v1/organizations/{id}/domains`
and `POST /api/v1/organizations/{id}/domains`; verify with
`POST /api/v1/organizations/{id}/domains/{domainId}/verify`, or remove with
`DELETE /api/v1/organizations/{id}/domains/{domainId}`. `POST` accepts
`{ "domain": "example.com" }` and returns the TXT record name and value to publish. Verification
checks the host's configured DNS resolver. Domain proof does not automatically add members;
enrollment remains disabled until an owner or administrator explicitly enables it with
`GET`/`PUT /api/v1/organizations/{id}/domains/automatic-enrollment`. The enrollment switch requires
at least one verified domain. Only confirmed, exact-domain email matches join; new members receive
the member role and access to the first workspace. Disabling the switch does not remove existing
members.

## Organization-context private chats

An organization can require the two people to share a workspace team for chats started in that
organization context. Set `privateChatsRequireSharedTeam` through
`PUT /api/v1/organizations/{id}/settings`; when enabled, `POST /api/v1/private-chats` accepts an
optional `organizationId` to apply the rule. Chats started from the independent contact flow omit
that field and are not affected. Organization-context access is checked again on accept, read,
send, upload, and attachment download; losing the shared-team relationship ends the live chat.

## Permission catalog

Installation administrators and auditors with `users.read` can list the stable permission keys at
`GET /api/v1/admin/permissions/catalog`. Each entry includes its area, scope (`installation`,
`organization`, or `workspace`), and state. Entries marked `planned` are catalog metadata only;
they are not yet accepted by authorization policies or assignable to roles.

## Installation roles and groups

Custom installation access is managed under `/api/v1/admin/access-control`. Listing roles or groups
requires `users.read`; creating, editing, deleting, or assigning them requires `users.manage`.
Installation roles accept only implemented installation-scope permission keys. Planned keys and
organization/workspace keys are rejected. Users receive custom roles only through group membership.
Each role permission is unassigned, `allow`, or `deny`; any matching `deny` overrides `allow` from
another group or role, including built-in permission grants. Changes affect API authorization on the
next request.

| Method and path | Operation |
| --- | --- |
| `GET /admin/access-control/roles` | List custom installation roles and grants |
| `POST /admin/access-control/roles` | Create a custom role |
| `PUT` / `DELETE /admin/access-control/roles/{roleId}` | Update or remove a custom role |
| `POST` / `DELETE /admin/access-control/roles/{roleId}/groups/{groupId}` | Grant or revoke a group role |
| `GET` / `POST /admin/access-control/groups` | List or create groups |
| `PUT` / `DELETE /admin/access-control/groups/{groupId}` | Update or remove a group |
| `POST` / `DELETE /admin/access-control/groups/{groupId}/members/{userId}` | Add or remove a group member |
| `GET /admin/users/{userId}/effective-permissions` | Review custom installation grants |
| `GET /admin/users/{userId}/access-review-contexts` | List installation, organization, and workspace contexts available for a user |
| `GET /admin/users/{userId}/access-review?scope=...` | Review effective permissions and group-to-role paths for the selected scope (plus inherited scopes) |
| `POST /admin/storage-migrations/dry-run` | Inventory files and verify source/destination access before a storage migration |
| `POST /admin/storage-migrations` | Start a persistent verified attachment copy job after preflight |
| `GET /admin/storage-migrations` | List the 50 most recent copy jobs with progress counts |
| `GET /admin/storage-migrations/{jobId}` | Read copy job status and progress |
| `POST /admin/storage-migrations/{jobId}/pause` | Pause a queued or running copy job |
| `POST /admin/storage-migrations/{jobId}/resume` | Resume a paused copy job |

`GET /api/v1/users/me` includes effective allowed and denied permissions. Custom installation roles
are group-based. The separate built-in `InstallationAdministrator` and `Auditor` system roles remain
managed through the installation user administration controls for bootstrap and compatibility.
Installation permissions do not grant conversation content access; resource and conversation
membership checks remain separate.

## Organization-scoped access and private chats

Organization owners manage scoped roles and groups under
`/api/v1/organizations/{organizationId}/access-control`. Organization administrators and members
may use the organization's other management features, but only an owner can manage these role and
group assignments. Group membership accepts active organization members only.

| Method and path | Operation |
| --- | --- |
| `GET` / `POST /organizations/{organizationId}/access-control/roles` | List or create organization roles |
| `PUT` / `DELETE /organizations/{organizationId}/access-control/roles/{roleId}` | Update or remove an organization role |
| `GET` / `POST /organizations/{organizationId}/access-control/groups` | List or create organization groups |
| `PUT` / `DELETE /organizations/{organizationId}/access-control/groups/{groupId}` | Update or remove an organization group |
| `POST` / `DELETE /organizations/{organizationId}/access-control/roles/{roleId}/groups/{groupId}` | Assign or remove a role on a group |
| `POST` / `DELETE /organizations/{organizationId}/access-control/groups/{groupId}/members/{userId}` | Add or remove a group member |

Organization roles can assign the implemented `private_chats.use` permission. Until a decision is
assigned to an organization group, that organization keeps its existing private-chat policy. Once
configured, both people need an effective Allow; a Deny overrides Allow from another role. The
check applies to chats started with `organizationId` and does not affect personal contact chats.
Existing shared-team policy remains an additional restriction when enabled. The role and group scope
is checked on every API operation; IDs from another organization return not found.

## Organization SSO configuration foundation

Owners and administrators can save organization-scoped OIDC settings with
`GET`/`PUT /api/v1/organizations/{id}/sso` and remove them with `DELETE` on the same route.
`PUT` accepts `issuer`, `clientId`, and `clientSecret`; later updates may omit the secret to keep
the protected value already stored. The issuer must be an HTTPS URL and the organization must have
a verified domain. Responses return issuer, client ID, and a `clientSecretConfigured` flag only.
The client secret is protected with the installation's shared Data Protection key ring.

Configured organizations can start sign-in at
`GET /api/v1/auth/organization-sso/{organizationId}`. The OIDC callback requires a verified email
whose domain exactly matches a verified organization domain. Existing confirmed accounts are
linked by exact email; creating a new account requires the organization's separate automatic-
enrollment opt-in. A successful login must still have active membership in the organization.
SSO is an available sign-in method and does not disable password or other configured sign-in
methods.

## Support inbox

Website conversations are the support conversations. Their `status` is `open`, `pending` (waiting
for the customer) or `closed` (shown as resolved); a customer's new message opens a pending or
resolved conversation again. Support agents and organization owners/administrators see the whole
inbox; responders see the conversations they take part in.

- `GET /api/v1/organizations/{id}/support/inbox` returns one page: `{ items, total, counts }`.
  Filters: `status` (`open`, `pending`, `closed`, `all`), `mine=true`, `assignment` (`anyone`,
  `assigned`, `unassigned`), `q` (customer name or subject), `sort` (`oldest`, the longest waiting
  first, or `newest`), `skip` and `take` (1–100, default 25). `counts` gives `open`, `pending`,
  `closed` and `all` under the other filters, plus `unassignedOpen` for visible open conversations
  with no assignee. Each item has the `conversation`, a `title` (the
  subject, or the first line of the first message) and the `lastMessage`
  (`{ text, fromCustomer, sentAtUtc }`). The older `GET …/support` list stays.
- `GET /api/v1/support/conversations/{id}/history` gives support staff the whole history, whether
  or not they take part (same shape as the conversation history). `POST …/join` joins to reply
  without changing the assignment; `POST …/claim` takes it; `PUT …/assignee` and `PUT …/status`
  as before.
- `PUT /api/v1/support/conversations/{id}/subject { subject }` sets or clears what it is about
  (internal; `400 subject_invalid` over 120 characters).
- **Internal notes:** `GET` and `POST /api/v1/support/conversations/{id}/notes { body }` (up to 4,000
  characters; `note_body_required`, `note_body_invalid`). Notes are stored apart from the history,
  so no message path (history, realtime, notifications, search, the visitor widget) ever carries
  one; only the team is told to reload.

## Message translation

- Users granted `messages.translate` through the installation group-to-role access model may request
  `POST /api/v1/conversations/{conversationId}/messages/{messageId}/translate` with
  `{ targetLanguage: "en" }`. The response is `{ translation, targetLanguage }`; the original message
  is not changed or stored as a translation. The caller must already be able to read the message.
- The web client also offers authorized support agents an opt-in browser preference that requests
  translations for support messages as they become visible. It uses the same endpoint and permission
  check; translation remains off by default.
- Administrators with `providers.read` can list named provider profiles at
  `GET /api/v1/admin/providers/ai/translation`. With `providers.manage`, they can create a profile
  (`POST`), update one (`PUT /{profileId}`), test one (`POST /{profileId}/test`), select the active
  profile (`POST /{profileId}/activate`), and delete an inactive profile (`DELETE /{profileId}`).
  API keys are never returned. Existing flat AI translation settings remain available as a legacy
  active provider until an administrator selects a named profile.

## Search

`GET /api/v1/search?q=…` searches the messages the caller can read right now (only conversations
they take part in, from their join, never deleted messages), newest first.

- Every word of `q` must match the **start** of a word in the message text or in an attachment's
  file name; case and accents are ignored (`presup cafe` finds "Presupuesto del Café"). `q` needs at
  least 2 characters (`400 query_too_short`).
- Filters: `conversationId`, `senderId`, `from` / `to` (UTC timestamps, inclusive), `hasFiles=true|false`.
- `limit` (1–50, default 20); pass `nextCursor` back as `cursor` (with the same filters and order) for
  more results. `order=oldest` returns the oldest first (default `newest`).
- The first page (no `cursor`) also has `counts: { total, withFiles }`: all matching messages and
  those with files, whatever `hasFiles` is (so `total - withFiles` have no files). Counts come from
  the index, so a message deleted a moment ago may still count for a few seconds. Later pages have
  `counts: null`.
- Each item has the `conversation` (as in the conversation list) and the `message` (as in history,
  with the caller's `saved`).
- `GET /api/v1/conversations/{id}/messages/{messageId}/context?size=3` returns the message with up to
  `size` (0–20) main-timeline entries before and after it, ascending: `{ items, hasOlder, hasNewer }`.
  Never anything before the caller's join (`404 message_not_found` for such a message).
- Changes reach the index within a few seconds. `403 search_disabled` when an administrator switched
  search off; `503 search_unavailable` when the search engine (OpenSearch) is down.
- Administrators: `GET /api/v1/admin/search` (provider and pending changes) and
  `POST /api/v1/admin/search/rebuild`.

## Notifications

- Preferences: `PUT /api/v1/users/me/preferences` with `emailNotifications`: `batched`, `daily` or
  `off`; `notificationPreviews` also decides whether emails and pushes include message text.
- Web Push: `GET /api/v1/notifications/push` gives `{ enabled, publicKey }` (VAPID, base64url) for
  `PushManager.subscribe({ userVisibleOnly: true, applicationServerKey })`. Register the result of
  `subscription.toJSON()` with `POST /api/v1/notifications/push/subscriptions`; stop with
  `POST /api/v1/notifications/push/subscriptions/remove { endpoint }`. A subscription ends with the
  sign-in session that created it.
- Pushed payloads (decrypted by the browser) are `{ title, body, url, tag }`.

## Drafts, scheduled messages and reminders

- Drafts: `PUT /api/v1/conversations/{id}/draft { body, replyToId?, threadRootId? }` (204; empty body
  without `replyToId` removes it); `GET /api/v1/drafts`.
- Scheduled: `POST /api/v1/conversations/{id}/scheduled { body, sendAtUtc, replyToId?, threadRootId?,
  alsoInConversation?, attachmentIds?, mentionUserIds? }` (201) — `sendAtUtc` in the next 30 days
  (`400 send_at_invalid`), at most 50 waiting (`409 too_many_scheduled`). `GET /api/v1/scheduled`
  returns `{ id, conversationId, conversationType, conversationTitle, body, sendAtUtc, status:
  pending|failed, failureCode, fileCount, threadRootId, replyToId }`; `PUT /api/v1/scheduled/{id}
  { body, sendAtUtc }` (waiting only); `DELETE /api/v1/scheduled/{id}`. At its time the message is
  sent with the normal rules; a refusal sets `status: failed` and `failureCode` (an API error code).
- Reminders: `POST /api/v1/conversations/{id}/messages/{messageId}/reminder { remindAtUtc }` (201,
  up to a year ahead, at most 100); `GET /api/v1/reminders` returns `{ id, conversationId,
  conversationType, conversationTitle, entry, remindAtUtc, status: pending|due }`;
  `DELETE /api/v1/reminders/{id}` marks it done.
- Realtime (chat hub): `PlanningChanged { kind: drafts|scheduled|reminders }` and
  `ReminderDue { reminderId, conversationId, messageId, threadRootId, authorName, excerpt }`.

## Message formatting

- `body` is the text as typed, using a Markdown-style subset (ADR-033): `**bold**`, `*italic*` /
  `_italic_`, `~~strike~~`, `` `code` ``, fenced code blocks, `> ` quotes, `- ` / `1. ` lists,
  `[text](url)` and bare links. Never render it as HTML; render the subset yourself or show it as
  plain text. Only `http`, `https` and `mailto` links should be clickable.
- `replyTo.excerpt`, email and push texts are plain (markers removed).

## Pins, saved messages and forwarding

- Pins: `PUT /api/v1/conversations/{id}/messages/{messageId}/pin { pinned }` (204). Direct
  conversations: either person; groups and channels: owners and administrators (`403 forbidden`
  otherwise); at most 50 (`409 too_many_pins`); not in website chats (`400 pins_unavailable`).
  `GET …/pins` returns `[{ entry, pinnedBy, pinnedAtUtc }]`, most recent first. Entries carry `pinned`;
  pinning adds a history entry of kind `pinned` whose `replyTo` is the pinned message; participants
  receive `ConversationUpdated` with change `pins`.
- Saved: `PUT …/messages/{messageId}/saved { saved }` (204); entries carry the caller's `saved`
  (null in realtime events). `GET /api/v1/saved?before=&limit=` returns `{ items: [{ conversationId,
  conversationType, conversationTitle, entry, savedAtUtc }], nextCursor, hasMore }`, only messages
  the caller can still read.
- Forward: `POST …/messages/{messageId}/forward { targetConversationId, clientMessageId, comment? }`
  creates a copy (201; a retry with the same `clientMessageId` returns it with 200) whose
  `forwardedFrom` is the original author; files are copied. `400 forward_unavailable` for website chats.

## Mentions and notification levels

- Mention people by sending `mentionUserIds` (participants of the conversation) with the message;
  write their `@Name` in the text for readers. `@everyone` and `@here` are read from the text in
  groups and channels. Errors: `400 mention_invalid` (not a participant), `400 too_many_mentions`,
  `403 mention_everyone_forbidden` (large conversation, not an owner or administrator),
  `400 mentions_unavailable` (website chats).
- Entries carry `mentions { userIds, everyone, here }` (null when none). In a realtime event a
  message mentions you when your id is in `userIds` or `everyone`/`here` is true.
- `GET /api/v1/mentions?before=&limit=` returns `{ items: [{ conversationId, conversationType,
  conversationTitle, entry, unread }], nextCursor, hasMore }`; `GET /api/v1/mentions/unread` returns `{ unread }`.
- `PUT /api/v1/conversations/{id}/settings { notificationLevel: "all" | "mentions" | "nothing" }`;
  summaries carry `notificationLevel` and `muted` (true for anything but `all`). `{ muted: true }`
  still works and means `mentions`.

## Reactions and threads

- React: `PUT /api/v1/conversations/{id}/messages/{messageId}/reactions { emoji, reacted }` (true adds
  yours, false removes it) returns the message's reactions `[{ emoji, count, userIds }]`. One emoji per
  call; `400 reaction_invalid` for anything else, `409 too_many_reactions` past 20 different emoji,
  `400 reactions_unavailable` in website chats. Others receive the realtime event `ReactionChanged`
  `{ conversationId, messageId, reactions }` with the complete new set. History entries carry `reactions`.
- Reply in a thread: send a message with `threadRootId` (a top-level message) and optionally
  `alsoInConversation: true`. Entries carry `threadRootId` and `shownInConversation`; history leaves out
  replies that are not shown in the conversation. A message with replies carries
  `thread { replyCount, lastReplyAtUtc, following, unread }` (`following` and `unread` are the caller's,
  null in realtime events).
- `GET …/messages/{messageId}/thread?before=&limit=` returns `{ root, items, nextCursor, hasMore,
  following, lastReadSequence }`; `POST …/thread/read { sequence }` returns `{ unread }`;
  `PUT …/thread/follow { following }`. Thread replies arrive as ordinary `MessageCreated` events with
  `threadRootId` set.

## Profile photos and links

- Every user summary's `avatar` is `{ initials, photoUrl }`. `photoUrl` is null for initials; otherwise
  `/api/v1/users/{id}/photo?v={version}`, readable by any signed-in person. The address changes with
  every new photo, so the response for the current version is cached privately for good
  (`immutable`); old addresses keep working but revalidate.
- `POST /api/v1/users/me/photo` (multipart, field `file`) sets the photo: PNG, JPEG or WebP by
  signature (`400` with field error `photo_type_invalid` otherwise), at most 1 MB
  (`413 photo_too_large`). Clients crop and resize first; the web app sends a 512 px square.
  `DELETE /api/v1/users/me/photo` goes back to initials. Both return the current user.
- `GET /api/v1/users/me/links` and `PUT /api/v1/users/me/links { links: [{ platform, url, visibility }] }`
  (at most 10, in order; the list replaces them all). `platform`: `website`, `github`, `linkedin`,
  `youtube`, `instagram` or `x`; `visibility`: `contacts` or `only_me`. Only http(s) addresses, and
  platform links must be on that platform's site; problems come back per row as field errors
  (`links[0].url`: `link_url_required`, `link_url_invalid`, `link_url_platform`;
  `links[0].platform`: `link_platform_invalid`; `links[0].visibility`: `link_visibility_invalid`).
- `GET /api/v1/users/{id}/links` gives the links for contacts when the caller is a contact, all of
  them for the person themself, and an empty list for anyone else.
- Deleting an account removes the photo and the links.

## Deleted accounts and deletion

- A user summary (`{ id, displayName, avatar, deleted }`) has `deleted: true` when the account was
  deleted. `displayName` is then the placeholder "Deleted user"; show your own localized label.
  Their messages stay in history.
- Sending into a direct conversation with a deleted person fails with `403 recipient_deleted`.
- `GET /api/v1/users/me/deletion` says whether the account can be deleted now
  (`blockingOrganizations`: organizations the caller is the only owner of; `lastAdministrator`).
- `POST /api/v1/users/me/deletion` with `{ "password" }` (or `{ "confirmEmail" }` for accounts
  without a password) deletes the caller's account and signs out every session. `409 sole_owner` or
  `409 last_administrator` when it cannot be deleted yet.
- `POST /api/v1/organizations/{id}/delete` with `{ "confirmName" }` deletes an organization and
  everything in it (owners). Administrators use `DELETE /api/v1/admin/users/{id}` and
  `POST /api/v1/admin/organizations/{id}/delete`.

## Realtime (SignalR)

**`/hubs/chat`** is for signed-in users. It authenticates with the sign-in cookie, accepts only the
web app's origin, and re-checks the session on every call. Contract version: `version: 1` in every
payload.

| Client → server | Purpose |
| --- | --- |
| `JoinConversation(conversationId)` / `LeaveConversation(conversationId)` | Receive a conversation's typing and group events (access is checked). |
| `SendTypingStatus(conversationId, isTyping)` | Typing indicator (expires by itself). |
| `GetPresence()` | Presence of contacts and of people you share a direct chat or group with (not channels). |

| Server → client | Payload |
| --- | --- |
| `MessageCreated`, `MessageUpdated`, `MessageDeleted` | `{ eventId, conversationId, entry }`. **At least once**: drop duplicate `eventId`s and keep the highest `entry.version`. |
| `MessageDelivered`, `MessageRead` | `{ conversationId, userId, sequence }` receipts. |
| `ConversationUpdated` | `{ conversationId, change, lastSequence }`. `change` is `created`, `updated`, `members`, `read`, `removed` (access ended) or `inbox` (support inbox changed). Reload what you show. |
| `TypingStatusChanged` | `{ conversationId, userId, isTyping, expiresAtUtc }`. |
| `UserPresenceChanged` | `{ userId, status: online/offline, atUtc }`. |
| `PrivateChatChanged`, `PrivateMessageCreated` | Private chats. They are never stored; forget everything when `status` is `ended`. |

After a reconnect, events may have been missed. Catch up with the change feed:

1. Before or right after connecting, take a cursor: `GET /api/v1/changes/cursor` → `{ cursor }`.
2. After a reconnect, `GET /api/v1/changes?since=<cursor>` → `{ reset, cursor, conversationIds, entries }`.
   `entries` holds `{ conversationId, entry }` with the **current state** of every message created,
   edited or deleted, or whose reactions changed, in conversations you may still see. Apply them by
   `entry.id` and `entry.version`, refresh the conversations in `conversationIds`, and keep the new
   `cursor`. The same change may come twice; never fewer.
3. `reset: true` means more than 500 changes, or a cursor older than 24 hours: reload the
   conversation list and the open history, then continue from the returned `cursor`.

An invalid cursor is a `400` with `cursor_invalid`. Receipts, membership and conversation settings
are not in the feed: reload the list and the open conversation for those. Use the official
`@microsoft/signalr` client; JSON protocol.

## Website chat (widget)

These endpoints are for the embedded chat. Most integrations need only the loader script; see
[examples/website-chat](../../examples/website-chat).

- `GET /api/v1/widget/{widgetId}/config`: public appearance of an enabled widget.
- `POST /api/v1/widget/{widgetId}/sessions` with `{ parentOrigin, language, identityToken? }`
  → `{ token, expiresAtUtc, … }`. Send the token as `Authorization: Bearer` to the `/api/v1/widget/visitor/*`
  endpoints, and as `access_token` to the hub `/hubs/widget`.
- **Identity tokens** are HS256 JWTs your **server** signs with the widget's signing secret.
  - Required claims: `sub`, `aud` (= the widget id), `iat`, `exp`. Optional: `name`, `email`.
  - Validity: at most 1 hour by default.
  - Never send the secret to a browser.
- Limits: 20 messages per minute per session; 5 new anonymous sessions per hour per address.

## Rate limits

- Sign-in, registration, recovery and confirmation: 20 requests per minute per IP by default.
- Visitor limits are listed above.

All of them answer 429 `too_many_requests`. Behind a reverse proxy, configure
`Network:TrustedProxies` so the limits apply per real client.
