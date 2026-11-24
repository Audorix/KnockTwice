# Changelog

All notable changes to Audorix.ChatX are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). Before 1.0, minor versions may contain breaking changes;
they are called out here.

## [Unreleased]

the UI redesign ([delivery record](docs/releases/ui-redesign.md)),
Phase 24, throughput ([delivery record](docs/releases/phase-24-throughput.md)),
Phase 23, send-path speed ([delivery record](docs/releases/phase-23-send-path.md)),
Phase 22, performance ([delivery record](docs/releases/phase-22-performance.md)),
Phase 21, release readiness ([delivery record](docs/releases/phase-21-release-readiness.md)),
Phase 20, drafts, scheduled messages and reminders ([delivery record](docs/releases/phase-20-planning.md)),
Phase 19, rich text ([delivery record](docs/releases/phase-19-rich-text.md)), Phase 18, message
tools ([delivery record](docs/releases/phase-18-message-tools.md)), Phase 17,
mentions ([delivery record](docs/releases/phase-17-mentions.md)), Phase 16, reactions and
threads ([delivery record](docs/releases/phase-16-reactions-threads.md)),
Phase 15, notifications ([delivery record](docs/releases/phase-15-notifications.md)), Phase 14,
message search ([delivery record](docs/releases/phase-14-search.md)), and Phase 13, 1.0 hardening
([delivery record](docs/releases/phase-13-hardening.md)).

### Changed (UI redesign)

- **New look:** calmer surfaces in light and dark (slate greys instead of near-black), the chat fills
  the window edge to edge, teal bubbles for other people and grey for your own, and a cleaner message
  box.
- **Message actions** appear as small icons beside a message on hover, tap or keyboard focus instead
  of a row of links under every message.
- **Signed-out pages** have a slim top bar and centred content; the home page introduces the product.
- **Sidebar:** on wide screens a labelled sidebar with a Workspace section and your name and status
  at the bottom (icon rail on medium windows, tab bar on phones).
- **Contacts page:** tabs for contacts, requests and blocked people; filter, status and sort; a table
  with shared organizations, a Message button and a "…" menu; paging; a profile side panel.
  `GET /api/v1/contacts` now includes `sharedOrganizations`.
- **Messages page:** a conversation list with search, All/Unread/Groups chips, your own **pinned**
  conversations on top and last-message previews; a chat header with participants, search and a
  menu; **Messages / Shared files** tabs; day separators and a "new messages" divider; names and
  times above bubbles, read receipts below; a composer box with attach, emoji, formatting and a
  send button with "send later". API: `pinned` and `lastMessage` on conversation summaries,
  `PUT /api/v1/conversations/{id}/pinned` and `GET /api/v1/conversations/{id}/files`
  (database migration `AddConversationPins`).
- **Activity page:** tabs with icons and counts (Mentions, Saved messages, Scheduled, Reminders);
  mentions grouped by day with search, All/Unread, a conversation filter and order, "Mark as read"
  per mention and "Mark all as read"; a "Coming up" column with the next reminders and scheduled
  messages.
- **Administration:** a page header with the environment and version; underlined section tabs; an
  installation overview with Users, Organizations and Registration cards (with links to their
  sections), the providers in use (a warning while malware scanning is off), installation details
  and shortcuts to roles, settings and the audit log. Links appear only for sections you may open.
  **Users:** status and role filters, sorting, a table (status, installation role, last sign-in,
  sessions, organizations), Manage and a "…" menu (view details, manage roles, sign out, confirm
  email, disable or enable, delete), a details panel with role switches, 8/25/50 per page.
  **Organizations:** search, sorting (name, most members), a table with members and creation date,
  and members in a side panel (rename, roles, remove, delete for administrators). **Roles:** role
  cards with member counts and a permission comparison that shows that no role grants access to
  conversations. API: `role` and `sort` on `GET /api/v1/admin/users` (and `filter=enabled`),
  `sort` on `GET /api/v1/admin/organizations`.
