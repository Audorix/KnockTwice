# Phase 22 — Performance: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Scope: [Implementation, Phase 22](../IMPLEMENTATION.md#phase-22--performance). Applicable ADRs:
036 (new), 022 (change feed completed), 016 and 035 (one server by default).

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 22 is | Performance: reconnect catch-up, web code splitting, conversation list paging, load testing |
| Capacity targets | "Mid-size", one server: 2,000 connected people, 100 messages a second, p95 send < 300 ms, p95 history page < 200 ms, no errors |
| Reconnect catch-up | Up to 500 changes; more, or after 24 hours, the client reloads |
| Presence (after the first baseline) | Contacts and people sharing a direct chat or group only; organization membership alone no longer shows presence |

## Delivered

| Area | What |
| --- | --- |
| Change feed | `change_log` table written in the same transaction as every new or changed history entry and every reaction change; `GET /api/v1/changes/cursor` and `GET /api/v1/changes?since=` (current state of changed entries the caller may still see, touched conversations, next cursor; `reset` over 500 of the caller's changes or past the 24-hour retention); purge with the hourly planning cleanup. Migration `AddChangeLog` (both providers). |
| Web catch-up | The open conversation keeps a cursor; after a reconnect it applies the changed entries and refreshes the conversation instead of reloading its history; it reloads on `reset` or a cursor older than 24 hours. |
| Code splitting | Pages other than conversations, home and sign-in load on demand (`React.lazy`); the SignalR client loads when realtime starts; the channel sign moved out of the channels page. First download 182.1 → 167.0 kB gzip. `npm run check:bundle` (budget 180 kB first download, 80 kB per chunk) runs in CI. |
| Conversation list | `GET /api/v1/conversations/page?limit=&after=` (50 by default, at most 200), same order as the full list; profiles are looked up for the page only. The web app shows "Show more conversations" and keeps what is shown on refresh. |
| Presence audience | Contacts plus people sharing an active direct chat or group, minus blocks (was: everyone sharing an organization). Channels and website chats do not count. The realtime presence test now checks that an organization colleague without a shared chat sees nothing. |
| Load test | `tools/Audorix.ChatX.LoadTest`: creates people, organizations and groups through the public API, connects everyone over WebSockets, sends messages and reads history at fixed rates, measures send, history and per-recipient delivery latency and errors, and prints PASS/MISS per target (exit code 2 on a miss). See the [testing guide](../development/testing.md#load-test). |
| Test speed | PostgreSQL integration tests now use a small connection pool per test database (they opened a new server process for every statement): the change-feed tests went from 44 s to 8 s, and a 501-message test from over 20 minutes to 28 s. |
| Documentation | ADR-036 (and the register entry for capacity targets), Implementation Phase 22, API guide (change feed, list paging), testing guide (bundle budget, load test), changelog, OpenAPI. |

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| Changes made while offline appear after a reconnect; only for people who may see them; over 500 asks for a reload | **Pass** (both databases; web test with a simulated reconnect). The 500 count is per person: someone else's busy conversation never forces a reload. |
| First download within budget, enforced in CI | **Pass**: 167.0 kB of 180 kB; largest chunk 63.7 kB of 80 kB. CI step written, not yet run on GitHub. |
| Pages of the list equal the full list, in order | **Pass** (both databases; web test for "Show more conversations"). |
| The load test runs at the targets and the results are recorded | **Recorded below — the targets are not met on the machine available.** |

## Load-test baseline

**Machine:** one Windows 11 workstation with 8 logical processors and 32 GB, running the API
(Release, `Production`, `Warning` logging), PostgreSQL 18 (default settings, `max_connections`
100), the load-test client, and the developer's own tools (IDE, another API instance, security
software that inspects network traffic). Client and server on one machine is the worst case for
both; treat these numbers as a floor, not as the server's capacity.

All runs: 2,000 people, groups of 50, history pages of 50 at the stated rate, 10 s warm-up.

| Run | Organizations | Messages/s | Result |
| --- | --- | --- | --- |
| A | one of 2,000 | 100 | **Collapsed while connecting.** No send succeeded; every hub connection dropped; the database stopped accepting connections in time. This run also used a pool of 200, above PostgreSQL's limit of 100, so it is not a clean measurement; it was not repeated. |
| B | 40 of 50 | 100 | **Overloaded.** 4.6 messages/s measured, send p95 57.6 s, 7,824 failed sends (1,809 sequence conflicts returned as 409, the rest client time-outs and back-pressure); all hubs dropped by the end. |
| C | 40 of 50 | 25 | **Stable, latency just over target.** 25/s sustained, all 2,000 connected throughout, no errors, every message delivered. Send p50 206 / p95 404 ms; history p50 112 / p95 214 ms; delivery p50 146 / p95 285 ms. Server ≈ 14 % of the machine, client ≈ 6 %. |
| E — after the presence change | one of 2,000 | 100 | **No longer collapses.** Connecting everyone took 27 s, as with small organizations, and all 2,000 connected. The server then overloaded exactly like run B: 5 messages/s measured, send p95 57 s, 1,951 sequence conflicts (409), hubs dropped by the end. |
| F — after the presence change | one of 2,000 | 25 | **Incomplete.** The 90-second run finished with only 1–3 requests ever open (fewer than run C), but the computer shut down before the tool printed its figures, and a rerun was cut off too. Repeat it. |
| D | 40 of 50 | 50 | **Over the knee.** 20.8 messages/s achieved, 1,985 sequence conflicts (409), send p95 39 s; connections held. The machine was 75–87 % busy, of which the server and client ≈ 23 % and PostgreSQL ≈ 10 %; the rest went to other processes. |

Setup for 2,000 people took about 2 minutes (sign-up and sign-in hash passwords), organizations and
groups 27 s, connecting 28 s.

### What the runs show

1. **Presence fan-out grew with the square of an organization's size.** Every connection was
   announced to everyone sharing an organization; in a 2,000-person organization, connecting
   everyone was about 4 million events. **Resolved in this phase** (owner decision): presence now
   goes to contacts and chat partners only, and run E shows a 2,000-person organization connecting
   as quickly as small ones.
2. **On this machine one server sustains about 20–25 messages a second with 2,000 connected
   people**, not 100. At 25/s the latency is close to the targets (send p95 404 ms against 300).
   Above that, sends queue and the per-conversation sequence conflicts exhaust their three retries
   and reach clients as 409.
3. **Every API request reads its session, user and roles from the database**, and a send loads the
   message's reactions, mentions, pins and attachments to describe it: about 15 statements per send.
   Caching the authentication lookups and describing a new message without re-reading it are the
   obvious first optimizations.
4. **Send latency grows with the number of people connected** (median 47 ms with 2 connected, 71 ms
   with 50, 206 ms with 2,000) while the machine is mostly idle, so part of the cost is waiting, not
   work. Where it waits was not established here.

## Validation results

- .NET (Release, SQLite and PostgreSQL), after the presence change: **458 of 458 passed**, none
  skipped, in 6 min 2 s. Before it: 458 of 458 in 6 min 24 s for the
  whole solution (with pooled PostgreSQL test databases; Phase 21's run took several times longer,
  with one test alone at 6½ minutes).
- Web tests 81/81; type-check clean; bundle check passes (167.0 kB).
- OpenAPI re-exported from the release build: three new paths (`/api/v1/changes/cursor`,
  `/api/v1/changes`, `/api/v1/conversations/page`), nothing removed.
- No secrets in the repository; the load test takes no credentials (it creates its own accounts).

## Open items

- Repeat run F (one organization, 25 messages/s) to get its figures.
- Optimize the send path (findings 2–4) and rerun runs B–E; measure on a dedicated server with the
  client on another machine before treating any number as the installation's capacity.
- Run the CI workflow on GitHub (bundle step included).
- The change feed does not carry receipts, membership or conversation settings; clients reload those.
- The paged list still reads all of a person's memberships before paging.
