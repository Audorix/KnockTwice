# Contributing to Audorix.ChatX

Thank you for helping. This guide covers how to set up, what we expect in a change, and how to get
it merged. By contributing you agree that your work is licensed under the [MIT License](LICENSE).

## Before you start

- **Bugs:** open an issue with steps to reproduce, what you expected, and your setup (database,
  Docker or not, browser).
- **Security problems:** do **not** open an issue; follow [SECURITY.md](SECURITY.md).
- **Features or larger changes:** open an issue first to agree on the approach.
  - Behaviour and security rules are recorded as architecture decisions in [docs/ADR.md](docs/ADR.md).
  - A change that alters one needs a new or amended ADR.
- Small fixes (typos, docs, obvious bugs) can go straight to a pull request.

## Development setup

Follow [docs/development/getting-started.md](docs/development/getting-started.md).

**Prerequisites**

- .NET SDK 10.0.404+ (pinned in `global.json`);
- Node.js 22.12+;
- optionally PostgreSQL for the PostgreSQL tests.

**Run it**

1. Run the API (`dotnet run --project src/Api/Audorix.ChatX.Api --launch-profile chatx`).
2. Start the web app (`cd web && npm ci && npm run dev`).
3. Complete the setup wizard at <http://localhost:5173>.
4. **Populate demo data** on the home page gives you people, organizations and conversations to
   work with.

## Making a change

**Architecture.** Keep to the layering in
[docs/development/architecture-boundaries.md](docs/development/architecture-boundaries.md):

- Domain → Application → Infrastructure/API; Contracts hold the wire types.
- Architecture tests enforce the project graph.
- Rules and authorization live in the Application layer and are checked on the server for every
  request. The web app only hides what the server would refuse.

**Both databases.** Every schema change needs a migration for SQLite **and** PostgreSQL:
`./scripts/add-migration.ps1 -Name <Name>`. See
[docs/database-migrations.md](docs/database-migrations.md).

**Both languages.** Every user-visible text exists in English and Spanish:

- `web/apps/chat-web/src/i18n/locales/{en,es}.json`;
- API error titles in `src/Api/Audorix.ChatX.Api/Resources/Localization/ErrorMessages*.resx`.

New error codes are lower_snake_case, stable, and get translations.

**Configuration.** New settings are validated at startup and documented in
[docs/development/configuration.md](docs/development/configuration.md). A setting must never
appear to enable something that does not exist.

**Accessibility.** Use labelled controls, keyboard operation, visible focus, and `aria-live` for
updates; test at phone width and in dark mode.

**Security**

- No secrets in code, tests or docs (test fixtures use obvious fake values).
- No content or secrets in logs or the audit log.
- Resources the caller may not see answer "not found".

**Code style.** `.editorconfig` applies.

- .NET: warnings are errors (`TreatWarningsAsErrors`, recommended analyzers).
- TypeScript: strict, including `exactOptionalPropertyTypes` and `noUncheckedIndexedAccess`.
- Keep files UTF-8 with LF line endings.

## Tests

Add or update tests with every behaviour change:

- **Domain rules:** `tests/Audorix.ChatX.UnitTests`.
- **API behaviour, authorization and persistence:** `tests/Audorix.ChatX.IntegrationTests`. These
  run against the real API with SQLite and, when `CHATX_TEST_POSTGRES` is set, PostgreSQL. Use the
  `Providers` theory data for anything that touches the database.
- **Web:** Vitest + Testing Library next to the component (`*.test.tsx`).

Run everything before opening a pull request:

```powershell
$env:CHATX_TEST_POSTGRES = "Host=localhost;Username=<user>;Password=<password>;Database=postgres"
./scripts/validate.ps1
```

```sh
CHATX_TEST_POSTGRES="Host=localhost;Username=<user>;Password=<password>;Database=postgres" ./scripts/validate.sh
```

The PostgreSQL account needs permission to create databases. The tests create and drop uniquely
named `chatx_test_*` databases.

`validate` runs `npm ci`, which replaces `web/node_modules`; stop running dev servers first.

## Pull requests

- Keep each pull request focused on one change and describe what changed and why.
- Link the issue, and mention any ADR, migration or configuration change.
- Include screenshots for UI changes (light and dark, desktop and phone width where relevant).
- Update the docs you touched: the configuration reference, the API guide, and `CHANGELOG.md`
  under *Unreleased*.
- If you added or upgraded a dependency, regenerate
  `docs/third-party-licenses.md` (`node scripts/third-party-licenses.mjs`) and check the license is
  permissive.
- All tests must pass. Maintainers may ask for changes; please do not take review personally.

## Releases

Maintainers release by:

1. Updating `<Version>` in `Directory.Build.props` and the `version` fields in
   `web/apps/chat-web/package.json` and `web/packages/chat-embed/package.json`.
2. Moving *Unreleased* in `CHANGELOG.md` under the new version.
3. Exporting the OpenAPI document (see [docs/api/README.md](docs/api/README.md)).
4. Tagging `vX.Y.Z`.
