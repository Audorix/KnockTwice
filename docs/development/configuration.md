# Configuration reference (Phase 1)

Settings use standard ASP.NET Core configuration: `appsettings.json`, then
`appsettings.{Environment}.json`, then user secrets (Development), then **the settings file
written by the setup wizard**, then environment variables (`Section__Key`), then command-line
arguments. Environment variables and arguments therefore always override what the wizard wrote.
**Secrets belong in environment variables, user secrets, a secret store or the wizard's settings
file (which is outside version control) — never in committed files.**

All settings are validated at startup. Invalid values stop the process with exit code `2` and a
message naming the setting; connection strings and passwords are never echoed.

## `Setup` (first-run wizard)

| Key | Default | Notes |
| --- | --- | --- |
| `Setup:Completed` | `false` | Until true, the API runs in **setup mode**: only `/health/*` and `/api/v1/setup/*` are served, everything else under `/api` answers 503 `setup_required`. The wizard writes `true` into the settings file. Operator commands (`migrate`, `grant-role`) always use the normal host. |
| `Setup:SettingsPath` | `App_Data/chatx.settings.json` | Settings file the wizard writes, absolute or relative to the content root. Docker profiles use `/app/data/chatx.settings.json` in the data volume. Created owner-read/write only on Unix; it contains database and SMTP passwords. |
| `Setup:Token` | empty | Fixed setup code (12+ characters) for automated installs. When empty, a random code is generated at each start of setup mode and printed to the log; whoever can read the server log can run setup. |
| `Setup:RestartAfterCompletion` | `true` | After a successful setup the process restarts in normal mode, reading the new file. |

The wizard writes `Setup:*`, `Database:*` (unless the database is configured by environment
variables or command-line arguments, which the wizard then shows as locked), `Email:*`,
`App:PublicBaseUrl`, `Localization:DefaultLanguage`, `Captcha:*` and the development-mode switches
`Identity:RequireConfirmedEmail` / `Identity:AutoConfirmEmail`. It validates the combined settings
with the same validators as startup, migrates the database and creates the first installation
administrator before it writes anything. It refuses a database that already has an administrator
(409 `setup_administrator_exists`). Delete the settings file to run setup again.

## `Database`

| Key | Required | Values | Notes |
| --- | --- | --- | --- |
| `Database:Provider` | yes | `Sqlite`, `PostgreSql` (case-insensitive) | No default and no fallback. Normally written by the setup wizard. |
| `Database:MigrateOnStartup` | no | `true` / `false` (default `false`; `true` in `appsettings.Development.json`) | Applies pending migrations before the API serves requests. Without it the API logs an error naming the pending migrations and readiness stays "not ready" until `migrate` runs. Keep it off in production and run `migrate` as an explicit deployment step. |
| `Database:ConnectionString` | yes | provider connection string | SQLite: must name a file (`Data Source=...`); in-memory is rejected. A relative path is resolved against the API content root. PostgreSQL: must include `Host` and `Database`. |

Examples:

```text
Database__Provider=Sqlite
Database__ConnectionString=Data Source=/var/lib/chatx/chatx.db

Database__Provider=PostgreSql
Database__ConnectionString=Host=db.internal;Port=5432;Database=chatx;Username=chatx;Password=<secret>
```

PostgreSQL is the production-oriented provider. SQLite is the lightweight single-instance
provider; do not run several API instances against one SQLite file.

## `Localization`

| Key | Default | Notes |
| --- | --- | --- |
| `Localization:DefaultLanguage` | `en` | Must be one of the supported languages. Also used as the installation record's initial default. |
| `Localization:SupportedLanguages` | all available (`en`, `es`) | May only list languages that ship resources. |

API responses are localized from the `Accept-Language` header (also `?culture=es` or the
standard ASP.NET Core culture cookie). Regional tags fall back to the language (`es-MX` → `es`).

## `App`

| Key | Required | Notes |
| --- | --- | --- |
| `App:PublicBaseUrl` | yes | Absolute http(s) URL where users open the web app (for example `https://chat.example.org`). Used for email links (`/verify-email`, `/reset-password`, `/sign-in`) and OAuth return redirects. Development: `http://localhost:5173`. |

## `Identity` (accounts and sessions)