- **Administration settings, providers and audit log:** **Settings** has a group list with "Find a
  setting", section cards in two columns with units and readable durations and sizes, an optional
  view of where each value comes from, and a save bar with the number of unsaved changes and
  Discard. **Providers** shows a card per provider (database, email, CAPTCHA, storage, scanning,
  search) with its state and Configure; the panel shows only the chosen provider's fields, a test
  card (email sends a test to your own address) and, for search, the index rebuild. The database
  is read-only there. **Audit log:** search (action, actor, target), action category, outcome and
  date filters, newest or oldest first, event titles with icons, expandable details with a copy
  button, 8/25/50 per page and the retention period. API: `from`, `to`, `q`, `skip` and
  `order=oldest` on `GET /api/v1/admin/audit`, and `total` when paging with `skip`.
- **Contacts:** "Start private chat" in a contact's "…" menu sends the private chat invitation
  and opens the chat, which waits for the contact to accept.
- **Landing page and sign-in:** signed-out visitors see "Your conversations. Your team. Your
  space." with a picture of the app, four features, a one-line service status and what the preview
  includes (folded). The top bar has Features (or Back to home), a light/dark switch remembered in
  this browser, the language and the account buttons; the footer has the languages and the
  license. Sign-in, registration and recovery use a two-panel card ("A better place to connect."
  beside the form); sign-in says "Welcome back", has placeholders, a show/hide password button and
  "New to ChatX? Create an account".
- **Home (welcome page):** a Home item in the sidebar; the date, development tools (administrators
  on development installations) and the connection state at the top; "Welcome back" with Open chats
  and New conversation; shortcuts to people, channels, saved messages and your profile; recent
  conversations with previews and unread counts; what needs your attention (unread mentions,
  contact requests, the next reminder); your workspaces; a "Make it yours" nudge until you have a
  photo and links. Administrators get a **Development tools** panel with the service status, demo
  data (counts as tiles) and what this build contains. Signed out, the page keeps the product
  introduction.
- **My profile:** a header with your photo, name, verified email and how others see you;
  **Profile · Media & links · Notifications · Security · Sessions** tabs. Profile has the details
  (name, email, photo), theme cards and language, and a sessions summary.
- **Profile photos:** an editor to drag, zoom and rotate the image, with previews at 120, 40 and
  24 px; the browser crops it to a 512 px square before uploading. The photo replaces the initials
  everywhere people appear; "Use initials" removes it. API: `avatar.photoUrl`,
  `POST`/`DELETE /api/v1/users/me/photo`, `GET /api/v1/users/{id}/photo`.
- **Media & links:** up to 10 links (GitHub, LinkedIn, YouTube, Instagram, X, website) with
  Contacts or Only me visibility, reordering by drag or keyboard, a preview of what contacts see;
  contacts see them in the contact profile panel. API: `GET`/`PUT /api/v1/users/me/links`,
  `GET /api/v1/users/{id}/links`. Database migration `AddProfilePhotosAndLinks`.
- **Support inbox:** Open, Pending and Resolved tabs with counts; search, assignment and "assigned
  to me" filters, oldest waiting or newest first; the list beside the selected conversation
  (subject, preview, channel, assignment, unread); the conversation with its status, assignee and
  start time, messages, day separators and **internal notes** for the team; a composer with
  Reply and Internal note. New **Pending** status (waiting for the customer; their next message
  opens it again) and a **subject** for support conversations. API: `GET
  /api/v1/organizations/{id}/support/inbox` (page, totals, previews), staff history, join,
  subject and notes under `/api/v1/support/conversations/{id}`. Database migration
  `AddSupportNotesAndSubjects`.
- **Demo data:** 500 support conversations with replies, internal notes, every status and
  assignments; the administrator who loads it becomes a support agent in their demo companies. The
  demo data counts gain `support`.
- **Smaller start-up download:** Spanish texts load only when Spanish is used (start-up JavaScript
  159 kB instead of 182 kB gzip).
- **Tab title fix:** the unread count in the browser tab title stays after the app applies your
  language (it could disappear until the next new message).
- **Organizations page (follow-up):** "Organization" under each name (the creation date shows on
  hover), the create panel shown beside the list on wide screens, your own organization's row
  highlighted, clearer Manage/Open buttons.
