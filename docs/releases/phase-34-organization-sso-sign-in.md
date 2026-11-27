# Phase 34 - Organization OIDC sign-in

Organization owners and administrators can configure OIDC from organization settings, rotate or
remove the protected client secret, and copy or open a shareable organization sign-in URL. The
challenge and callback use the standard OpenID Connect authorization-code flow with PKCE.

The callback requires a verified email whose domain exactly matches a verified organization
domain. A matching confirmed account is linked by exact email; automatic account provisioning is
available only when an administrator explicitly enables the separate domain-enrollment setting.
Successful authentication also requires active organization membership and creates a normal
Knock Twice session. OIDC is an additional sign-in method and does not force all members to use
SSO.

Verification completed:

- API and integration-test projects build with zero warnings or errors.
- Focused organization SSO integration test passes on SQLite. PostgreSQL coverage is skipped when
  `CHATX_TEST_POSTGRES` is not configured.
- Web typecheck passes.
- Organization page tests pass (2 tests), including the SSO settings panel.

A live identity-provider round trip and PostgreSQL integration run remain to be verified with a
configured provider and test database.
