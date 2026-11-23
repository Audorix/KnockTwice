# Audorix.ChatX

Open-source, self-hosted team and customer chat built with ASP.NET Core (.NET 10) and React.
One messaging engine serves personal chat, business communication and an embeddable website chat.
Runs on **SQLite** or **PostgreSQL**, in English and Spanish.

> **Version 0.1.0, preview.** Feature-complete for its first release and covered by automated
> tests on both databases. APIs may still change before 1.0. See the [changelog](CHANGELOG.md).

## Features

- **Chat:** direct and group conversations, replies, edits and deletions, delivered/read receipts,
  typing indicators, presence, unread counts and realtime updates (SignalR) across devices.
- **Files:** images, PDF, Office, text and ZIP attachments.
  - Checked by content and kept private.
  - Stored on disk or in S3-compatible storage.
  - Optional ClamAV scanning.
- **Private chats:** one-to-one chats that live only in server memory for the initiator's session and
  are never saved.
- **Organizations:** members with roles, invitations, workspaces, **channels** (public or private),
  **teams**, **guests** limited to single channels, and organization-specific settings.
- **Website chat:** embed a chat on any website with one script tag or a React component.
  - Visitors can be anonymous or identified by a token your server signs.
  - Members answer in their normal chat.
  - Support agents work an inbox with assignment and open/closed status.
- **Administration:** live settings, email/storage/captcha providers, user and organization
  management, roles (Administrator, Auditor) and an audit log.
- **Accounts:** email confirmation, password reset, revocable sessions, Google/Microsoft sign-in,
  Cloudflare Turnstile, rate limits, CSRF protection, account deletion that keeps conversations
  readable ("Deleted user").
- **Notifications:** email when messages stay unread (batched, daily or off) and browser push while
  ChatX is closed, following each person's preview setting.
- **Search:** find messages and file names across your conversations (prefix, accent-blind), with
  filters; database full-text by default, OpenSearch optional.
- **Data retention:** inactive website visitors and never confirmed accounts are removed
  automatically; message retention can be switched on.
- **Operations:**
  - Setup wizard on first start.
  - Health endpoints.
  - Explicit migrations.
  - Docker images and Compose profiles.

## Get started

| You want to… | Read |
| --- | --- |
| Try it on your machine with Docker | [Docker development profiles](docs/development/docker.md): `docker compose -f docker/compose.sqlite.yaml up --build -d`, then open <http://localhost:8080> |
| Run it on a server | [Docker deployment](docs/deployment-docker.md) (PostgreSQL + HTTPS) or [installation without Docker](docs/installation.md) |
| Develop it | [Getting started](docs/development/getting-started.md) and [contributing](CONTRIBUTING.md) |

Every path starts with the **setup wizard** in the browser. It chooses the database, configures email
and creates the first administrator, after you enter the setup code the server prints.

## Embed the website chat

```html
<script src="https://chat.example.org/widget.js" data-widget-id="YOUR-WIDGET-ID" async></script>
```

A complete example with server-signed identities is in
[examples/website-chat](examples/website-chat). The React component is in
[web/packages/chat-embed](web/packages/chat-embed).

## Documentation

| Topic | Document |
| --- | --- |
| Installation, upgrades, administrators | [installation.md](docs/installation.md) |
| Docker deployment | [deployment-docker.md](docs/deployment-docker.md) |
| Backup and restore (with a restore drill) | [backup-restore.md](docs/backup-restore.md) |
| Database migrations | [database-migrations.md](docs/database-migrations.md) |
| Configuration reference | [development/configuration.md](docs/development/configuration.md) |
| API (guide + OpenAPI reference) | [api/README.md](docs/api/README.md) · [openapi-v1.json](docs/api/openapi-v1.json) |
| Architecture | [ARCHITECTURE.md](docs/ARCHITECTURE.md) · [solution boundaries](docs/development/architecture-boundaries.md) |
| Architecture decisions | [ADR.md](docs/ADR.md) |
| Roadmap and phases | [IMPLEMENTATION.md](docs/IMPLEMENTATION.md) · delivery records in [docs/releases](docs/releases) (release 0.1.0: [Phase 12](docs/releases/phase-12-open-source-release.md); next: [Phase 13](docs/releases/phase-13-hardening.md), [Phase 14](docs/releases/phase-14-search.md), [Phase 15](docs/releases/phase-15-notifications.md), [Phase 16](docs/releases/phase-16-reactions-threads.md), [Phase 17](docs/releases/phase-17-mentions.md), [Phase 18](docs/releases/phase-18-message-tools.md), [Phase 19](docs/releases/phase-19-rich-text.md), [Phase 20](docs/releases/phase-20-planning.md), [Phase 21](docs/releases/phase-21-release-readiness.md)) |
| Testing | [development/testing.md](docs/development/testing.md) |
| Third-party licenses | [third-party-licenses.md](docs/third-party-licenses.md) |

## Repository layout

```text
src/Api/Audorix.ChatX.Api                       HTTP host, setup wizard, migrate/grant-role commands
src/Core/Audorix.ChatX.Domain                   entities and invariants
src/Core/Audorix.ChatX.Application              use cases and ports
src/Core/Audorix.ChatX.Contracts                wire DTOs and error codes
src/Infrastructure/Audorix.ChatX.Infrastructure EF Core model, Identity, email, storage adapters
src/Infrastructure/Audorix.ChatX.Persistence.*  SQLite and PostgreSQL adapters with migrations
tests/                                          unit, architecture and integration tests
web/apps/chat-web                               React web app, embedded chat page, widget loader
web/packages/chat-embed                         React integration for the website chat
examples/website-chat                           example website with server-signed identities
docker/                                         Dockerfiles, nginx config, Compose profiles
scripts/                                        validation, migrations, license report, restore drill
docs/                                           guides, reference, ADRs, release records
```

## Contributing and security

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

Please report vulnerabilities privately as described in [SECURITY.md](SECURITY.md), not in public
issues.

## License

[MIT](LICENSE) © 2026 Audorix. Third-party components are listed in
[docs/third-party-licenses.md](docs/third-party-licenses.md).
