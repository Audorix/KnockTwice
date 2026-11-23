# Phase 21 — Release readiness: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Scope: [Implementation, Phase 21](../IMPLEMENTATION.md#phase-21--release-readiness). Applicable ADRs:
035 (new), 016 (single node, now optional), 025 (private chats), 027 (retention).

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 21 is | Release readiness (after the status audit), not another feature |
| Which items | CI and session purge; reconnect catch-up, web performance and load testing left for later |
| Scale-out | An optional Redis backplane: off by default; when configured, SignalR backplane, shared presence and shared rate limits |

## Delivered

| Area | What |
| --- | --- |
| Session purge | `Retention:ClosedSessionDays` (30, live setting): ended sessions and those browsers' push subscriptions are deleted by the retention job; counted in `retention.applied`. |
| Scale-out | `IClusterPresence` (`LocalClusterPresence` / `RedisClusterPresence`), `PresenceDirectory` used by notifications, reminders and `@here`; cluster-aware online/offline announcements and presence snapshots with a 20 s heartbeat; `IRateCounter` (`LocalRateCounter` / `RedisRateCounter`) behind visitor limits and the sign-in rate-limit policy; SignalR Redis backplane; `redis` readiness check; startup notice and private-chat warning. Settings `ScaleOut:Redis`, `ScaleOut:Prefix`. |
| CI | `.github/workflows/ci.yml`: .NET restore, Release build and tests with a PostgreSQL 17 service; web `npm ci`, type-check, tests and build. |
| Documentation | ADR-035, Implementation Phase 21, configuration (scale-out, session retention), testing guide, changelog. |

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| Without Redis nothing changes; with Redis instances share presence and limits | **Pass** for the instance behaviour (SQLite and PostgreSQL, two instances, in-memory stand-in for Redis); the existing realtime and sign-in tests pass unchanged. **Not verified against a real Redis** (none available here); startup with an unreachable Redis was checked. |
| Ended sessions purged, active ones kept | **Pass** (both databases). |
| CI runs the validation steps | Written; **not yet run** — it runs on the first push to GitHub. |

## Validation results

- Full .NET run (Release, both databases): 450 of 452. Both failures were PostgreSQL tests under the
  run's heavy load (one took 6½ minutes): a realtime presence test timed out waiting for an event,
  and a reminder test's 5-second margin was outrun by the request. Rerun on their own, the realtime,
  planning and scale-out tests passed 19/19; the planning tests now use 20-second margins and passed
  6/6 again.
- Web tests 79/79 (a run during the .NET suite timed out in six unrelated tests; rerun alone, all
  passed); type-check clean.
- Startup with `ScaleOut:Redis` set but unreachable: the API starts, `/health/ready` reports `redis`
  unhealthy, the private-chat warning is logged.
- No API change (OpenAPI unchanged); no secrets in the repository. The CI workflow has not run yet.

## Open items

- Run the CI workflow on GitHub and fix anything Linux-specific (for example letter case in paths).
- Try two instances with a real Redis behind a load balancer.
- Still open from the audit: reconnect catch-up (ADR-022), web code splitting, load testing and
  capacity targets, the Docker profiles, real push / S3 / OpenSearch / ClamAV checks.
- **Commit the work**: the repository still has only its initial commit.