- **Private chats page:** a page header with a "Session only" badge; when no private chat runs, an
  explanation with "Open conversations" and three facts (one sign-in session, outside chat
  history, this browser only); running chats listed with their state. The invitation is a card
  (person, state, the rules as rows with icons, the screenshot note, Cancel / Send invitation); the
  waiting and incoming-invitation screens use the same card.
- **Organizations page:** All organizations · Owned by me · Member tabs with counts, search,
  sorting, a table with your role and Manage (owners and administrators) or Open, eight per page,
  and a "Create an organization" panel beside the list (open when you have none).
- **Channels page:** organization and workspace bar; **All channels · Joined · Discover · Archived**
  tabs with counts; search, visibility filter and sorting (recently active, name, most members); a
  table with channel icon, Joined/Not joined/Private tags, description, members, last activity,
  Open or Join channel and a "…" menu (manage, archive or restore, leave); "New channel" in a side
  panel. Compact rows on phones. Without a choice in the address it opens the organization and
  workspace of your most recently active channel (before, it opened your first organization, which
  could have no channels, so demo channels seemed missing).
- **Channel archiving:** managers archive a channel (read-only: history stays, no posting,
  editing, reacting, joining or adding people) and restore it. `PUT /api/v1/channels/{id}/archived`,
  `archivedAtUtc` on channels and conversation summaries, an `archived` history entry, error
  `channel_archived`; database migration `AddChannelArchiving`.
- **Demo data:** 300 more channels (800 in all; everyone is in at least 10) and activity: mentions,
  saved messages, reminders and scheduled messages, 500 items for the administrator who loads it and
  50–100 for every demo person, with recent mentions unread. Loading again extends an existing demo
  installation. The demo data counts gain `activity`.
- **Search page:** a search card with conversation, sender and date filters shown as removable
  chips; **All results · Messages · Files** tabs with totals; newest or oldest first; result cards
  with highlighted words, files, "View in conversation" and save; a **Message context** panel with
  the messages around the selected result (a slide-over on smaller screens). Opening search from a
  conversation now starts filtered to it. API: `counts` and `order=oldest` on
  `GET /api/v1/search`, results carry `saved`, and
  `GET /api/v1/conversations/{id}/messages/{messageId}/context`.

### Changed (Phase 24)

- **Busy groups:** messages sent to the same conversation at the same moment wait for each other
  for a few milliseconds instead of failing and retrying; positions stay strictly in order, without
  gaps.
- **Push notifications** are prepared per batch instead of per message (fewer database reads).

### Changed (Phase 23)

- **Faster sending:** a text message runs 8 database statements instead of 22 (it is described once
  and published from memory), and an active session is re-read at most every 15 seconds.
- **Fewer "try again" errors in busy groups:** simultaneous sends retry up to 8 times with a short
  random pause before answering 409 (was 3).
- **Sign-out on other servers:** with several API instances, a revoked session can still be accepted
  by another instance for up to 15 seconds (same server: at once).

### Added (Phase 22)

- **Reconnect catch-up:** after a dropped connection the web app fetches what changed while it was
  away (`GET /api/v1/changes?since=`): new, edited and deleted messages and reactions appear without
  a reload. Over 500 changes, or after 24 hours, it reloads instead.
- **Paged conversation list:** `GET /api/v1/conversations/page` (50 at a time, at most 200); the web
  app shows "Show more conversations". The unpaged list is unchanged.
- **Load test:** `tools/Audorix.ChatX.LoadTest` measures a test installation against the capacity
  targets (2,000 connected people, 100 messages a second, p95 send < 300 ms, p95 history < 200 ms).

### Changed (Phase 22)

- **Presence:** you now see online status of your contacts and of people you share a direct chat or
  group with. Sharing an organization alone no longer shows it (in large organizations announcing
  everyone to everyone overloaded the server). Channels and website chats do not count.

- **Faster first load:** pages are downloaded when first opened and the realtime client only after
  sign-in; the first download fell from 182 kB to 167 kB gzip. CI fails when it grows past 180 kB
  (`npm run check:bundle`).

### Added (Phase 21)

- **Several API instances (optional):** set `ScaleOut:Redis` to a Redis connection string to share
  realtime events (SignalR backplane), presence and sign-in/visitor rate limits between instances.
  Off by default; readiness reports Redis when on. Private chats still need a single instance.
