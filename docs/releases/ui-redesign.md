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

## Open items

- Administration, channels, search and the support inbox were restyled only through the shared
  tokens; a dedicated pass can follow if they need it.
- The template folder (`Demo/`) stays out of the repository (it is in `.gitignore`).
