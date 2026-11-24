# UI redesign (2026-11): delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-24. Requested by the project
owner before Phase 25.

## Owner decisions of 2026-11-24

| Question | Decision |
| --- | --- |
| Reference | A commercial chat template the owner holds a licence for, used **only as visual inspiration**: no code, styles, class names, images, icons or fonts copied (it is not open source). |
| Accent colour | Keep our teal (the template's violet would make the product look like it) |
| Layout | Full-height panels: rail, list and conversation fill the window, told apart by tone |
| Scope | The whole app: tokens, shell, chat, sign-in pages, every other page through the shared tokens |

## Delivered

| Area | What |
| --- | --- |
| Tokens (`styles/theme.css`) | Light: cool lavender-grey canvas, white working surfaces, filled inputs. Dark: layered slate greys (rail, list and chat one step apart, never pure black). Teal accent kept (`#0f766e`, `#3cc4b4` in dark); bubble colours with their own tokens so white text stays readable in both themes. 15 px base text, softer borders and shadows, 0.75 rem radius. |
| Design layer (`styles/design.css`, new) | All layout and component rules of the redesign, written for this project and commented. |
| Chat | Edge-to-edge list and conversation on wide screens; teal bubbles for other people, soft grey for your own, small time line inside the bubble; reactions and thread summaries under it; history events as small centred pills; a clean composer (filled field, icon attach and send buttons). |
| Message actions | "Reply · React · Reply in thread · More · Edit · Delete" moved from a line of links under every message into a small icon toolbar beside the bubble, shown on hover, tap or keyboard focus (`ToolButton`, our own line icons in `NavIcon`). Buttons keep their words as accessible names. |
| Signed-out pages | The teal side panel became a slim top bar (brand, sign in, create account); pages are centred; language and licence moved to a footer. The home page opens with the product introduction and its two actions; the outdated "Next milestone" card was removed. |
| Phones | Message tools float over the bubble's corner; the formatting toolbar and the keyboard hint are hidden; the history is sized so the composer sits above the tab bar; the top bar fits on one line. |
| Accessibility | Focus moves to the new page without scrolling the top bar away. Focus rings, labels and keyboard access unchanged. |

## Validation

- Checked in the browser on a local throwaway installation with demo data: chat, list, account,
  contacts, home and sign-in, in light and dark, at 1280×800 and 375×812 (phone).
- Web tests 81/81 (unchanged: every action is still found by its name); type-check clean; bundle
  167.5 kB of 180 kB.
- No API or database change.

## Contacts page and sidebar (from the owner's mockup, 2026-11-24)

Owner decisions: the wide sidebar for the whole app; organizations shown from the API; status
Online/Offline only (no "Away" until idle detection exists); "View profile" opens a side panel.

| Area | What |
| --- | --- |
| Sidebar | From 1200 px: brand ("ChatX by Audorix"), Chats · Activity · Search · Channels · Support · Private, a **Workspace** section (Contacts, Organizations, Administration), counts as pills, a bar on the active item, and the signed-in person with connection status and settings at the bottom. 768–1199 px: the icon rail; phones: the bottom tab bar. One markup for all three. |
| Contacts page | Breadcrumb, title and subtitle, "Find people" (focuses the search); "Connect with someone new" search; tabs **All contacts · Requests · Blocked** with counts; filter (name or organization), status (all/online/offline) and sort (A–Z, Z–A, recently added); a table with name, status, shared organizations and a Message button plus a "…" menu (View profile, Remove contact, Block contact); 8/16/32 per page with page numbers. On phones each row becomes a card. |
| Profile panel | A side dialog: avatar, name, status, shared organizations, "contact since", and Message / Remove / Block. Escape, the close button or a click outside close it; focus returns to where it was. |
| API | `GET /api/v1/contacts` items gain `sharedOrganizations` (`[{ id, name }]`, organizations both people are active members of). Additive; OpenAPI re-exported. |

Validation: contacts integration tests 10/10 on SQLite and PostgreSQL (new: shared organizations);
web tests 82/82 (new: filtering, paging, sorting, row menu and profile; `EmbedApp` once timed out in
a parallel run and passed 6/6 twice alone); bundle 169.0 kB of 180 kB. Checked in the browser in
dark and light at 1400 px and on a phone.

## Messages page (from the owner's mockup, 2026-11-24)

Owner decisions: add personal conversation pinning; previews from the API; bubbles like the mockup
(your messages teal on the right, others grey); a Shared files tab.

| Area | What |
| --- | --- |
| List | Title with a "New conversation" button, search, chips **All · Unread · Groups**, **Pinned** and **Recent** sections, rows with avatar, name, time, place and a preview ("You: …", "Ana: …", deleted message, file). The rows scroll on their own so the header stays. |
| Chat header | "#" mark or avatar, title, a stack of participant avatars, search in this conversation (opens Search filtered to it), a "…" menu (pin / unpin). |
| Tabs | **Messages · Shared files**, with the notification level on the right. Shared files: cards with type, size, author and date, download, "load older". |
| Timeline | Day separators (Today / Yesterday / date), an "N new messages" divider at the first unread, name · time above each bubble, a double-check read receipt under your own, typing dots. |
| Composer | One box: text, then a bar with attach, emoji, formatting, and Send joined to a "send later" chevron. |
| API | Summaries gain `pinned` and `lastMessage`; `PUT …/{id}/pinned`; `GET …/{id}/files` (paged, newest first, never older than the caller's join, deleted messages left out). Migration `AddConversationPins` (SQLite and PostgreSQL: `PinnedAtUtc` on participants). OpenAPI re-exported. |

## Activity page (from the owner's mockup, 2026-11-24)

| Area | What |
| --- | --- |
| Header | Breadcrumb, title, subtitle and "Mark all as read" while anything is unread. |
| Tabs | Mentions · Saved messages · Scheduled · Reminders, with icons and counts. |
| Mentions | Search, All/Unread, a conversation filter and newest/oldest order; grouped by day; cards with an unread dot, author, place (and organization or thread), the text with the mention highlighted, file chips, and Open conversation · save · Mark as read. "You're up to date" at the end. |
| Coming up | A side column (below on narrow screens) with the next reminders and scheduled messages. |

No API change for the activity page (existing mentions, saved, scheduled and reminders endpoints).

## Search page (from the owner's mockup, 2026-11-24)

Owner decisions: real totals from the API; a context panel backed by a new API; "Oldest first" added.

| Area | What |
| --- | --- |
| Search card | A large field with clear (×) and Search; Conversation (with the organization when names repeat), From (the conversation's people, or your contacts), From/To date, With files. Active filters as removable chips with "Clear filters"; an (i) button shows the search tips. Dates apply only once the year is complete (no searches for year 0002 while typing). |
| Results | Tabs **All results · Messages · Files** with totals, newest/oldest order, "N results for … in #channel", matching conversations as quick links, cards with avatar (or a file tile in Files), place and organization, highlighted words (text and file names), file chips, "View in conversation" and a save bookmark; "Showing 1–N of M" and "Load more". |
| Message context | The selected result with up to three messages before and after, day separators, the match highlighted, files to download, and "Open conversation". A side column from 1200 px (the first result is selected there); a slide-over that takes focus (Escape closes) below. |
| Entry | `/search?conversation=<id>` (the search button in a conversation header) starts filtered to that conversation. |
| API | `GET /api/v1/search`: `order=newest|oldest`; first page `counts { total, withFiles }` (database: one `COUNT` query; OpenSearch: `size 0` with `track_total_hits` and a `hasFiles` filter aggregation); results carry the caller's `saved`. New `GET /api/v1/conversations/{id}/messages/{messageId}/context?size=` (0–20, default 3; never before the caller's join). OpenAPI re-exported. |

Validation: search integration tests on SQLite and PostgreSQL (new: counts with and without filters,
oldest-first paging, saved state, context with neighbours, join boundary, validation), OpenSearch query
unit test (order, count query); web tests 84/84 (new: tabs with totals, context panel, saving,
order, filter from a conversation link and clearing); bundle 173.1 kB of 180 kB. Checked in the
browser in dark and light at 1500 px and on a phone (375 px, no sideways scrolling).

Validation: conversation, contacts and list-page integration tests on SQLite and PostgreSQL (new:
last message, deleted last message, pinning, shared-files paging and deletion); web tests 83/83;
type check clean; bundle 172.5 kB of 180 kB (largest chunk 63.7 kB). Checked in the browser in dark
and light at 1500 px and on a phone (375 px, no sideways scrolling).

## Channels page (from the owner's mockup, 2026-11-24)

Owner decisions: add channel archiving (the mockup's Archived tab); "New channel" in a side panel.

| Area | What |
| --- | --- |
| Header | Breadcrumb, title, subtitle and **New channel** (disabled until you belong to the workspace). Guest invitations stay on top. |
| Scope bar | Organization and Workspace selects in one card with "Channels you can access in this workspace". |
| Tabs | **All channels · Joined · Discover · Archived** with counts (Archived shows its count only when there is one). Arrow keys move between tabs. |
| Tools | "Find a channel…" (name and description), Visibility (all/public/private), sort (recently active, name, most members). |
| Table | Icon tile (# or lock; grey when archived), name with Joined / Not joined / Private / Archived tags, description, members, last activity ("5 minutes ago", "Yesterday", a date), **Open** or **Join channel**, and a "…" menu (open or join, manage, archive or restore, leave). Footer: "Showing N channels" and the private-channel note. Phones: compact two-line rows. |
| New channel | A side panel (modal, Escape closes, focus returns): name, description, public/private as cards, teams. Opens the new channel. |
| Archiving | Managers archive and restore channels. Archived channels are read-only for everyone (history stays; no posting, editing, reacting, joining or adding people); the conversation shows a notice instead of the composer; the history shows "… archived the channel" / "… restored the channel". |
| API | `PUT /api/v1/channels/{id}/archived { archived }`; `archivedAtUtc` on channels and conversation summaries; history kind `archived` (text `archived`/`restored`); `409 channel_archived`. Migration `AddChannelArchiving` (SQLite and PostgreSQL). OpenAPI re-exported. |
| Shared | The "…" row menu moved to `shared/components/RowMenu.tsx` (contacts and channels); new `archive` and `leave` icons. |

Validation: `BusinessTests` on SQLite and PostgreSQL (new: archiving rights, read-only rules, restore),
`PersistenceTests` (no model drift) and `MessagingTests` pass; web tests 85/85 (new: tabs, search,
visibility filter, restore from the menu, the create panel); type check clean; bundle 174.5 kB of
180 kB. Checked in the browser with the demo data in dark and light at 1500 px and on a phone
(375 px, no sideways scrolling).

## Private chats page (from the owner's mockups, 2026-11-24)

| Area | What |
| --- | --- |
| Header | Breadcrumb (Workspace / Private chats / Invitation), title, subtitle and a **Session only** badge. |
| No chat running | A drawn chat-bubble-and-lock illustration, "No private chat is active", **Open conversations** (to the conversation list), the hint "Open a direct chat, then choose Private chat", three facts (one sign-in session, outside chat history, this browser only) and a "Before you start" note. Running chats are listed with a state pill (Active, Waiting, Invitation). |
| Invitation | A card: back link, the person with "Direct conversation" and a state pill (Not sent, Waiting, Invitation), "Before you send the invitation", the rules as rows with icons (worded for the invitee where it differs), the screenshot note, "No messages are sent with this invitation." and Cancel / **Send invitation**; "After sending, you'll wait here for … to accept." The initiator's waiting screen and the invitee's Accept / Decline screen use the same card. |
| Layout | The list and invitation pages now have the normal page padding (only an open private chat uses the full-height chat layout); the sidebar's Private item uses the regular accent instead of purple. The open chat keeps its sealed look. |

No API change.

## Organizations page (from the owner's mockup, 2026-11-24)

| Area | What |
| --- | --- |
| Header | Breadcrumb, title, subtitle and **New organization**. Invitations for you stay on top (Join / Decline). |
| Tabs | **All organizations · Owned by me · Member** with counts. |
| Table | Search, sort (Name A–Z, Z–A, newest), initials tile, name and creation date, your role as a pill (owner highlighted), **Manage** for owners and administrators or **Open**; eight per page with "Showing 1–8 of N organizations". Phones: compact rows. |
| Create | A "Create an organization" panel beside the list (above it on phones): name, hint, "You'll become the owner", "A General workspace is created automatically", Create / Cancel. Open from the start when you belong to no organization. "Looking for an organization? Ask an administrator for an invitation." below it. |
| Shared | `Pagination` moved to `shared/components/Pagination.tsx` (contacts and organizations). |

No API change.

Validation: web tests 88/88 (new: the private chats empty state and list, the redesigned invitation
for both people, the organizations tabs, roles, paging, sorting, search, invitation and the create
panel); type check clean; bundle 176.1 kB of 180 kB. Checked in the browser with the demo data in
dark mode at 1500 px and on a phone (375 px, no sideways scrolling).

## Administration overview (from the owner's mockup, 2026-11-24)

| Area | What |
| --- | --- |
| Header | Breadcrumb, title, subtitle; on the right the environment badge and version (and "Read only" for auditors). |
| Tabs | The existing sections as underlined tabs (Overview, Users, Organizations, Roles, Settings, Providers, Audit log), still following permissions. |
| Overview | Cards for Users (with disabled count), Organizations and Registration, each linking to its section; "Provider configuration" (database shown as SQLite/PostgreSQL, file storage, malware scanning with a state dot) with View provider / Configure links and an amber warning while scanning is off; "Installation details" (environment, version, registration) with links to application and provider settings; shortcuts to Roles & permissions, Settings and Audit log; a note that installation settings apply to all organizations. A link is shown only when the caller may open its section. |
| Other tabs | Unchanged in this step. |

No API change (the existing `GET /api/v1/admin/overview`).

Also on 2026-11-24, the organizations page was aligned further with its mockup: "Organization" under
each name (creation date on hover), the create panel shown beside the list on wide screens, the
caller's own organizations highlighted, and clearer Manage/Open buttons.

Validation: web tests 91/91 (new: the overview with counts, links, providers, the scanning warning,
details and shortcuts, and links hidden without permission); type check clean; bundle 177.4 kB of
180 kB. Checked in the browser with the demo data in dark mode at 1500 px and on a phone (375 px, no
sideways scrolling).

## Administration: users, organizations and roles (from the owner's mockups, 2026-11-24)

| Area | What |
| --- | --- |
| Users | "Users 301" and a subtitle; a filter bar (search, Status: all/enabled/disabled/email not confirmed, Role: all/administrator/auditor/user, Search); a table with avatar, name and email, status (dot; locked out and unconfirmed shown), installation role pills, last sign-in, sessions and organizations; **Manage** opens a details panel (facts, role switches, sign out, confirm/resend, disable or enable, delete); the "…" menu has View details, Manage roles, sign out, confirm email, Disable/Enable account and Delete account (red). Sort Name A–Z/Z–A; 8/25/50 per page with page numbers; "Installation roles apply across all organizations." Auditors see View details only and read-only switches. |
| Organizations | "Organizations 51", search with Search and a sort (name either way, most members); a table with initials tile, name, members, created date and time, and **View members** opening a side panel (rename, member roles, remove, delete organization for administrators; read-only otherwise); paging as for users; "This view includes all organizations in the installation." |
| Roles | "Installation roles" with **Manage assignments** (to Users), the fixed-roles note, role cards (Administrator, Auditor) with member counts and descriptions, a "Permission comparison" table (Allowed / Not granted per permission; demo data marked "Development only"; a separated row showing that no role accesses conversations or message content) and the closing note. |
| API | `GET /api/v1/admin/users`: `filter=enabled`, `role` (a role name or `none`), `sort` (`nameAsc`, `nameDesc`); `GET /api/v1/admin/organizations`: `sort` (`nameAsc`, `nameDesc`, `members`). Unknown sorts are 400. "Newest" was left out: creation dates are stored as text on SQLite and cannot be ordered there. OpenAPI re-exported. |
| Fix | The unread count in the tab title was lost when the app applied the user's language (the i18n title reset ran after it); the title is now rebuilt on language changes too. |

Validation: `AdministrationTests` and `AccountDeletionTests` on SQLite and PostgreSQL (new: status and
role filters, sorting, invalid sort); web tests 93/93 (new: user menu and details panel, separate
filters, organizations panel and sort, the permission comparison); type check clean; bundle 178.5 kB
of 180 kB. Checked in the browser with the demo data in dark mode at 1500 px and on a phone (375 px,
no sideways scrolling).

## Administration: settings, providers and audit log (from the owner's mockups, 2026-11-24)

| Area | What |
| --- | --- |
| Settings | A group list (Access, Features, Limits, Localization, Audit, Retention) with "Find a setting"; it opens on the first group that has settings or on `?group=`. Section cards in two columns (Limits: messages, attachments, private chats); values with units ("members", "MB", "days") and a readable duration or size; locked values show a lock; allowed file types fold away; Reset per setting; "View setting sources" shows where each value comes from. A sticky save bar counts unsaved changes, with Discard and Save changes. |
| Providers | Cards for database, email, CAPTCHA, storage, scanning and search, each with its provider, a state pill and Configure. The panel shows only the selected provider's fields, grouped in sections, with Save configuration and Cancel, and a test card on the right (email: a test to your own address, the last result and a delivery note; none for CAPTCHA). The database panel is read-only (masked connection, "The database answered"). Search adds the index rebuild. A warning while scanning is off links to it. |
| Audit log | Search (action, actor, target) with Search; Action category, Outcome and a date range; Clear filters, Refresh, Newest/Oldest first. A table with time, event title, icon and code, actor avatar and target; a row opens details (action, target type, actor, target id with copy, source address, other details as "key: value"). 8/25/50 per page with page numbers; "Audit retention: N days" with a link to the setting. |
| API | `GET /api/v1/admin/audit`: `from`, `to`, `q` (action, target or actor name), `skip` (returns `total`) and `order=oldest`; an unknown order is 400. OpenAPI re-exported. |
| Bundle | Spanish texts are a separate chunk loaded when Spanish is used; English stays bundled as the fallback. Start-up JavaScript dropped from 182.1 to 159.3 kB gzip (budget 180 kB). |

Validation: `AdministrationTests`, `AccountDeletionTests` and retention tests on SQLite and
PostgreSQL (new: audit skip and total, oldest first, dates, search by actor and action, invalid
order); web tests 95/95 (new: settings groups, units, search and save bar; providers; audit paging,
details, search and filters); type check and build clean. Checked in the browser with the demo data
at 1500 px in dark and light, on a phone (375 px, no sideways scrolling), and in Spanish.

## Support inbox and support demo data (owner request and mockup, 2026-11-24)

| Area | What |
| --- | --- |
| Demo data | 500 support conversations in the ten companies the administrator who loads it belongs to (120 and 80 in the first two), through two widgets per company ("Website chat" and "In-app", switched off, no allowed sites). Customers are identified visitors with stand-in accounts flagged as demo data. About 40% open (recent, the customer waiting; a third unassigned), 15% pending (the team replied last), 45% resolved (thanks and sometimes a goodbye). The administrator and three people per company become support agents; about a quarter of the assigned conversations are the administrator's, with the customer's last messages unread. Internal notes on about a third of the worked conversations. English and Spanish topics; one in ten has no subject, so the first message stands in. Their own seed: everything loaded before stays the same. Clearing removes them with the rest. The counts gain `support`. |
| Page | Breadcrumb, title and subtitle, the organization picker on the right (it opens on the first organization with support conversations); **Open · Pending · Resolved · All** tabs with counts; search, Assignment (anyone, unassigned, assigned), "Assigned to me", Oldest waiting / Newest activity. The list: customer initials, name, time since the last message, an unread dot, subject, preview, channel and assignment chips, "Showing N of M" with Load more. The conversation: subject (editable), customer · channel, Mark resolved (Reopen when resolved), a "…" menu (mark pending or open, edit subject, take, open in chats); a status row with the state, the assignee picker and the start time; day separators, customer messages on the left, the team's on the right, files, internal notes in amber cards and assignment/status lines; a composer with **Reply** and **Internal note** tabs (attach, emoji, bold, italic, list; "Replies are visible to the customer." / "Notes are only visible to your team."). Reading as a participant clears the unread count; replying joins first. One panel at a time below 992 px. |
| API | `pending` status; `GET …/support/inbox` (page, totals per status, previews, search, assignment, sort); staff history, join, subject and internal notes under `/api/v1/support/conversations/{id}`. Migration `AddSupportNotesAndSubjects` (subject column, `support_notes` table) on SQLite and PostgreSQL. OpenAPI re-exported. |

Validation: `BusinessTests` (new: inbox page, totals, title and preview, sorting, paging, assignment,
subject search, staff history, notes kept from the history and the visitor, join, pending and
reopening), `DemoDataTests` (500 support conversations, notes, statuses, assignments, unread;
clearing removes them), `WidgetTests`, `AccountDeletionTests` and `PersistenceTests` on SQLite and
PostgreSQL; web tests 95/95 (the support test now covers tabs, search, notes, replies and status);
type check and build clean; bundle 161.9 kB of 180 kB. Checked in the browser with the demo data at
1600 px in dark and light (a note added live) and on a phone (375 px, no sideways scrolling).

## My profile, photos and links (from the owner's mockups, 2026-11-24)

| Area | What |
| --- | --- |
| Page | Breadcrumb Account / Profile, "My profile", Sign out. A header card: the photo (or initials) with a camera button, name, email with "Email verified", "Visible to people you chat with.", Change photo / Use initials, and "How you appear" (the conversation look). Tabs **Profile · Media & links · Notifications · Security · Sessions** (with the count), kept in `?tab=`. Profile: details (display name, the locked email, the photo row, Save profile), Appearance (System/Light/Dark cards with a check, language), a sessions summary with "View N sessions". Notifications: this device (desktop, previews) and when away (email, push). Security: password (or external sign-in) and account deletion. Sessions: the session list. |
| Photo | "Change profile photo" dialog: a square stage with the circle, thirds grid and move handle; drag (or arrow keys) to reposition, zoom slider with −/+, rotate left/right, Reset; previews at 120, 40 and 24 px; the file's name, type and size; Choose another image (or drop one); JPG, PNG or WebP up to 5 MB; Remove photo; Save photo. The browser crops to a 512 px square (WebP, PNG or JPEG under 1 MB) and uploads only that. The editor is a separate chunk loaded when opened. |
| Everywhere | The shared avatar shows the photo wherever a person appears (lists, conversations, panels, the sidebar), falling back to initials if it cannot load. |
| Media & links | A table: drag handle (also arrow keys), platform with its mark, the address, Contacts / Only me, remove; Add link (up to 10, the next unused platform); "Pending edits", Discard changes, Save links; errors from the server at their row. Profile preview of what contacts see, with "You control visibility". Contacts see the links in the contact profile panel. |
| API | `avatar.photoUrl`; `POST`/`DELETE /api/v1/users/me/photo`; `GET /api/v1/users/{id}/photo` (signed-in people; versioned, cached for good); `GET`/`PUT /api/v1/users/me/links`; `GET /api/v1/users/{id}/links` (contacts only). Photos are checked by signature and stored under random keys; account deletion removes the photo and links. Migration `AddProfilePhotosAndLinks` on SQLite and PostgreSQL. OpenAPI re-exported. |

Validation: `ProfileMediaTests` on SQLite and PostgreSQL (photo types and size, any signed-in
person can read it, cache header, photo address in contacts, versions, removal, erased with the
account; link checks per row, platform hosts, more than ten refused, order, visibility for contacts
and strangers, clearing), plus account deletion, contact, administration, sign-in and persistence
tests; web tests 97/97 (new: header, photo, session count, Use initials; links add, wrong address,
fix, visibility, keyboard reorder, save, preview); account tests follow the new tabs; type check and
build clean; bundle 164.3 kB of 180 kB. Checked in the browser with the review account at 1600 px in
dark and light: a photo uploaded, edited in the dialog (zoom, drag) and saved; links added, a wrong
LinkedIn address reported and fixed, saved; on a phone (375 px, no sideways scrolling).

## Home / welcome page (from the owner's mockups, 2026-11-24)

| Area | What |
| --- | --- |
| Navigation | A **Home** item first in the sidebar (not in the phone tab bar, where the brand mark leads home). |
| Top bar | Breadcrumb Workspace / Home; today's date; **Development tools DEV** for administrators of development installations; the live connection state. |
| Welcome | "Welcome back, name", the intro line, **Open chats** and **New conversation** (opens the new-conversation panel through `?new=1`), drawn speech bubbles and curves. Four shortcuts: Find people, Browse channels, Saved messages, Your profile. |
| Cards | **Recent conversations** (the three most recent: avatar or channel/group tile, name, Direct or the organization, the last message with its author in groups and channels, time, unread count; View all). **Needs your attention** (unread mentions, incoming contact requests with Review opening the requests tab, the next or due reminder with its time and conversation). **Your workspaces** (organization, workspace, Open into the channels page). **Make it yours** while there is no photo or no links. |
| Administration | An "Administration & development · Admin only" row opens the **Development tools** panel: service status, demo data (introduction, "Currently loaded" tiles for people, companies, conversations, messages, activity and support, Populate / Clear) on development installations, and "In this build" with named capabilities; the footer notes it is available only in Development to authorized administrators. |
| Signed out | The product introduction, the service status and "In this build". |

Validation: web tests 99/99 (new: welcome, recent conversations with direct and channel rows,
contact requests, the reminder, workspaces with only joined ones, the profile nudge, no admin row
for others; the development tools panel with status, demo tiles and build list, closing with
Escape); the account, contacts and conversations tests still pass; type check and build clean;
bundle 168.2 kB of 180 kB. Checked in the browser with the demo data at 1600 px in dark and light,
the development tools panel, and on a phone (375 px, no sideways scrolling). No API changes.

## Landing page and sign-in (from the owner's mockups, 2026-11-24)

| Area | What |
| --- | --- |
| Top bar | Brand and "Preview 0.1"; **Features** (a jump to the feature cards) on the landing page, **Back to home** on the account pages; a light/dark switch for signed-out visitors, kept in this browser (`chatx.theme` in local storage; a signed-in account's theme still wins, and signing out returns to the browser's choice); the language; Sign in and Create account on the landing page. |
| Landing | "Open source · Self-hosted", "Your conversations. Your team. Your space." (the last in teal), the lead, Create account (when registration is open) and Sign in, "Open source under the MIT license."; a still, decorative picture of the app (sidebar, channels, messages, a file, the composer) over soft shapes; four feature cards; the service status in one row; "What's included in Preview 0.1" folded open on request. |
| Account pages | Sign-in, registration, recovery and verification share a two-panel card: a dark panel ("A better place to connect.", two message bubbles joined by a loop, Conversations · Channels · Collaboration) and the form. Only the form on narrower screens. Sign-in: "Welcome back", "Sign in to continue to ChatX.", placeholders, a show/hide password button, Sign in with an arrow, Forgot your password?, "New to ChatX? Create an account". |
| Footer | Brand, "Open source. Made for connection.", English / Español, "MIT license." (the account pages leave out the brand). |

Validation: web tests 101/101 (new: the landing page with its features, status and folded list;
sign-in heading, the show/hide password button and the forgot link); the existing sign-in,
registration and recovery tests pass on the new card; type check and build clean; bundle 171.0 kB
of 180 kB (the landing page is part of the first download, so the budget is getting closer).
Checked in the browser at 1600 px in light and dark (landing, sign-in, registration) and on a phone
(375 px, no sideways scrolling). No API changes.

## Open items

- Administration, channels and search were restyled only through the shared tokens; a dedicated
  pass can follow if they need it.
- Internal notes are not yet removed by message retention; they go with their conversation.
- Profile photos are checked by signature but not sent to the malware scanner, and the website
  chat widget still shows initials for the team (visitors cannot read photos).
- The template folder (`Demo/`) stays out of the repository (it is in `.gitignore`).
