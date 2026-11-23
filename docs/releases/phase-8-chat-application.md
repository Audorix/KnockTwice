# Phase 8 — React chat application: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Applicable ADRs: 009 (localization), 017, 022, 023, 025. Most Phase 8 screens were delivered with
their backend slices (Implementation §12.4: "UI integration should accompany each backend slice");
this phase completes the application around them.

## Gate decisions (Implementation §12.2), owner answers of 2026-11-23

| Gate | Decision |
| --- | --- |
| Layout | Chat-first shell for signed-in users: a navigation rail on wide screens, a bottom tab bar on phones; list and conversation side by side on desktop, one at a time with a back button on narrow screens. The identity panel stays on signed-out pages. |
| Notifications | In-app: unread badge in the navigation, unread count in the browser tab title, toasts for messages outside the open chat; optional desktop notifications (profile setting plus the browser's permission) while ChatX is in the background; muted conversations never notify; message text can be hidden; private chat text is never shown. Email and push remain later phases. |
| Theme | System (default), Light or Dark, saved to the account like the language; signed-out pages follow the system. |

## Delivered behaviour

- **App shell**: rail/tab bar with Chats (unread badge), Private (invitation badge), Contacts,
  Organizations and Profile; realtime status in the rail; focus moves to the new page after
  navigation; conversations and private chats fill the window so only lists and timelines scroll.
- **Mobile**: bottom tab bar respecting safe areas, list-or-conversation switching with a back
  button, timeline and composer sized to the viewport, no horizontal scrolling (checked at
  375 px); in-app toasts and invitations stay clear of the tab bar.
- **History**: older pages load automatically when the reader scrolls near the top (scroll position
  kept), the view follows new messages only when the reader is at the end or sent them, otherwise a
  "New messages" button appears. Off-screen messages are skipped by the browser
  (`content-visibility: auto`) — together with cursor pagination this is the virtualization
  approach; a windowing library was not needed at the current page sizes.
- **Notifications**: `NotificationsProvider` keeps unread totals (muted excluded) from the
  conversation list, refreshed by realtime events; tab title "(N) Audorix.ChatX"; toasts link to
  the conversation; desktop notifications use the Notification API with a per-conversation tag.
- **Preferences** (profile): theme selector, language selector, desktop notification and preview
  switches. API: `PUT /api/v1/users/me/preferences` accepts `theme`, `desktopNotifications`,
  `notificationPreviews` (omitted fields unchanged); `GET /users/me` returns them.
- Already delivered and integrated earlier: sign-in/registration, profile, contacts with presence,
  conversation list, direct and group chat, composer with replies/edits/attachments, typing,
  presence, read receipts, attachment previews, private chats, EN/ES.

## Migration impact

`AddUserPreferences` (both providers): `user_profiles.theme` (default `system`, check constraint),
`desktop_notifications` (default false), `notification_previews` (default true). SQLite rebuilds the
table for the check constraint (handled by EF). Development installations apply it at startup.

## Validation results

| Check | Result | Evidence |
| --- | --- | --- |
| .NET build (Release, warnings as errors) | **Passed** | 0 warnings, 0 errors |
| .NET tests incl. real PostgreSQL 18 | **Passed** — 302 of 302 (6 min 15 s) | Includes the new preferences test (saved across devices, invalid theme, empty request still refused). |
| Frontend | **Passed** — 39 tests, type-check, production build | Shell: unread badge and tab title (muted excluded), toast outside the open chat, none for muted, private text never shown, saved dark theme applied; profile: theme saved, desktop notifications ask the browser first. |
| Acceptance: core messaging works | **Passed** | Existing messaging, attachment and private chat suites unchanged and green; browser check of a conversation with files in the new shell. |
| Acceptance: desktop and mobile usable, no critical mobile layout issues | **Passed** | Browser at desktop width and 375 × 812: rail vs bottom tab bar, list/chat switching with back button, composer above the tab bar, no horizontal scrolling on the profile page. |
| Acceptance: history virtualized where necessary | **Passed** | Cursor pages loaded on scroll with position kept; off-screen messages use `content-visibility: auto`. |
| Acceptance: realtime events update the UI | **Passed** | Badge/title update and toasts from hub events (tests); live updates from earlier phases unchanged. |
| Acceptance: EN/ES complete | **Passed** | Locale key-parity test; all new texts in both languages. |
| Light and dark themes | **Passed** | Saved Dark overrides a light system setting in the browser; System follows the device. |
| Docker profiles | **Not run** | Docker is not installed. |

The navigation label changed from "Account" to "Main navigation" (ES "Navegación principal"), and
the tab bar uses the short label "Chats".

## Open items

- Desktop notifications use the page's Notification API; browsers that require a service worker
  (some mobile browsers) only get the badge and title count. Web push is a later phase.
- No sound; no per-conversation notification levels beyond mute.
- The bundle exceeds Vite's 500 kB warning; code splitting by route is a follow-up.
- The organization and contact pages keep their earlier card layouts inside the new shell.
- Docker profiles not run (Docker is not installed).

## Next slice

Phase 9 — Administration.