- **Session purge:** sign-in sessions that ended more than `Retention:ClosedSessionDays` (30) days ago
  are deleted, with those browsers' push subscriptions (Admin → Settings → Data retention).
- **CI:** `.github/workflows/ci.yml` runs the .NET tests (SQLite and PostgreSQL) and the web checks.

### Changed (Phase 21)

- Sign-in rate limiting counts through the same counter as the visitor limits (sliding window on a
  single server, shared fixed window with Redis); limits and settings are unchanged.

### Added (Phase 20)

- **Drafts on every device:** unsent text and its reply target are saved per conversation and thread
  and marked "Draft" in the conversation list (`PUT …/draft`, `GET /api/v1/drafts`).
- **Send later:** under the message box, pick in 20 minutes, 1 or 3 hours, tomorrow, next Monday or a
  custom time (up to 30 days). The **Scheduled** tab lists them to edit or cancel; a message that
  cannot be sent at its time stays there with the reason (`POST …/scheduled`, `/api/v1/scheduled`).
- **Reminders:** **More → Remind me…** on a message. At the time you get a notice in the app, a push
  notification and, when you are away, an email; the **Reminders** tab lists them until you mark
  them done (`POST …/messages/{id}/reminder`, `/api/v1/reminders`).
- The Mentions navigation item is now **Activity** (Mentions, Saved, Scheduled, Reminders); its badge
  counts unread mentions and due reminders. Setting `Planning:DispatchInterval` (15 s).

### Upgrade notes (Phase 20)

- Migration `AddPlanning` adds `message_drafts`, `scheduled_messages`, `scheduled_message_files` and
  `reminders`.

### Added (Phase 19)

- **Formatting in messages:** `**bold**`, `_italic_`, `~~strike~~`, `` `code` ``, code blocks,
  `> ` quotes, lists and links, shown formatted in conversations, threads and the website widget.
  Links open in a new tab; only http, https and mailto are clickable. Mentions are highlighted.
- **Formatting toolbar and shortcuts** in the composer: Ctrl/Cmd+B, I, E (code), K (link),
  Ctrl/Cmd+Shift+X (strikethrough).

### Changed (Phase 19)

- Emails, push notifications and reply previews show messages without formatting markers.
  Messages are stored exactly as typed; nothing to migrate.

### Added (Phase 18)

- **Pinned messages:** **More → Pin** on a message (either person in direct conversations, owners and
  administrators in groups and channels; up to 50). A "pinned" entry appears in the history and a
  "N pinned" button lists them (`GET …/pins`, `PUT …/messages/{id}/pin`).
- **Saved messages:** **More → Save** keeps a private bookmark; the **Saved messages** tab on the
  Mentions page lists them while you can still read them (`PUT …/messages/{id}/saved`, `GET /api/v1/saved`).
- **Forwarding:** **More → Forward** sends a copy (text and files) to another of your conversations,
  marked "Forwarded from {author}", optionally after a comment (`POST …/messages/{id}/forward`).
- **Jump to message:** mentions, search results, saved and pinned messages open the conversation at
  the message and highlight it.

### Upgrade notes (Phase 18)

- Migration `AddMessageTools` adds `message_pins`, `saved_messages` and
  `conversation_events.forwarded_from_user_id`, and relaxes the history check constraint for
  "pinned" entries.

### Added (Phase 17)

- **Mentions:** type `@` in a message (or thread reply) to pick a participant; `@everyone` and
  `@here` notify the whole group or channel (or those online). In groups and channels with more than
  20 people only owners and administrators can use them (`Messaging:EveryoneMentionLimit`).
- **Mentions view** (`/mentions`, `GET /api/v1/mentions`): messages that mention you, newest first,
  with unread state and a badge in the navigation.
- **Notification levels** per conversation: All messages, Only mentions, or Nothing
  (`PUT …/settings { notificationLevel }`). Push, email and in-app notifications follow them.

### Changed (Phase 17)

- **Mute became "Only mentions"**: the Mute button is replaced by the notification level, and muted
  conversations still notify you when you are mentioned. Choose "Nothing" for complete silence. The
  API keeps `muted` (true for anything but "all") and still accepts `{ muted }`.

