# Phase 12 — Open-source release 0.1.0: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Applicable ADRs: 014 (licensing), 015 (deployment), 018 (security), 020 (release boundaries, still proposed).

## Gate decisions, owner answers of 2026-11-23

| Question | Decision |
| --- | --- |
| Copyright holder | "Audorix" (`LICENSE`, build metadata) |
| Vulnerability reports | GitHub private vulnerability reporting (security advisories); no email published |
| First version | 0.1.0 (preview) |
| Repository | https://github.com/audorix/Audorix.ChatX |

## Deliverables (Implementation §Phase 12)

| Deliverable | Where |
| --- | --- |
| MIT license | `LICENSE` (copyright Audorix); `Directory.Build.props` (Version 0.1.0, Company, Copyright, RepositoryUrl) |
| README | `README.md`, rewritten as the release front page |
| Architecture documentation | `docs/ARCHITECTURE.md`, `docs/development/architecture-boundaries.md` (existing, updated through Phase 11) |
| ADR documentation | `docs/ADR.md` (ADR-014 note for this release) |
| Installation guide | `docs/installation.md`: without Docker, SQLite or PostgreSQL, wizard or unattended, administrators, upgrades, production checklist |
| Docker deployment guide | `docs/deployment-docker.md` and the new `docker/compose.production.yaml` (PostgreSQL, API not published, web on localhost behind TLS, trusted proxies, backups, upgrades) |
| API documentation | `docs/api/README.md` (auth, CSRF, errors, paging, idempotency, realtime, widget) and `docs/api/openapi-v1.json` (122 paths, exported from the release build) |
| Configuration reference | `docs/development/configuration.md` (existing; updated for trusted proxy chains) |
| Contribution guide | `CONTRIBUTING.md` |
| Security policy | `SECURITY.md` |
| Sample environment configuration | `docker/.env.example`, `docker/.env.production.example`, `docs/samples/appsettings.Production.example.json` |
| Database migration instructions | `docs/database-migrations.md` |
| Example embedded integration | `examples/website-chat` (dependency-free Node site with server-signed identities), React package `web/packages/chat-embed` |
| Changelog | `CHANGELOG.md` |

## Code changes in this phase

- **`Network:TrustedProxies`:** the forwarded-header chain is now followed back while each hop is
  trusted. The previous limit of one hop would have keyed rate limits on the TLS proxy when it runs
  in front of the bundled nginx.
- **Version and wording:** the public `/api/v1/meta` stage changed from `foundation` to `preview`.
  The signed-out intro text (EN/ES) now describes the current product instead of the Phase 1
  foundation.
- **`.gitignore`:** keeps `docker/.env.production.example`.

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| A developer can install using documentation alone | Walked through `docs/installation.md` against the published release build (`dotnet publish -c Release`, `ASPNETCORE_ENVIRONMENT=Production`), for both the wizard and the unattended path. |
| SQLite installation works | **Pass.** Setup wizard on SQLite: ready 200; administrator signs in with a Secure, HttpOnly cookie; Swagger off (404); the settings file holds no administrator or database password. Unattended path (sample settings file plus environment variables): `migrate` applied 10 migrations, ready 200, registration, `grant-role`. |
| PostgreSQL installation works | **Pass.** Database configured by environment (wizard shows it locked); the wizard created the database; ready 200; sign-in as above. The check database was dropped afterwards. |
| Docker deployment works | **Not verified here:** Docker is not installed on the build machine. The images and development profiles are unchanged since earlier phases. `compose.production.yaml` is new and reuses the same images; it should be run once on a Docker host before announcing the release. |
| API documentation is available | **Pass.** Guide plus OpenAPI 3.1 reference in the repository; Swagger UI in Development. |
| No secrets in the repository | **Pass.** Scanned all 391 files git would publish for credentials, keys, tokens, passwords, the test database password and personal data or local paths. The only matches are test fixtures and published test values: AWS's documentation example key in the S3 signer test, and Cloudflare's always-pass Turnstile test keys in `appsettings.Development.json`. `Demo/` (licensed template), `App_Data/`, databases and `.env` files are git-ignored. |
| Dependency licenses are reviewed | **Pass.** `docs/third-party-licenses.md`: 23 NuGet packages (MIT, Apache-2.0, PostgreSQL) and 27 npm runtime packages (MIT, BSD-2/3-Clause, Unlicense). No copyleft. |

## Validation results

- .NET: **365 tests passed** with PostgreSQL and SQLite, Release build, 0 warnings.
- Web: type-check clean; 56 tests passed (18 files); production build OK; the React package
  type-checks.
- Every relative link in the 32 Markdown files resolves.

## Open items before announcing

- Run `docker/compose.production.yaml` once on a Docker host (and the development profiles).
- On GitHub:
  - enable private vulnerability reporting (repository → Settings → Security);
  - create the `v0.1.0` tag and release;
  - decide whether `.claude/launch.json` (local tool configuration) should be published.
- `web/packages/chat-embed` is not an npm workspace member and is not published to npm yet.
- ADR-020 (release boundaries) remains *Proposed*; account deletion and retention are still open in
  the decision register.