| Key | Default | Notes |
| --- | --- | --- |
| `Identity:AllowRegistration` | `true` | `false` blocks local registration *and* first-time external sign-in (403 `registration_disabled`). |
| `Identity:RequireConfirmedEmail` | `true` | Password sign-in is refused (403 `email_not_confirmed`) until the emailed link is used. Turn off only for closed installs. |
| `Identity:AutoConfirmEmail` | `false` | **Development mode** (set by the wizard together with `RequireConfirmedEmail=false`): registration creates confirmed accounts and sends no email at all. Logged as a startup warning. Never use it on an installation others can reach. |
| `Identity:PasswordMinimumLength` | `10` | 8–128. No composition rules (NIST SP 800-63B); at least 4 distinct characters; 128 characters maximum. |
| `Identity:MaxFailedAccessAttempts` | `5` | Failed password attempts before lock-out. Locked accounts get the same `invalid_credentials` response. |
| `Identity:LockoutDuration` | `00:15:00` | |
| `Identity:SessionLifetime` | `12:00:00` | Idle lifetime of a sign-in without "keep me signed in" (sliding). |
| `Identity:PersistentSessionLifetime` | `14.00:00:00` | Idle lifetime with "keep me signed in" (sliding). |
| `Identity:TokenLifetime` | `1.00:00:00` | Email confirmation and password reset links (max 7 days). |

Browser sessions use an HttpOnly, `SameSite=Lax` cookie named `chatx.auth` that is `Secure` outside
Development. The cookie only references a server-side session (`user_sessions` table), so sessions can
be listed and revoked, and password resets/changes end other sessions immediately. Every
POST/PUT/PATCH/DELETE under `/api/v1/` must carry the antiforgery token from `GET /api/v1/auth/csrf`
in the `X-CSRF-TOKEN` header. Data Protection keys (which protect cookies and tokens) are stored in
the database table `data_protection_keys`, so restarts do not sign users out.

The setup wizard creates the first installation administrator. Further administrators are
assigned from the command line after they have registered:

```powershell
dotnet run --project src/Api/Audorix.KnockTwice.Api --launch-profile chatx -- grant-role admin@example.org InstallationAdministrator
```

## `Email`

| Key | Default | Notes |
| --- | --- | --- |
| `Email:Provider` | `Smtp` | `Smtp` or `Postmark`. |
| `Email:FromAddress` / `Email:FromName` | `no-reply@chatx.localhost` / `Knock Twice` | Use an address your provider is allowed to send from. |
| `Email:Smtp:Host` / `Port` | `localhost` / `1025` | Defaults target a local development server such as Mailpit. |
| `Email:Smtp:Security` | `None` | `None`, `StartTls`, `StartTlsWhenAvailable`, `SslOnConnect`, `Auto`. Use TLS in production. |
| `Email:Smtp:Username` / `Password` | empty | Set both or neither. **Secret.** |
| `Email:Postmark:ServerToken` | empty | Required when the provider is `Postmark`. **Secret** (`Email__Postmark__ServerToken`). |
| `Email:Postmark:MessageStream` | `outbound` | Postmark transactional stream. Open and link tracking are disabled. |

Message bodies and tokens are never logged; delivery failures return 503 `email_unavailable`.

## `Captcha`

| Key | Default | Notes |
| --- | --- | --- |
| `Captcha:Provider` | `Turnstile` | `Turnstile` (Cloudflare) or `None`. `None` disables challenges and logs a startup warning; use it only for offline development or tests. |
| `Captcha:Turnstile:SiteKey` | empty (test key in Development) | Public key rendered by the browser widget. |
| `Captcha:Turnstile:SecretKey` | empty (test key in Development) | **Secret** (`Captcha__Turnstile__SecretKey`). |
| `Captcha:Turnstile:AllowedHostnames` | empty | Optional extra check of the hostname the challenge was solved on. |

Challenges protect registration, password-reset requests and confirmation-link resends; sign-in is
protected by lock-out and rate limiting instead. Verification fails closed: if Cloudflare cannot be
reached, the request is rejected with 503 `captcha_unavailable`. `appsettings.Development.json` uses
Cloudflare's published always-pass test keys, which still require internet access to Cloudflare.

## `Authentication` (external sign-in)

| Key | Notes |
| --- | --- |
| `Authentication:Google:ClientId` / `ClientSecret` | Enables "Continue with Google" when both are set. Redirect URI: `{public origin}/api/v1/auth/external/signin-google`. |
| `Authentication:Microsoft:ClientId` / `ClientSecret` | Enables "Continue with Microsoft". Redirect URI: `{public origin}/api/v1/auth/external/signin-microsoft`. |

