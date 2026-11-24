# Security policy

## Supported versions

| Version | Supported |
| --- | --- |
| 1.0.x | Yes |
| 0.1.x | No (preview; upgrade to 1.0) |

Security fixes are released as new patch versions. Please upgrade to the latest patch release.

## Reporting a vulnerability

**Do not open a public issue for security problems.**

Report privately through GitHub:
**[Report a vulnerability](https://github.com/audorix/ChatX/security/advisories/new)**
(repository → *Security* → *Report a vulnerability*).

Please include:

- the affected version or commit, and how it is configured (database, Docker or not, providers);
- steps to reproduce, or a proof of concept;
- the impact you expect (for example account takeover, data disclosure across organizations);
- whether the issue is already public.

What happens next:

- We acknowledge reports within **5 working days**.
- We keep you informed while we investigate.
- We agree with you on a disclosure date once a fix is available.
- If you wish, you are credited in the advisory.

Please give us reasonable time to fix the problem before you disclose it. Do not access other
people's data, disrupt installations you do not operate, or use social engineering.

## Scope

In scope:

- this repository: the API, web app, embedded chat, widget loader, React package, Docker files and
  nginx configuration;
- its documented default configuration.

Out of scope:

- vulnerabilities in third-party services or dependencies without a demonstrated impact on
  Audorix.ChatX (report those upstream);
- installations that deviate from the documentation (for example running Development settings in
  production, or exposing the API without HTTPS);
- missing best-practice headers without a concrete attack.

## Security model in brief

Details are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/ADR.md](docs/ADR.md)
(ADR-018, ADR-024, ADR-025, ADR-026).

**Browser authentication**

- HttpOnly, SameSite=Lax, Secure cookies bound to server-side sessions that can be revoked.
- Antiforgery tokens on every state-changing request.
- No CORS.
- Lock-out and rate limits.
- Optional human verification.

**Authorization**

- Checked on the server for every request and every realtime call.
- Resources outside the caller's reach answer "not found".
- Organizations, workspaces and channels are isolated from each other.
- Guests are never organization members.
- No administrator role reads conversation content.

**Files**

- Checked by content signature.
- Stored privately and authorized on every download, with no public links.
- Served with a sandboxing CSP and `nosniff`.
- Optional malware scanning with quarantine.

**Website chat**

- Runs in a sandboxed iframe that only the listed origins may frame (`frame-ancestors`).
- `postMessage` is checked by source window and origin.
- Visitors use bearer tokens (only their hashes are stored) and never cookies.
- Identity tokens are HS256 JWTs signed by the website's server: algorithm pinned, audience
  checked, short lifetime.

**Secrets**

- Provider secrets are write-only in the administration and never logged.
- Widget secrets are protected with the installation's Data Protection keys.

## Hardening checklist for operators

- Serve over HTTPS only.
- Run with `ASPNETCORE_ENVIRONMENT=Production` and development mode off in the setup wizard.
- Keep the settings file (`App_Data/chatx.settings.json` or the Docker data volume) and database
  backups private: they contain provider secrets and the Data Protection keys.
- Set `Network:TrustedProxies` only to proxies that all traffic passes through.
- Configure human verification or restrict registration.
- Keep dependencies and base images up to date.
