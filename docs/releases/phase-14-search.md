# Phase 14 — Message search: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Scope: [Implementation, Phase 14](../IMPLEMENTATION.md#phase-14--message-search). Applicable ADRs:
028 (new), 021 and 023 (access and history from the join), 027 (retention reaches the index).

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 14 is | Message search |
| Engine | Provider based: database full-text by default, OpenSearch supported |
| Word matching | Not answered; the recommended default applied: every word as a prefix, accent- and case-blind, no language stemming |
| What is searched | Not answered; the recommended defaults applied: message text and attachment file names, filters (conversation, sender, dates, files), conversation quick jump |

## Delivered

| Area | What |
| --- | --- |
| Core | `Application/Search`: `SearchText` (normalization shared by all providers), `ISearchProvider`, `ISearchBacklog`, `SearchService` (scopes from the conversation list, every match re-checked in the database), `SearchIndexer`. |
| Queue | `search_queue`: a row for every created, edited or deleted message and attachment link, added in the same `SaveChanges` (EF hook); retention and organization deletion queue their bulk removals explicitly. Background `SearchIndexingService` every `Search:IndexInterval`. |
| Database provider | `message_search` with SQLite FTS5 (external content, triggers, `unicode61 remove_diacritics 2`) or a PostgreSQL generated `tsvector` (`simple`) with a GIN index; prefix queries; visibility as a join against the caller's (conversation, first visible position) pairs (`json_each` on SQLite, `unnest` on PostgreSQL). |
| OpenSearch provider | REST over `HttpClient` (no new dependency): strict mapping with a lower-case/ASCII-folding analyzer, bulk indexing, `prefix` queries, visibility filter, `search_after` paging, basic authentication; a new index re-queues every message. Configured under `Search:OpenSearch:*`; **Admin → Providers → Message search** with a read-only connection test. |
| API | `GET /api/v1/search` (filters, cursor); `GET /api/v1/admin/search`, `POST /api/v1/admin/search/rebuild` (audited `admin.search.rebuilt`); live switch `Search:Enabled`; `403 search_disabled`, `503 search_unavailable`. |
| Migration `AddMessageSearch` | Both databases; queues every existing message so upgraded installations are indexed in the background. |
| Web app | **Search** in the navigation: query, filters (conversation, sender of that conversation, dates, with files), conversation quick jump, highlighted words, "More results". Admin: search index status and rebuild. English and Spanish. |
| Documentation | ADR-028; configuration (`Search:*`), API guide, backup guide (search index), testing guide, changelog, README; OpenAPI reference re-exported (128 paths). |

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| People find only messages they can read, from their join, never deleted ones | **Pass** (SQLite and PostgreSQL): other people's conversations not found; a later group member does not find earlier messages; leaving a group ends it at once; deleted messages disappear. |
| Edits, deletions and retention reach the index | **Pass** (both databases). |
| Both database providers behave the same; OpenSearch configurable and testable | **Pass** for the database providers. **OpenSearch: not verified against a real cluster** (no Docker or OpenSearch here); its requests and failure handling are unit-tested with a recording HTTP handler. Run it once against an OpenSearch 2.x cluster before relying on it. |

## Validation results

- .NET (Release, 0 warnings), SQLite and PostgreSQL: 384 tests; 383 passed and the PostgreSQL
  `DemoDataTests` timed out under load (the intermittent failure seen in earlier phases). That test
  was rewritten with the demo data change below and passes on both databases.
- Search: `SearchTests` 4/4 (both databases), `SearchRulesTests` 9/9.
- Web: type-check clean; tests pass; checked in the browser on desktop and phone widths, and over
  the 10,000 demo messages.

## Demo data (requested after this phase, before Phase 15)

Owner request of 2026-11-23: 300 people (password `Demo123!` for all), 50 companies, 150 groups,
about 10,000 messages in groups and direct conversations; loading stays idempotent; a **Clear demo
data** button removes only demo data.

- `DemoDataPlan` (fixed seed) describes the data; `EfDemoDataStore` inserts what is missing in bulk
  and clears with set operations in one transaction. Migration `AddDemoFlags` adds `is_demo` to
  accounts, organizations and conversations and flags earlier demo data.
- Clearing removes demo accounts (and what they wrote anywhere), demo organizations and demo
  conversations (and conversations demo accounts created), with their files; real people's
  conversations and data stay. A real person's message deleted by a demo moderator stays a tombstone.
- Demo passwords bypass the password policy (8 characters where 10 are required): demo data only
  exists where `DemoData:Enabled` is on.
- Added before Phase 16 (owner request): 500 channels spread over the companies' General and team
  workspaces (a fifth private, managers and members from the workspace, the loader in about half of
  the General-workspace channels of their companies) with 2,500 channel messages. They come from a
  separate seed, so earlier demo data stays identical and an existing installation is only extended.
  The demo data test now allows 5 minutes per load, like the web app, after a timeout while the
  whole suite shared one PostgreSQL server.
- Added on 2026-11-24 (owner request, after the UI redesign): 300 more channels (800 in all) and
  activity. People who belonged to no company join two, and anyone still in fewer than 10 channels
  is added to General-workspace channels of their companies (or joins one more company), so everyone
  is in at least 10. Activity: messages that @mention one to four people of the conversation
  (`message_mentions` rows, the mentions of the last two days left unread), saved messages,
  reminders (1 hour to 14 days ahead) and scheduled messages (2 hours to 20 days ahead): 400 + 60 +
  25 + 15 = 500 items for the administrator who loads it, 50–100 for everyone else. Again a separate
  seed, so everything earlier stays identical; people added to an existing company also join its
  General workspace. The counts gain `activity`. `DemoDataPlanTests` (unit) check the plan; the
  integration test checks the loaded rows per person.
- Measured: loading took about 20 seconds and clearing about 5 seconds on SQLite on this machine
  (before the channels were added); extending an installation that had the earlier demo data with the
  2026-11-24 additions took 31 seconds (300 channels, about 11,300 messages, about 23,000 activity items).
  `DemoDataTests` (both databases) and the web tests pass.

## Open items

- Verify the OpenSearch provider against a real cluster (index creation, analyzer, prefix queries,
  `search_after`).
- Not included: relevance ranking, typo tolerance, stemming, searching inside file contents,
  jumping to the message inside the conversation (results open the conversation).
- On phones the bottom tab bar now holds seven items; long labels are shortened.