A first external sign-in creates an account (if registration is allowed) with the provider's email
marked confirmed. An existing account with the same email is **never** linked automatically; the user
is asked to sign in with their password instead.

## `Organizations`

| Key | Default | Notes |
| --- | --- | --- |
| `Organizations:AllowCreation` | `true` | Whether any signed-in user may create organizations (403 `organization_creation_disabled` otherwise). |
| `Organizations:DefaultWorkspaceName` | `General` | Name of the workspace created with every organization (1–80 characters). |

Members join only by accepting an invitation addressed to their confirmed email address.
Invitations expire after 14 days and never reveal whether an address has an account.

## `Conversations`

| Key | Default | Notes |
| --- | --- | --- |
| `Conversations:MaxGroupParticipants` | `256` | Upper bound for active participants of a group, including the owner (2–10000). |

## `Messaging`

| Key | Default | Notes |
| --- | --- | --- |
| `Messaging:EditWindow` | `00:15:00` | How long authors may edit their own messages (0–7 days). Deleting has no time limit. |
| `Messaging:MaxLength` | `4000` | Maximum message length in characters (plain text). |
| `Messaging:EveryoneMentionLimit` | `20` | Phase 17: in groups and channels with more participants than this, only owners and administrators may use `@everyone` / `@here` (0–100000; 0 = always only them). |
| `Messaging:MaxMentions` | `50` | Phase 17: most people one message may mention by name (1–1000). |
| `Messaging:OutboxPollInterval` | `00:00:01` | How often the transactional outbox is checked for notifications that were not published right after their commit. |
| `Messaging:OutboxRetryDelay` | `00:00:03` | Delay before an unpublished notification becomes due, and the base of the retry backoff. |
| `Messaging:OutboxMaxAttempts` | `10` | After this many failed attempts a notification is dropped; clients still see the message in history. |

## `DemoData`

