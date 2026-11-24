# Phase 24 — Throughput: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-24.
Scope: [Implementation, Phase 24](../IMPLEMENTATION.md#phase-24--throughput). Applicable ADRs:
038 (new), 037 (send path), 036 (targets), 022 (ordering).

## Owner decisions of 2026-11-24

| Question | Decision |
| --- | --- |
| What Phase 24 is | Reach 100 messages a second (the go-ahead for the deeper changes Phase 23 named) |
| Message order | Keep strict, gap-free positions per conversation |
| Load testing on the development workstation | Stopped: the full-size runs kept the machine at 100 % and it shut down several times |

## Delivered

| Area | What |
| --- | --- |
| Append lock | `IConversationStore.LockForAppendAsync`: a transaction whose first statement is a no-op update of the conversation's row, then the conversation is re-read; the send takes its position, saves and commits inside it, and publishes after. Concurrent sends to one conversation wait instead of failing. |
| Batched push | `NotificationService.SendPushAsync` handles a batch together: each conversation and its participants once, mentions in one query (`ListMentionedUserIdsAsync` for many messages), presence, subscriptions and authors once per batch. Same rules as before (thread followers, mentions, "nothing" level, website assignment, nobody who is online). |
| Tests | `Simultaneous_sends_to_one_group_all_succeed_with_gap_free_positions` (both databases): 45 sends from three people at once, all accepted, positions contiguous. |
| Documentation | ADR-038, Implementation Phase 24, changelog. |

## Measurements

**Where the limit was (before this phase's changes).** A 100 messages/s run with CPU sampled per
process showed the workstation at **98–99 % for the whole run**: the API about 37 %, PostgreSQL about
16 %, the rest the load-test client (2,000 simulated people receiving about 4,900 messages a second)
and other programs. Above 50 messages a second this machine runs out of processor before the
server's own limits are reached, so it cannot show whether one server handles 100.

**After the changes.** Not measured. A 50 messages/s run was healthy throughout (at most 6 requests
open, every message sent), but the computer shut down while it was writing its report, and its
figures are lost. Creating the 2,000 test accounts alone holds the machine at 100 % for over a
minute (passwords are hashed deliberately slowly). Load testing was stopped at the owner's request.

**What is known:** Phase 23 met every target at 25 and 50 messages a second on this machine (send p95
65 and 80 ms); this phase removes the retries that dominated at higher rates and most of the push
worker's per-message reads. Whether one server reaches 100 must be measured on a dedicated server,
with the load-test client on another machine (see the [testing guide](../development/testing.md#load-test)).

## Validation results

- Build (Release) clean.
- Integration tests run in groups, both databases: messaging, threads, mentions, message tools,
  planning, change feed and website chat 52/52; notifications and push (including encrypted
  delivery and removal of ended subscriptions) 15/15; concurrency 6/6 (including the new test).
- The whole suite was not run on this machine in this phase (it is the next heaviest job); it runs
  in CI on GitHub with every push.
- No API change (OpenAPI unchanged); no secrets in the repository.

## Open items

- Measure on a dedicated server, client on a second machine: 50, 75 and 100 messages a second.
- Check the CI run on GitHub for this push (full suite on both databases).
- The load-test tool creates fresh accounts every run; reusing them would make repeated runs much
  lighter.