### Upgrade notes (Phase 17)

- Migration `AddMentions` replaces `conversation_participants.muted` with `notification_level`
  (muted conversations become `Mentions`) and adds `message_mentions`.

### Added (Phase 16)

- **Emoji reactions:** **React** on any message (six quick emoji, more, or any emoji typed in); at
  most 20 different emoji per message; click a reaction to add or remove yours. Live for everyone
  (`PUT …/messages/{id}/reactions`, realtime event `ReactionChanged`).
- **Threads:** **Reply in thread** opens a side panel; replies stay out of the conversation unless
  "Also send to the conversation" is ticked. Messages show "N replies" with your unread count.
  Follow or stop following; followers are notified about replies
  (`GET …/messages/{id}/thread`, `POST …/thread/read`, `PUT …/thread/follow`; `threadRootId` and
  `alsoInConversation` when sending).
- Not available in website chats or private chats.

### Upgrade notes (Phase 16)

- Migration `AddReactionsAndThreads` adds the thread columns on conversation entries and the
  `message_reactions` and `thread_follows` tables. Existing messages are unaffected.

### Added (Phase 15)

- **Email notifications:** messages still unread 15 minutes after they arrived are emailed in one
  batch, at most hourly; each person can choose daily emails or switch them off. Text only with
  message previews on.
- **Web Push:** "Notifications in this browser when ChatX is closed" on the account page; encrypted
  payloads (RFC 8291) with VAPID (RFC 8292); a service worker at `/sw.js`. Push stops when the
  browser's sign-in ends.
- Live switches `Notifications:EmailEnabled` and `Notifications:PushEnabled`, plus the delay and interval.

### Upgrade notes (Phase 15)

- Migration `AddNotifications` adds the email preference (default batched), the per-participant
  "notified up to" position (set to the current end of every conversation, so nothing old is
  emailed), push subscriptions, the push queue and the VAPID key table.
- People start receiving notification emails after the upgrade; check the email provider first.

### Added (Phase 14)

- **Message search** (`GET /api/v1/search`, **Search** in the web app):
  - every word matches the start of a word, ignoring case and accents, in message text and attachment file names;
  - filters by conversation, sender, dates and files, with highlighted results and a quick jump to conversations by name;
  - only what the person can read: their conversations, from their join, never deleted messages.
- **Search providers:** the database (SQLite FTS5 or PostgreSQL full-text, the default) or OpenSearch
  (`Search:Provider`, **Admin → Providers → Message search**). Index status and rebuild for
  administrators; `Search:Enabled` switches search off live.

### Changed (demo data, before Phase 15)

- **Populate demo data** now loads 300 people, 50 companies, about 960 contacts, 150 groups, about
  330 direct conversations, 500 channels and about 12,500 messages over the last 30 days. It is still idempotent and
  extends demo data loaded by earlier versions instead of duplicating it.
- Every demo account uses the password `Demo123!`; accounts from earlier loads are switched to it.
- **Clear demo data** (`DELETE /api/v1/admin/demo-data`) removes only demo data, flagged by the new
  `is_demo` column (migration `AddDemoFlags`; earlier demo data is flagged by the migration).

### Upgrade notes (Phase 14)

- Migration `AddMessageSearch` adds `message_search` (with an FTS5 table and triggers on SQLite, or a
  generated `tsvector` column with a GIN index on PostgreSQL) and `search_queue`, and queues every
  existing message. The API indexes them in the background after the upgrade; until it finishes,
  older messages may be missing from results.

### Added

- **Account deletion:**
  - People delete their own account after confirming their password.
  - Administrators delete accounts from **Admin → Users**.
  - Personal data is erased; messages stay and show "Deleted user" (localized).
  - Refused while the person is the only owner of an organization or the last administrator.
- **Organization deletion** by owners (on the organization page) and administrators, with
  everything in it, including files.
- **Data retention** (**Admin → Settings → Data retention**, `Retention:*`):
  - inactive anonymous website visitors after 90 days;
  - never confirmed accounts after 30 days;
  - optionally, messages and their files older than a chosen number of days.
- **Backup and restore guide** ([docs/backup-restore.md](docs/backup-restore.md)) and
  `scripts/restore-drill.ps1`, which practises a restore on throwaway data with SQLite or PostgreSQL.
