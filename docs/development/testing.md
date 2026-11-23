# Testing and validation

## One command

```powershell
./scripts/validate.ps1
```

```bash
./scripts/validate.sh
```

This restores, builds (Release) and tests the .NET solution, then runs `npm ci`, type-check,
frontend tests and the production build. No CI provider is configured in this repository yet;
the script is the reproducible check to run locally or to call from CI later.

## .NET test projects

`dotnet test` uses Microsoft.Testing.Platform (selected in `global.json`):

```powershell
dotnet test --solution Audorix.ChatX.slnx
```

| Project | What it proves |
| --- | --- |
| `Audorix.ChatX.UnitTests` | Domain invariants of the installation record; database and localization configuration validation, including that failure messages never echo credentials; setup code generation and verification, the settings file format, wizard connection-string building and administrator input validation; realtime state (presence across devices and grace period, typing renewal and expiry, leave/revoke, timer validation). |
| `Audorix.ChatX.ArchitectureTests` | The documented project graph, package restrictions, compiled-assembly references and that Contracts expose no domain/persistence types. |
| `Audorix.ChatX.IntegrationTests` | Real SQLite and PostgreSQL: migrations, write/read after reopening, idempotent re-migration, single-row constraint, provider-specific migration discovery, no model/snapshot drift; API health (including database outage), problem responses, OpenAPI/Swagger exposure; the real executable's `migrate` command and invalid-configuration exit code. **Phase 2 (`AuthFlowTests`)**: register → confirm → sign in → profile/preferences → sign out and password reset on both providers; change password, session listing/revocation and ownership, CSRF enforcement, no account enumeration, captcha failure/unavailability, lock-out, permission policy, disabled registration, external-provider challenge, email failure and rate limiting. Email and captcha ports are in-memory fakes there; their real adapters are unit-tested against a recording HTTP handler. **Phase 3 (`OrganizationTests`, `ContactTests`)**: invitation-based joining and role enforcement, last-owner rule and organization isolation (every cross-organization read and write → 404) on both providers; composite-FK rejection of cross-organization workspace membership and the one-pending-request-per-pair index on both providers; workspace visibility; email lookup vs. organization-scoped name search; contact requests, crossing requests, removal; blocking (contact removed, requests closed, blocked user sees "not found"). **Setup wizard (`SetupTests`, `CommandLineTests`)**: the real executable starts in setup mode and logs a setup code; setup mode serves only health and the setup API; the setup code is required (case- and separator-insensitive); invalid input is reported per field and writes nothing (no database file, no settings file); unreachable PostgreSQL is reported without host or password; a complete setup on SQLite **and** a PostgreSQL database the wizard creates migrates, creates a confirmed administrator, writes the settings file (no setup code, no administrator password) and the normal host then starts from that file alone, signs the administrator in with the administrator role, and — in development mode — registers new accounts confirmed without email; a database that already has an administrator is refused; a database configured by environment is locked. **Phase 4 (`RealtimeTests`)**, through real SignalR connections: anonymous and cross-origin hub negotiation refused; presence delivered only to contacts and organization colleagues (both providers), once per user across devices, offline only after the last device and the grace period; a reconnect within the grace period produces no events; conversations require access (protected fixture `FixtureConversationAccess`, since conversations arrive in Phase 5), rejoining is idempotent, typing renewals are silent and indicators expire; revoked access stops delivery to an open connection and is re-checked on use; sign-out and "sign out other sessions" close exactly the affected connections. **Phase 5 (`ConversationTests`, both providers)**: direct conversations unique per pair and scope, only between contacts or organization members, never across a block; groups add only contacts or members; history visible from join, paginated in consistent ascending pages without gaps; unread counts and forward-only read positions; roles (administrator vs owner rights, last owner, succession); removal and leaving end access; leaving an organization ends its conversations permanently; 12 concurrent writers get unique contiguous sequences; the database rejects duplicate direct keys and reused sequence positions; a removed participant's open hub connection stops receiving events and gets `removed`. **Phase 6 (`MessagingTests`, both providers)**: idempotent sends (retry returns the original, a different body conflicts, six concurrent identical sends store one message); body validation; only authors edit, within the edit window; tombstones; owners/administrators delete others' messages in groups, members cannot; reply previews and replies that cannot quote another conversation (application and composite foreign key); forward-only delivered/read watermarks with read implying delivered; realtime events only to participants who may see the message (a later joiner gets no events about earlier messages, a removed member none at all), receipts to the others; messages saved before publication and published later by the outbox dispatcher when the transport fails; direct chats refuse messages across a block; unique client ids in the database. **Demo data (`DemoDataTests`, both providers)**: the first load creates 300 people, 50 companies, 150 groups, 500 channels and 12,500 messages, the second nothing, departures are respected, demo accounts sign in with `Demo123!`; clearing leaves the real people's data exactly as it was (row counts before loading and after clearing are equal) and a second clear removes nothing; administrator-only, loading only where enabled. |

