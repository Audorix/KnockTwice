# Phase 28 - Pluggable support routing queue

Support routing now uses a durable database outbox and a selectable queue provider. The database
remains the source of truth for cases and routing events.

- `Database` is the default provider and leases outbox rows directly from SQLite or PostgreSQL.
- `Redis` relays rows to Redis Streams and processes them through a consumer group. Unpublished
  work remains recoverable in the database outbox if Redis is unavailable.
- Processing retries with backoff and moves exhausted work to a dead-letter state. UTC tick columns
  keep lease scheduling queryable on SQLite while preserving the timestamp columns.
- The installation admin page can select a provider, save Redis connection details, and test
  reachability. Provider changes take effect after the API restarts.
- Later queue adapters can be added without moving support case data out of the relational database.

## Validation

- Redis 7.4 Docker integration test: 1/1 passed; verified relay, consumption, and outbox completion.
- Database provider/widget integration test: passed on SQLite.
- Full integration suite: 132 passed, 66 skipped, 0 failed. PostgreSQL tests skipped because
  `CHATX_TEST_POSTGRES` was not configured; PostgreSQL migrations were generated.
- Full unit suite: 269 passed, 0 failed.

Explicit retry/dead-letter, duplicate-delivery, and consumer-recovery scenarios remain follow-up
test coverage.