- `@audorix/chatx-embed` is ready to publish to npm (`npm pack` builds it; license included).

### Changed

- Direct conversations with someone who deleted their account stay readable but accept no new
  messages (`403 recipient_deleted`).
- User summaries in the API carry `deleted: true` for deleted accounts.
- ADR-020 (release boundaries) is accepted; the discovery and presence defaults are confirmed.

### Upgrade notes

- Migration `AddAccountDeletion` (both databases) adds `deleted_at_utc` to `users` and
  `user_profiles` and a `created_at_ticks` column with an index to `conversation_events`, filled
  from existing rows. Run `migrate` as usual.
- Retention starts five minutes after the API starts. With the defaults it removes anonymous
  visitors not seen for 90 days and accounts left unconfirmed for 30 days. Set
  `Retention:VisitorInactiveDays` or `Retention:UnconfirmedAccountDays` to `0` before upgrading
  to keep them.

## [0.1.0] - 2026-11-23

First public release. Delivery records for each phase are in [docs/releases](docs/releases).

### Added

- **Platform:**
  - ASP.NET Core (.NET 10) API with a React/TypeScript web app.
  - SQLite or PostgreSQL with separate migrations and an explicit `migrate` command.
  - Health endpoints.
  - RFC 9457 errors with stable codes.
  - English and Spanish.
  - OpenAPI document and Swagger UI in Development.
- **Setup wizard:** database, email, first administrator and options in the browser, protected by a
  setup code from the server log.
- **Accounts:**
  - Registration with email confirmation (SMTP or Postmark).
  - Password reset.
  - Google and Microsoft sign-in.
  - Cloudflare Turnstile.
  - Revocable sessions, lock-out, rate limits and CSRF protection.
- **Organizations and contacts:** invitations, roles, workspaces, contacts with requests and blocking,
  privacy-preserving people search.
- **Realtime:** SignalR presence, typing indicators and live updates across devices.
- **Conversations and messaging:**
  - Direct and group conversations, with history from each member's join.
  - Replies, edits, deletions, idempotent sends.
  - Delivered/read receipts and unread counts.
  - A transactional outbox for at-least-once delivery.
- **Attachments:**
  - Content-checked files on disk or in S3-compatible storage.
  - Optional ClamAV scanning and quarantine.
  - Cleanup of abandoned uploads.
- **Private chats:** one-to-one, in memory only, ending with the initiator's session and leaving
  nothing behind.
- **Chat-first web app:** navigation rail and tab bar, notifications, desktop notifications,
  System/Light/Dark themes.
- **Administration:**
  - Live settings and provider configuration.
  - User and organization management.
  - Administrator and Auditor roles.
  - An audit log.
- **Website chat:**
  - Embeddable widget (`widget.js`) in an isolated frame, with origin restrictions.
  - Anonymous or server-signed identified visitors (HS256), with visitor rate limits.
  - React package `@audorix/chatx-embed`.
- **Business communication:**
  - Public and private channels in workspaces.
  - Teams.
  - Guests limited to single channels.
  - Organization-specific settings.
  - A support inbox with agents, assignment and open/closed status.
- **Deployment:**
  - Docker images.
  - Development Compose profiles (SQLite, PostgreSQL with Mailpit).
  - A production Compose profile behind a TLS reverse proxy.
  - `Network:TrustedProxies` for real client addresses behind proxies.
- **Documentation:**
  - Installation, Docker deployment, configuration, migrations and the API guide with an OpenAPI
    reference.
  - Architecture and ADRs.
  - Contribution guide, security policy and third-party license review.
  - An embedding example.

### Known limitations

- **Single API instance:** realtime connections, rate limits, private chats and widget connections
  live in one process. Scaling out needs a SignalR backplane and shared rate limiting.
- **Not yet available:** email and push notifications, channel archiving, and support SLAs, tags and
  automation.
- **Data deletion:** account deletion and data-retention policies are still to be decided
  (delivered in Phase 13, see Unreleased).

[Unreleased]: https://github.com/audorix/Audorix.ChatX/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/audorix/Audorix.ChatX/releases/tag/v0.1.0