EF Core's in-memory provider is deliberately not used.

**Phase 13 (`AccountDeletionTests`, both providers):** account deletion refused for a sole
organization owner and the last administrator, password re-confirmation, sign-out, erased account
(no email, password or credentials), the address free to register again, contacts and memberships
gone; history kept with "Deleted user", group owner succession, direct conversations closed to new
messages; organization deletion by owners only, with file content removed from storage;
administrator deletion and its audit records; retention removing an inactive anonymous visitor, a
never confirmed account and expired messages with their files, and nothing when nothing is due.

**Phase 14 (`SearchTests`, both providers; `SearchRulesTests`):** prefix, accent- and case-blind
matching; only the caller's conversations and history from their join; attachment file names and
every filter; cursor paging; edits, deletions, leaving a group and message retention reaching the
index; the live switch; administrator rebuild and its permission. Unit tests cover text
normalization, the OpenSearch query (visibility filter, sorting, `search_after`), index creation,
bulk requests, partial bulk failures and outages, with a recording HTTP handler — not a real
OpenSearch cluster.

**Phase 15 (`NotificationTests`, both providers; `WebPushTests`):** unread-message emails batched
once per interval, the author and people with email off excluded, no text without previews, the
daily choice and validation; Web Push through a fake push service that decrypts the payload
(VAPID header, `aes128gcm`, title, body and link), no text without previews, gone subscriptions
dropped, nothing after the browser's sign-in ended. Unit tests check encryption against the RFC 8291
example, a browser-side decryption, the VAPID signature and subscription validation. Not covered: a
real browser vendor push service.

**Phase 16 (`ReactionsAndThreadsTests`, both providers):** reactions added once per person and emoji,
removed again, shown in history; text, several emoji and empty values refused; the 20-emoji limit
(an emoji already present still accepted); no access from outside the conversation. Threads: hidden
and echoed replies, no nested threads, the main timeline and its unread count without hidden replies,
the thread summary (count, following, unread), thread listing and read position, deletion lowering
the count, stop following. The web test covers the reaction picker, chips and the thread panel with
"Also send to the conversation".

**Phase 17 (`MentionsTests`, both providers; `MentionTextTests`):** named mentions on the message and
in the Mentions view, unread until the conversation is read; outsiders refused; `@everyone` refused
for members above the limit (lowered to 2 in the test) and accepted for the owner, reaching everyone;
`@everyone` inside an address and self-mentions ignored; deletion and leaving remove mentions from
the view. Levels: `{ muted: true }` means "mentions", invalid levels refused, a person on "only
mentions" is emailed just the message mentioning them, a person on "nothing" not at all. Unit tests
cover broadcast detection and the level rules; web tests the composer suggestions, the level
selector and the Mentions page.

**Phase 18 (`MessageToolsTests`, both providers):** pins by either person in a direct conversation
(idempotent, recorded once in history, `pinnedBy`), refused for group members and accepted for the
owner, removed by unpinning and by deleting the message, invisible to outsiders; saved messages
private to their owner, ordered and paged, removed by unsaving, deletion and leaving; forwarding a
message with a photo: comment then copy, credited to the original author, with its own readable file
copy, idempotent retry, refused into a conversation the caller is not in. Web tests cover the More
menu (save, pin, forward dialog), the pins list, opening at a linked message, the Saved tab, and
search results linking to the message.

**Phase 19 (`MessageTextTests`; web `richText.test.tsx`):** plain text without markers (bold, italic,
strike, code, quotes, links with their address, code blocks kept verbatim, arithmetic and
`snake_case` untouched, unsafe links left as text) and bounded one-line excerpts; the renderer's
formatting, blocks, HTML never interpreted, only safe links clickable with `noopener noreferrer`,
mention highlighting; the composer's shortcut and toolbar and a formatted sent message.

**Phase 20 (`PlanningTests`, both providers):** drafts per conversation and thread, private, removed
when emptied, length-checked; scheduled messages refused outside the next 30 days, edited and
cancelled (not by others), one with a file sent on time as its author with the same file, one in a
group the author left kept as `failed` with `conversation_not_found`; a reminder refused in the past,
private, due on time with an email (plain text, link to the message) and listed as `due`, removed when
done. Web tests: draft restored with its reply target and saved after typing, "Send later" tomorrow
9:00, "Remind me" in an hour, the Scheduled and Reminders tabs, quick times.

