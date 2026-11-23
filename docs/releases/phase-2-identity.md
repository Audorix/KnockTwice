# Phase 2 — Identity and authentication: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-22.
Applicable ADRs: 006, 008 (user-owned data), 009, 013, 016 (no process-local durable state), 018.
ADR-020 to ADR-024 remain **Proposed**; ADR-024's widget token flow was not implemented.

Also in this slice: Swagger UI at `/swagger` (Development only) over the existing OpenAPI
document, and `chatx-web.code-workspace` for VS Code.

## Decisions taken for this phase (owner-approved)

| Gate (Implementation §12.2) | Decision |
| --- | --- |
| Session lifecycle | HttpOnly `SameSite=Lax` cookie + antiforgery tokens; server-side sessions that can be listed and revoked. |
| Email delivery adapter | Provider-based: `Smtp` (default, local settings) and `Postmark`. |
| Human verification | Provider-based: Cloudflare `Turnstile` (default) and `None`. |
| Email verification | Required before password sign-in by default (`Identity:RequireConfirmedEmail`). |
| Avatar | Generated initials now; uploads with the Phase 7 storage abstraction. |
| External-login callbacks | `/api/v1/auth/external/signin-{google,microsoft}`; enabled only when credentials are configured. |

## Delivered behaviour

- ASP.NET Core Identity on the shared EF model (snake_case tables on both providers).
- `POST /api/v1/auth/register`, `confirm-email`, `resend-confirmation`, `login`, `logout`,
  `forgot-password`, `reset-password`; `GET /auth/config`, `GET /auth/csrf`;
  `GET /auth/external/{provider}` + callback; `GET/DELETE /auth/sessions`, `POST /auth/sessions/revoke-others`.
- `GET/PUT /api/v1/users/me`, `PUT /users/me/preferences`, `POST /users/me/password`.
- Role and permission foundation; `InstallationAdministrator` active; `GET /api/v1/admin/installation`
  guarded by the `installation.read` permission; `grant-role` command for the first administrator.
- Security: antiforgery on every state-changing `/api/v1` request; uniform responses that do not
  reveal account existence; lock-out; per-IP rate limits on auth endpoints; password reset and change
  revoke other sessions; external sign-in never auto-links existing accounts; Data Protection keys
  persisted in the database.
- Localized (EN/ES) account emails and API error titles.
- Web app: sign-in (with "keep me signed in" and enabled external providers), registration with
  Turnstile, forgot/reset password, resend confirmation, email verification, account page (profile,
  language preference saved to the account, password change, sessions). Small history router; no
  new runtime dependencies.
- Docker: Mailpit inbox in both profiles; email, captcha and public URL settings in `docker/.env`.

## Migration impact

`AddIdentityAndSessions` for each provider creates `users`, `roles`, `user_roles`, `user_claims`,
`user_logins`, `user_tokens`, `role_claims`, `user_profiles`, `user_sessions`,
`data_protection_keys`. The `installation` table is unchanged. Applied to the local development
SQLite database with the `migrate` command.

## Validation results

| Check | Result | Evidence |
| --- | --- | --- |
| .NET build (Release, warnings as errors) | **Passed** | `scripts/validate.ps1` |
| .NET tests incl. real PostgreSQL 18 | **Passed** — 125 total, 125 passed, 0 skipped | `AuthFlowTests` register→confirm→sign-in→profile→sign-out and password reset run on SQLite **and** PostgreSQL; temporary databases dropped (0 left, verified). |
| Architecture rules | **Passed** | Infrastructure limited to Identity/DataProtection EF packages; Domain/Application free of Identity. |
| Frontend type-check, tests, production build | **Passed** — 19 tests | Run individually; see note on `npm ci` below. |
| Browser, end to end (Vite → API → SQLite) | **Passed** | Registered with the real Turnstile widget (Cloudflare test keys) and real server-side siteverify; confirmation email delivered over SMTP to a local sink; link confirmed; keyboard sign-in with "keep me signed in"; account page; switching to Spanish saved the account preference; session listed as "Chrome on Windows"; auth cookie not readable by JavaScript; sign-out → 401. 431 px wide (mobile layout). |
| *Correction (recorded during Phase 3)* | — | The browser check above went through an older dev server whose proxy pointed at an orphaned API process on port 5080 (the intended `.env.local` override was ignored because it was written with a UTF-8 BOM). That API served the Phase 2 endpoints, but its exact build cannot be confirmed. The Phase 3 browser check re-ran sign-in and sign-out against a verified fresh build; the automated Phase 2 suite (125 tests on both providers) is unaffected. |
| Postmark delivery | **Unit-tested only** | Payload, headers and error handling verified against a recording HTTP handler; no Postmark account was used. |
| Google / Microsoft sign-in | **Partially verified** | Challenge redirect to Google with the API callback verified; a full round trip needs real client credentials. |
| Docker profiles (incl. Mailpit) | **Not run** | Docker is not installed on the validation machine. |
| `npm ci` step of `scripts/validate.ps1` | **Failed (environment)** | An orphaned Vite dev server from an earlier preview kept a native module locked, so `npm ci` could not delete `node_modules`. `npm install` from the lockfile, then type-check/tests/build, passed. Re-run the script after closing that process. |

## Open items

- Behind a reverse proxy, configure forwarded headers before relying on per-IP rate limits and the
  remote IP sent to Turnstile.
- Expired/revoked `user_sessions` rows are not yet purged (needs a background cleanup job).
- A distinct notification language (ADR-009) is not yet separate from the UI language.
- Account deletion and data retention remain open product decisions (ADR decision register).

## Next slice

Phase 3 — Organizations and contacts. Its gate requires deciding ADR-021 (personal vs
organization conversation ownership), discovery defaults and blocking behaviour first.
