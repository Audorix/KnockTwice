# Phase 23 — Send-path speed: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-24.
Scope: [Implementation, Phase 23](../IMPLEMENTATION.md#phase-23--send-path-speed). Applicable ADRs:
037 (new), 036 (targets and baseline), 022 (outbox).

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 23 is | Send-path speed, the bottleneck found by the Phase 22 baseline |
| How far to go | Fix the known costs, measure and record; deeper changes (for example a send queue) only with the owner's go-ahead |

## Delivered

| Area | What |
| --- | --- |
| Describe once | After saving, a new message is described once (`ConversationService.DescribeNewAsync`: no reactions, pins or thread lookups; files and mentions only when the send added them) and that view is both published and returned. The outbox row is still written with the message and removed after publishing; if publishing fails the dispatcher retries it as before. |
| Session check cache | `SessionCheckCache`: a session validated in the last 15 s is not read again, never past its expiry or when its 5-minute activity write is due. Every revocation through `SessionService` clears it at once. |
| Sequence conflicts | Up to 8 tries (was 3) with a random back-off (5–20 ms, doubling, at most 200 ms) before 409. |
| Tests | Unit tests for the cache (owner, lifetime, revocation, expiry, due activity write). The existing sign-in, sign-out, "sign out other sessions", messaging and realtime tests cover the behaviour. |
| Documentation | ADR-037, Implementation Phase 23, changelog. |

## Statements per send

Captured on an idle server (PostgreSQL, one plain text message to a group of three, SQL and request
logging on):

| | Before (Phase 22) | After |
| --- | --- | --- |
| Statements inside the request | 22 | 8 |
| Request time | 56 ms | 25 ms |

The 8: membership, conversation, organization membership, duplicate check, the save (one batch),
the sender's profile, the recipients, removing the outbox row.

## Load test

Same machine and setup as the [Phase 22 baseline](phase-22-performance.md#load-test-baseline)
(one shared 8-thread workstation running the API, PostgreSQL and the client), 2,000 people in **one
organization**, groups of 50, 5 s warm-up, 60 s measured. Phase 22 figures are the closest earlier run.

| Messages/s | Result | Send p50 / p95 | History p95 | Delivery p95 | Errors | Phase 22 |
| --- | --- | --- | --- | --- | --- | --- |
| 25 | **All targets met** | 33 / 65 ms | 48 ms | 52 ms | 0 | send p95 404 ms, history p95 214 ms |
| 50 | **All targets met** | 44 / 80 ms | 64 ms | 67 ms | 0 | 20.8/s achieved, send p95 39 s |
| 100 | **Overloaded**: 42.8/s achieved | 22.9 s / 37.3 s | 34.9 s | 55.0 s | 758 × 409, 20 hubs dropped | 5/s achieved, send p95 57 s |

At 25 and 50 messages a second every target holds with large margins (send p95 80 ms against
300). The limit on this machine now lies between 50 and 100 messages a second; above it requests
queue and the sequence conflicts again exhaust their retries. The target of 100 is **not met yet**.

## Validation results

- .NET (Release, SQLite and PostgreSQL): 459 of 460 in the full run (6 min 58 s). The one failure,
  `PrivateChatTests.A_private_chat_lives_with_the_initiators_session_and_leaves_nothing_behind`,
  timed out waiting 10 s for an event under the suite's load; it passed 3 of 3 runs on its own
  (all three private-chat tests each time). Sign-out ends private chats directly, not through the
  session cache.
- New unit tests 2/2. No web change in this phase; no API change (OpenAPI unchanged).
- The load-test tool now writes its report before closing its connections (four runs had been lost
  when the session ended while 2,000 connections were closing).
- No secrets in the repository.

## Open items

- Reach 100 messages a second: the next costs are the per-conversation sequence row (every send in a
  group updates it, so busy groups serialize), the background push and search queues (work per
  message), and measuring on a dedicated server with the client on another machine.
- Deeper changes (for example a send queue) need the owner's go-ahead.
- With several API instances a revoked session can be accepted elsewhere for up to 15 s.