**Phase 21 (`SessionRetentionTests`, `ScaleOutTests`, both providers; `ScaleOutSettingsTests`):** ended
sessions purged after the retention period while the active one keeps working; two API instances over
one database with a shared in-memory stand-in for Redis: presence known across instances, no repeated
"online" and no false "offline" while connected elsewhere, offline once gone from both; sign-in limits
counted across both instances. Unit tests check the scale-out settings. Redis itself is not exercised
(no Redis server in the test environment); a startup check confirmed the API starts with Redis
configured but unreachable and reports it in readiness.

**CI** (`.github/workflows/ci.yml`) runs the same steps as `scripts/validate.ps1` on GitHub Actions, with a
PostgreSQL service container.

## Restore drill

`scripts/restore-drill.ps1` backs up and restores a throwaway installation (SQLite, or PostgreSQL
with `CHATX_TEST_POSTGRES` and the PostgreSQL client tools) and checks sign-in, contacts, history
and attachment content after the restore. See [backup-restore.md](../backup-restore.md#practising-a-restore).

## Load test

`tools/Audorix.ChatX.LoadTest` (Phase 22, ADR-036) measures an installation against the agreed
targets for one server: 2,000 connected people, 100 messages a second, p95 send under 300 ms, p95
history page under 200 ms, delivery to each recipient p95 under 1 s, and no errors. It uses only the
public API and the hub, creating its own people (`lt<run>-<n>@loadtest.example.org`), one
organization and groups of 50, so it can be pointed at any **test** installation. Never run it
against a production server: it creates thousands of accounts.

The installation must be in development mode with registration open: `Identity:AutoConfirmEmail=true`,
`Captcha:Provider=None`, and `RateLimiting:AuthPermitLimit` raised for the setup (for example
`100000`). For a realistic number, run the server in Release with `Production` logging, on its own
database, and the tool on a different machine if you can.

```powershell
dotnet run -c Release --project tools/Audorix.ChatX.LoadTest -- --url http://localhost:5190 --out results.json
```

Options change the scale and the targets (`--users`, `--group-size`, `--rate`, `--history-rate`,
`--duration`, `--warmup`, `--send-p95`, `--history-p95`, `--delivery-p95`; `--help` lists them).
The tool prints p50/p95/p99/max per operation and a PASS/MISS line per target, and exits `0` when
every target is met, `2` when one is missed and `1` when the run could not complete. The measured
baseline is in the [Phase 22 delivery record](../releases/phase-22-performance.md).

## PostgreSQL integration tests

They run only when `CHATX_TEST_POSTGRES` holds a connection string for a login that may
`CREATE DATABASE`. Each test creates a database named `chatx_test_<guid>` and drops it
afterwards (`DROP DATABASE ... WITH (FORCE)`); no other database is touched. Each test database
uses a small connection pool (at most 4), so parallel tests stay well under PostgreSQL's default
limit of 100 connections; keep other heavy users of the same server (such as a load-test
instance) stopped while the tests run. Without the
variable these tests are reported as **skipped**, never as passed.

A dedicated test login is recommended, for example (run as a PostgreSQL superuser):

```sql
CREATE ROLE chatx_test LOGIN CREATEDB PASSWORD '<choose a password>';
```

```powershell
$env:CHATX_TEST_POSTGRES = "Host=localhost;Port=5432;Username=chatx_test;Password=<password>;Database=postgres"
dotnet test --solution Audorix.ChatX.slnx
```

## Frontend

```powershell
cd web
npm ci
npm run typecheck
npm test
npm run build
npm run check:bundle
```

`check:bundle` (Phase 22, also in CI) measures the built app: the first download (entry script and
the chunks `index.html` preloads) must stay under 180 kB gzip and every chunk under 80 kB. Pages
other than conversations, home and sign-in are loaded on demand; when the check fails, look for a
page imported directly instead of through `lazy` in `AppShell.tsx`, or a small helper imported from
a big page module.

Vitest covers the CSRF-aware API client (token reuse, one refresh on `csrf_token_invalid`, field
errors), sign-in and registration behaviour (localized errors, unconfirmed-email path, account
language applied after sign-in, field-level validation, safe return URLs), readiness state mapping (available / not ready / unreachable / cancellation),
the status card's loading, failure and keyboard retry behaviour, language persistence and
fallback, Spanish plural rules, and that English and Spanish resources have identical keys. The
setup wizard is tested through its gate (application vs. wizard), a wrong setup code, a full walk
through every step that checks the request sent with the setup code, and returning to the step a
server error belongs to. The realtime client is tested with a fake hub: presence snapshot and
events, rejoin plus presence reload after a reconnect without duplicates, typing only for joined
conversations, dropping revoked conversations, retry with backoff, and error-code parsing. Account
deletion (Phase 13) is tested for the list of organizations to hand over, the password confirmation
with a field error and the sign-out, and deleted people get a localized name in every API response.
