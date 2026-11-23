# Phase 10 — Embedded website chat: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Applicable ADRs: 005, 006, 007, 011, 016, 018, 021, 023, **024 (accepted with this phase)**.

## Gate decisions (Implementation §12.2), owner answers of 2026-11-23

| Gate | Decision |
| --- | --- |
| Widget isolation | Iframe plus a small loader script (`widget.js`); ADR-024 accepted. |
| Visitor identity | The host website's **server** signs an HS256 JWT with the widget's signing secret; the browser never sees the secret. Anonymous visitors are allowed per widget. |
| Who answers | Organization members chosen per widget ("responders"); they answer in their normal chat list. |
| Visitor limits ("balanced") | Anonymous sessions last 30 days and slide while used; one conversation per visitor; 20 messages per minute per session; 5 new anonymous sessions per hour per address; attachments for identified visitors only. |

## Delivered behaviour

**For organizations** (owners and administrators, *Organizations → an organization → Website chat*):

- Create widgets; each has a name, the title visitors see, an optional welcome message, an accent
  colour, a corner (bottom right/left), a language (visitor's browser, English or Spanish), whether
  anonymous visitors are allowed, the exact website addresses allowed to show it (up to 20, e.g.
  `https://www.example.org`; paths and wildcards are refused), and the members who answer.
- A widget can only be switched on with an allowed address and a responder; a refused change leaves
  the widget unchanged.
- The signing secret is shown once on creation and when replaced; replacing it invalidates tokens
  signed with the old one at once. It is stored protected with the installation's Data Protection keys.
- The page shows the embed snippet and how to sign identity tokens.
- `Widgets:Enabled` is a live setting (Admin → Settings → Features).

**For websites:**

- One script tag: `<script src="https://chat.example.org/widget.js" data-widget-id="…" async></script>`,
  or the JavaScript API (`ChatXWidget.init/identify/logout/open/close/toggle/destroy/on`), or the
  React package `web/packages/chat-embed` (`<ChatXWidget baseUrl widgetId identityToken … />`).
- The loader only creates a sandboxed iframe and resizes it (launcher, panel, full screen on phones).
  Messages between page and frame are accepted only from the other window and its origin.
- The embed page is the only frameable page: the web server sends `frame-ancestors` with the
  widget's allowed origins (nginx `auth_request` in Docker, a Vite middleware in development);
  unknown or disabled widgets allow no one. Verified in the browser: an unlisted origin's frame is blocked.

**For visitors:**

- A launcher with an unread badge (also reported to the host page), a panel with the title, the
  people answering, the welcome message, history, typing indicator, and a notice that messages are
  kept so the team can answer. Light and dark, keyboard and screen-reader friendly, EN/ES.
- No session is created by a page view: an anonymous session starts when the visitor opens the chat;
  an identified one when the host identifies them. The same token subject continues the same
  conversation (on any device), under the name in the token. `logout()` ends the session.
- Files can be attached by identified visitors (same type checks, limits and scanning as members).
- Realtime through `/hubs/widget`, which carries only the visitor's own conversation events.

**For responders:** website conversations appear in the chat list with a *Website* badge, the
visitor's name and a hint; they reply, edit and delete as in any conversation. Visitors never
appear in people search or organization member lists.

### API

| Endpoint | Who | Notes |
| --- | --- | --- |
| `GET/POST /api/v1/organizations/{orgId}/widgets`, `GET/PUT …/{widgetId}`, `POST …/{widgetId}/secret` | Owners, administrators (cookie + CSRF) | Management; the secret only in create/rotate responses. |
| `GET /api/v1/widget/{id}/config` | Public | Appearance of an enabled widget. |
| `GET /api/v1/widget/{id}/frame-policy` | Public | `X-ChatX-Frame-Ancestors` header and JSON. |
| `POST /api/v1/widget/{id}/sessions` | Public, rate-limited | Anonymous or with `identityToken`; requires an allowed `parentOrigin`. |
| `DELETE /api/v1/widget/visitor/session`, `GET …/conversation`, `POST …/messages`, `POST …/read`, `POST …/delivered`, `POST …/attachments`, `GET …/attachments/{id}/content` | Visitor bearer token | No cookies, no CSRF. |
| `/hubs/widget` | Visitor bearer token (`access_token` query for WebSockets) | `JoinConversation()`, `SendTypingStatus(bool)`; same event contracts as `/hubs/chat`. |

New error codes: `widget_not_found`, `widget_origin_not_allowed`, `widget_identity_required`,
`widget_identity_invalid` (401), `widget_attachments_identified_only`, `widget_no_responders`,
`widget_origins_required`, `widget_responders_required`, `responder_not_member`,
`widget_origin_invalid`, `widget_origins_too_many`, `widget_color_invalid`,
`widget_language_invalid`, `widget_welcome_too_long`, `widget_name_*`, `widget_title_*`,
`position_invalid`, and `too_many_requests` (429) for visitor limits.

### Also in this phase

- `Network:TrustedProxies` (opt-in, default empty): believe `X-Forwarded-For`/`-Proto` from listed
  proxies so per-address limits see real clients. Not set in the Docker profiles because they also
  publish the API port directly.
- MailKit 5 compatibility: `SmtpEmailSender` sends with `SendRequest` (the package had been raised to
  5.0.0 in `Directory.Packages.props`, which no longer compiled against the old call).

## Migration impact

Migration `AddWidgets` for both providers (additive):

- `users.is_visitor`, `conversations.widget_id` (FK `SET NULL`, indexed),
  `conversation_participants.is_visitor`.
- New tables `widgets`, `widget_responders`, `visitors` (unique `(widget_id, external_subject)` and
  `user_id`), `visitor_sessions` (unique `token_hash`).

Existing data is untouched; no widget exists until an organization creates one.

## Validation results

- .NET: **352 tests passed** (320 before this phase) with PostgreSQL (`CHATX_TEST_POSTGRES`) and
  SQLite, Release build, 0 warnings. (One earlier run, made while browser checks and the web tests
  ran on the same machine, timed out once in the PostgreSQL demo-data test; it passed alone and in
  the clean full run.) New: `WidgetTests` (anonymous flow with responders, isolation, hub delivery and
  disconnect when switched off; identity tokens valid/invalid/expired/wrong audience/`alg: none`/too
  long-lived, same subject on two devices, secret rotation, identified uploads; per-address and
  per-session limits; management rules), `WidgetRulesTests` (token verification, origin normalization,
  sliding windows).
- Web: 53 tests (17 files), type-check and production build (two pages). New: embed app (lazy
  session, sending, identified mode and unread reporting, unavailable widget), host bridge message
  checks, `widget.js` loader (framing, resizing, full screen on phones, origin/source checks,
  queued calls), the React package, and the widget administration.
- Browser, scratch instance (API, Vite and a separate "customer website" origin): anonymous visitor
  writes from the customer site, responder sees the *Website* conversation and replies, the reply
  arrives live in the open widget and as an unread badge (also reported to the host page) when
  closed; identified visitor via a server-signed token gets a separate conversation and attachments;
  sign-out clears the widget; frame policy headers correct and an unlisted origin's frame blocked
  (also while the API is unreachable: the embed page then fails closed); full-screen chat on a
  375×812 phone viewport; widget administration renders and saves.

## Open items

- Visitor limits and the widget connection list are process-local (ADR-016); several API instances
  need shared rate limiting and a SignalR backplane.
- Retention of website conversations and visitor records follows the general (still open) account
  deletion and retention decision.
- `web/packages/chat-embed` is not yet an npm workspace member (adding it changes the lockfile);
  build it with `npx tsc -p packages/chat-embed/tsconfig.json` and publish when the owner decides.
- Docker: the nginx `auth_request` configuration was written but not run here (Docker is not
  installed on the build machine); the Vite equivalent was verified in the browser.
- Unread counts for responders include the "started a conversation" system line, as for other
  conversations started by someone else.

## Next slice

Phase 11 — business communication channels, per the [roadmap](../IMPLEMENTATION.md).