| Key | Default | Notes |
| --- | --- | --- |
| `DemoData:Enabled` | `false` (`true` in `appsettings.Development.json`; the setup wizard writes `true` in development mode) | Allows installation administrators to run **Populate demo data** (home page, or `POST /api/v1/admin/demo-data`, permission `installation.demo_data`). It adds 300 people (`*@demo.chatx.test`, shared password `Demo123!`, set directly and exempt from the password policy), 50 companies with workspaces, about 960 contacts, 150 groups, about 330 direct conversations, 800 channels in the companies' workspaces (a fifth of them private; everyone, the caller included, is in at least 10), about 24,000 messages over the last 30 days (10,000 in groups and direct conversations, 4,000 in channels, about 10,000 that @mention people) and activity: mentions, saved messages, reminders and scheduled messages, 500 items for the caller and 50–100 for every demo person (mentions of the last two days stay unread). It includes the caller (10 companies, about 60 contacts, part of the groups and channels). Idempotent: it only adds what is missing and never re-adds someone who left. **Clear demo data** (`DELETE /api/v1/admin/demo-data`) removes only records flagged as demo data (`is_demo` on accounts, organizations and conversations): demo people with what they wrote anywhere, demo organizations and demo conversations with their contents; real people's conversations and data stay (only their memberships in demo organizations and groups go). Never enable it on an installation others can reach. |
| `DemoData:AvatarsPath` | empty (`../../../Demo/avatars` in `appsettings.Development.json`: the 300 pictures in the repository's `Demo/avatars`) | Folder of profile pictures for demo people: `avatar-001.jpg` goes to the first demo person, up to `avatar-300.jpg` (`.jpeg`, `.png` and `.webp` also work; at most 1 MB each). Relative paths are resolved from the API's content root. Every load gives a picture to demo people who have none, so it also works on data loaded earlier; people with a photo keep it. Clearing demo data deletes these photos. Empty or missing folder: demo people show initials. |

## `Discovery`

| Key | Default | Notes |
| --- | --- | --- |
| `Discovery:AllowEmailLookup` | `true` | `GET /api/v1/users/search?q=<exact email>` finds one confirmed account. |
| `Discovery:AllowOrganizationDirectory` | `true` | Name search (`q` without `@`) covers only people sharing an active organization with the caller. |

Search results never include email addresses, exclude the caller and anyone involved in a block with
the caller, and are capped at 20.

## `RateLimiting`

| Key | Default | Notes |
| --- | --- | --- |
| `RateLimiting:AuthPermitLimit` | `20` | Requests per client IP per window on login, registration, recovery, confirmation and external sign-in endpoints (429 `too_many_requests`). |
| `RateLimiting:AuthWindow` | `00:01:00` | |

Behind a reverse proxy all clients share the proxy's IP unless `Network:TrustedProxies` lists it
(see [`Network`](#network)); set it before relying on per-IP limits in production.

## `OpenApi`

| Key | Default | Notes |
| --- | --- | --- |
| `OpenApi:Enabled` | `false` (`true` in `appsettings.Development.json`) | Serves `GET /openapi/v1.json` (generated by `Microsoft.AspNetCore.OpenApi`) and Swagger UI at `/swagger` (Swashbuckle UI package only), also in setup mode, where the document contains only the setup API. Both are off unless enabled. The `chatx` launch profile opens `/swagger`. |

## `Logging`

Production uses the JSON console formatter (structured, UTC timestamps); Development uses
single-line text. EF Core command logging is at `Warning`, and sensitive-data logging is never
enabled. Standard `Logging:LogLevel:*` keys apply.

## `Realtime`

| Key | Default | Notes |
| --- | --- | --- |
| `Realtime:PresenceOfflineGrace` | `00:00:05` | A user stays "online" this long after their last connection closes, so reloads and short network drops cause no offline/online flicker (0–5 minutes). |
| `Realtime:TypingTimeout` | `00:00:06` | A typing indicator ends this long after the last "typing" signal unless renewed. |
| `Realtime:SweepInterval` | `00:00:01` | How often expired typing indicators and offline transitions are published. |
| `Realtime:SessionCheckInterval` | `00:00:30` | How often open hub connections are checked against their sign-in session. Revoked or expired sessions are disconnected; sign-out and session revocation through the API disconnect immediately, and every hub call re-checks the session. |

The hub is `/hubs/chat` (SignalR). It requires the `chatx.auth` cookie and accepts browser
connections only from the API's own origin or `App:PublicBaseUrl` (cross-site WebSocket
protection; no CORS is configured). Presence and typing state are kept **in memory on one API
instance**; several instances need a SignalR backplane and shared presence first (ADR-016).

## `Attachments` (Phase 7)

| Key | Default | Notes |
| --- | --- | --- |
| `Attachments:MaxFileSize` | `26214400` (25 MB) | Per file, in bytes (1 KB – 5 GB). Enforced while the upload streams in (413 `attachment_too_large`). Keep a reverse proxy's body limit in line (the Docker nginx allows 26 MB). |
| `Attachments:MaxPerMessage` | `10` | Attachments per message (1–50). |
| `Attachments:UserQuota` | `1073741824` (1 GB) | Total stored bytes per person (413 `attachment_quota_exceeded`); `0` = unlimited. Deleting a message with files frees its space. |
| `Attachments:AllowedExtensions` | `[]` = `jpg jpeg png gif webp pdf docx xlsx pptx txt csv zip` | A subset of the supported types. Anything else, and every active type (HTML, SVG, scripts, executables), is refused. The content signature must match the extension; the declared type must match or be generic. |
| `Attachments:AbandonedAfter` | `1.00:00:00` | Uploads never sent with a message are deleted after this long. |
| `Attachments:PendingTimeout` | `01:00:00` | Uploads whose bytes never finished arriving are cleaned up after this long. |
| `Attachments:CleanupInterval` | `00:01:00` | How often the cleanup job runs (abandoned uploads, retried content removal, rescans of quarantined files). |

Files are private: every download (`GET /api/v1/attachments/{id}/content`) is authorized through
the conversation and message, is served with `Content-Disposition` (only JPEG/PNG/GIF/WebP inline),
`nosniff`, a sandboxing CSP and `Cache-Control: private, no-cache`. There are no public or signed URLs.

## `Storage` (Phase 7, ADR-007)

| Key | Default | Notes |
| --- | --- | --- |
| `Storage:Provider` | `Local` | `Local` or `S3`. |
| `Storage:Local:Path` | `App_Data/attachments` | Relative to the content root, like the SQLite file; Docker uses `/app/data/attachments` in the data volume. Never inside a web root. Back it up together with the database. |
| `Storage:S3:Endpoint` | | Any S3-compatible service, e.g. `https://s3.eu-west-1.amazonaws.com`, `http://minio:9000`, an R2 endpoint. |
| `Storage:S3:Region` | `us-east-1` | Signing region (Signature V4). |
| `Storage:S3:Bucket` | | Must be **private**; ChatX never sets ACLs and never hands out object URLs. |
| `Storage:S3:AccessKeyId`, `Storage:S3:SecretAccessKey` | | Secrets: environment variables or a secret store only. The key needs `PutObject`, `GetObject` and `DeleteObject` on the bucket (or prefix). |
| `Storage:S3:ForcePathStyle` | `true` | Path-style URLs (`endpoint/bucket/key`), needed by most self-hosted services; `false` uses `bucket.endpoint`. |
| `Storage:S3:Prefix` | | Optional key prefix inside the bucket. |
| `Storage:S3:Timeout` | `00:01:40` | Per request. |

## `Scanning` (malware, Phase 7)

| Key | Default | Notes |
| --- | --- | --- |
| `Scanning:Provider` | `None` | `None` or `ClamAv`. With `ClamAv`, uploads stay **quarantined** (not sendable, not downloadable) until clamd answers "clean"; while it is unreachable they stay quarantined and are rescanned by the cleanup job; infected files are rejected and deleted. Private-chat files are refused while the scanner is unavailable. |
| `Scanning:ClamAv:Host`, `Scanning:ClamAv:Port` | `localhost`, `3310` | A clamd daemon (`INSTREAM`). Its `StreamMaxLength` must be at least `Attachments:MaxFileSize`. |
| `Scanning:ClamAv:Timeout` | `00:00:30` | Per scan. |

## `PrivateChats` (Phase 7, ADR-025)

| Key | Default | Notes |
| --- | --- | --- |
| `PrivateChats:Enabled` | `true` | Allows one-to-one private chats that are never saved. |
| `PrivateChats:InvitationTimeout` | `00:10:00` | An invitation not accepted in time ends the chat. |
| `PrivateChats:MaxMessages` | `1000` | Messages kept in memory per chat; older ones are forgotten (and their files wiped). |
| `PrivateChats:MaxFileSize` | `10485760` (10 MB) | Per file in a private chat. Same allowed types as `Attachments:AllowedExtensions`. |
| `PrivateChats:MaxBytesPerChat` | `52428800` (50 MB) | Memory budget for files per chat. |
| `PrivateChats:MaxActivePerUser` | `5` | Invited or active private chats per person. |
| `PrivateChats:SweepInterval` | `00:00:05` | How often expired invitations and ended initiator sessions are checked. |

Private chats live only in the memory of one API process: a restart ends them all, and several
API instances would need sticky sessions or a shared ephemeral store first.

## `Widgets` (website chat, Phase 10, ADR-024)

Widgets themselves (title, colour, allowed website addresses, who answers, anonymous visitors, on/off)
are configured per organization in the web app (**Organizations → an organization → Website chat**).
These keys set installation-wide policy.

| Key | Default | Notes |
| --- | --- | --- |
| `Widgets:Enabled` | `true` | Live setting. `false` switches every website chat off: configuration and sessions answer 404/401 and the embed page may not be framed. Existing conversations stay with the organizations. |
| `Widgets:SessionLifetime` | `30.00:00:00` | A visitor session lasts this long and is renewed while it is used (5 minutes – 365 days). |
| `Widgets:SessionTouchInterval` | `01:00:00` | The renewal is written at most this often. |
| `Widgets:IdentityTokenMaxLifetime` | `01:00:00` | Longest allowed validity (`exp − iat`) of an identity token signed by a website (1 minute – 1 day). |
| `Widgets:AnonymousSessionsPerHourPerAddress` | `5` | New anonymous sessions per client address and hour (429 `too_many_requests`). Sessions start only when a visitor opens the chat. |
| `Widgets:MessagesPerMinute` | `20` | Messages per visitor session and minute. |

Visitors are identified for a website by a JWT its **server** signs: HS256 with the widget's signing
secret (shown once when the widget is created or the secret is replaced), claims `sub` (the
website's own user id), optional `name` and `email`, `aud` = the widget id, `iat` and `exp`. The page
passes it with `ChatXWidget.identify(token)` (or the React package's `identityToken` prop) and calls
`ChatXWidget.logout()` when its user signs out. Never put the secret in browser code.

The limits are kept in the API process (one instance, ADR-016). Behind a reverse proxy, set
`Network:TrustedProxies` so the per-address limit sees real client addresses.

## `Network`

| Key | Default | Notes |
| --- | --- | --- |
| `Network:TrustedProxies` | empty | IP addresses or CIDR networks of reverse proxies whose `X-Forwarded-For`/`X-Forwarded-Proto` are believed. The chain is followed backwards while each hop is listed, so a TLS proxy in front of the bundled nginx works when both are listed. Empty: forwarded headers are ignored and every request counts under the address that connected. List only proxies that all traffic passes through; anyone who can reach the API directly from a listed network could claim any address. Example (environment): `Network__TrustedProxies__0=172.18.0.0/16`. |

## Live settings and the administration (Phase 9)

Configuration sources, lowest to highest precedence: `appsettings*.json` → the installation
settings file → **live settings** (database table `installation_settings`, edited in
**Admin → Settings**) → environment variables → command-line arguments. A key set by the
environment or command line is shown as locked in the administration and cannot be changed there.
Live settings apply to the next request (services read them per request) and every API instance
reloads them every 30 seconds.

Editable live: `Identity:RegistrationMode`, `Organizations:AllowCreation`,
`Discovery:AllowEmailLookup`, `Discovery:AllowOrganizationDirectory`, `PrivateChats:Enabled`,
`Attachments:Enabled`, `Widgets:Enabled`, `Attachments:MaxFileSize`, `Attachments:MaxPerMessage`,
`Attachments:UserQuota`, `Attachments:AllowedExtensions`, `Messaging:MaxLength`,
`Messaging:EditWindow`, `Conversations:MaxGroupParticipants`, `PrivateChats:MaxFileSize`,
`PrivateChats:MaxBytesPerChat`, `Localization:DefaultLanguage`, `Localization:SupportedLanguages`,
`Audit:RetentionDays`, `Retention:VisitorInactiveDays`, `Retention:UnconfirmedAccountDays`,
`Retention:MessageDays`, `Search:Enabled`, `Notifications:EmailEnabled`, `Notifications:PushEnabled`,
`Notifications:EmailDelay`, `Notifications:EmailInterval`. Each save is validated with the same rules as startup (also across
settings, e.g. a private chat's memory budget must hold its largest file).

**Admin → Providers** edits `Email`, `Captcha`, `Storage` and `Scanning` in the installation
settings file (`Setup:SettingsPath`) after the startup validation; they apply after a restart.
Secret values are write-only and never returned. "Test" tries the candidate values without saving
(email to the administrator, a storage probe object, a scanner sample).

| Key | Default | Notes |
| --- | --- | --- |
| `Identity:RegistrationMode` | `open` | `open`, `invitation` (only addresses with an open organization invitation) or `closed`. `Identity:AllowRegistration=false` still means closed. |
| `Organizations:AllowCreation` | `true` | `false`: only installation administrators create organizations. |
| `Attachments:Enabled` | `true` | `false`: no new uploads (regular or private chats); existing files stay readable. |
| `Audit:RetentionDays` | `365` | Audit records older than this are deleted (checked every 6 hours; 7–3650). |

## Data retention (Phase 13)

Applied every six hours (first run five minutes after start); what was removed is logged and
recorded in the audit log as `retention.applied`. `0` switches a rule off. All three are live
settings.

| Key | Default | Notes |
| --- | --- | --- |
| `Retention:VisitorInactiveDays` | `90` | Anonymous website visitors not seen for this many days are deleted with their sessions. Their conversations stay with the organization, the visitor shown as "Deleted user". Identified visitors (signed by your website) are kept. 0–3650. |
| `Retention:UnconfirmedAccountDays` | `30` | Accounts whose email address was never confirmed are removed after this many days; the address can register again. Only while `Identity:RequireConfirmedEmail` is on (otherwise such accounts are usable). 0–3650. |
| `Retention:MessageDays` | `0` (off) | Messages older than this many days are deleted with their attachment files (file content first; a message whose file cannot be removed waits for the next run). Replies keep their own text and lose the quote. Other history entries (joins, renames) stay. 0–36500. |
| `Retention:ClosedSessionDays` | `30` | Phase 21: sign-in sessions that expired or were signed out more than this many days ago are deleted, with the push subscriptions of those browsers. 0 = keep. 0–3650. |

## Several API instances (Phase 21)

| Key | Default | Meaning |
| --- | --- | --- |
| `ScaleOut:Redis` | empty (single server) | A StackExchange.Redis connection string, for example `redis:6379,password=…,ssl=true`. When set: SignalR Redis backplane, presence shared between instances, shared sign-in and visitor rate limits, and a `redis` readiness check. Requires a restart. |
| `ScaleOut:Prefix` | `chatx` | Prefix of every Redis key and channel (letters, digits, `-`, `_`), so several installations can share one Redis. |

With several instances behind a load balancer: enable WebSockets on the balancer, use the same
database, Data Protection keys (already in the database) and settings everywhere, and either run
one instance for private chats or switch `PrivateChats:Enabled` off (private chats live in one
instance's memory; a startup warning reminds you).

## Drafts, scheduled messages and reminders (Phase 20)

| Key | Default | Meaning |
| --- | --- | --- |
| `Planning:DispatchInterval` | `00:00:15` | How often due scheduled messages and reminders are processed (100 ms – 10 min). Drafts older than 30 days are removed hourly. |

## Notifications (Phase 15)

| Key | Default | Notes |
| --- | --- | --- |
| `Notifications:EmailEnabled` | `true` | Live. Emails about unread messages; each person chooses batched (default), daily or off under **Account → Preferences**. Needs a working email provider. |
| `Notifications:EmailDelay` | `00:15:00` | Live. A message is emailed only if still unread this long after it arrived (and the person is not connected). |
| `Notifications:EmailInterval` | `01:00:00` | Live. At most one notification email per person in this interval ("batched"); "daily" waits 24 hours. |
| `Notifications:PushEnabled` | `true` | Live. Web Push to subscribed browsers while the person has ChatX closed. The VAPID key pair is created on first use and stored in the database (private key protected by Data Protection). |
| `Notifications:PushContact` | `App:PublicBaseUrl` | The VAPID subject: a `mailto:` or `https:` contact the browser vendors' push services can reach. |
| `Notifications:DispatchInterval` | `00:00:10` | How often new messages are pushed (emails are checked once a minute). |

Push needs HTTPS (or `localhost`) and the service worker at `/sw.js`, which the web build and the
nginx configuration serve from the site root.

## Message search (Phase 14)

| Key | Default | Notes |
| --- | --- | --- |
| `Search:Enabled` | `true` | Live setting. `false`: the search API answers `403 search_disabled`; indexing continues so switching it back on needs nothing else. |
| `Search:Provider` | `Database` | `Database` (SQLite FTS5 / PostgreSQL full-text, inside the application database) or `OpenSearch`. Settings file (**Admin → Providers → Message search**) or environment; restart required. After switching, rebuild the index (**Admin → Providers → Search index**). |
| `Search:IndexInterval` | `00:00:02` | How often queued message changes are applied to the index (100 ms – 10 min). |
| `Search:OpenSearch:Url` | — | Cluster URL, for example `https://search.internal:9200`. Required with the OpenSearch provider. |
| `Search:OpenSearch:IndexName` | `chatx-messages` | Lower-case index name, one per installation. Created on first start with its mapping; every message is then indexed. |
| `Search:OpenSearch:Username` / `Password` | — | Basic authentication (OpenSearch security plugin); set both or neither. The password is a secret: environment variable or settings file only. |
| `Search:OpenSearch:Timeout` | `00:00:10` | Per request. |

OpenSearch receives the text of every message: reach it over TLS on a private network and protect
it like the database. It is not part of the database backup, and can always be rebuilt.

Account and organization deletion have no settings; see [installation.md](../installation.md#deleting-accounts-and-data-retention).

## Reserved for later phases

The `Features` section shown in Implementation §6 is **not read** yet
(registration is controlled by `Identity:AllowRegistration`). They will be added, validated and enforced together with the features they control, so
no setting can appear to enable functionality that does not exist.

## Frontend

| Variable | Where | Notes |
| --- | --- | --- |
| `CHATX_DEV_API_URL` | Vite dev server environment | Proxy target for `/api`, `/hubs` (including WebSockets), `/health`, `/openapi`. Default `http://localhost:5080`. |
| `VITE_API_BASE_URL` | build time | Optional absolute API origin. Leave unset for same-origin deployments (recommended). |

The UI language preference is stored in the browser under the `chatx.language` key.
