# Phase 33 - Organization OIDC configuration foundation

Organization owners and administrators can store OIDC issuer, client ID, and client secret settings
through the organization API. At least one verified company domain is required. The secret is
protected with the installation's shared Data Protection key ring before it is stored, and responses
expose only whether a secret is configured. Removing the last verified domain is blocked until the
saved SSO configuration is removed.

This is configuration storage only. Organizations cannot sign in through these settings yet, and
the application does not enforce SSO. The next SSO slice must add the OIDC challenge and callback,
verified account matching, and an explicit access policy before SSO can be activated.

SQLite and PostgreSQL migrations add nullable OIDC configuration fields. The API, integration-test,
and unit-test projects build successfully with zero warnings or errors. The SQLite migration chain
was applied to a fresh disposable database, and an idempotent PostgreSQL migration script was
generated successfully. The organization-page typecheck and focused web tests pass (2 tests). The
SSO domain unit test passes; the focused integration test passes on SQLite and is skipped on
PostgreSQL because `CHATX_TEST_POSTGRES` is not configured in this environment. The automatic-
enrollment integration test also passes on SQLite.

The repository's `dotnet test` connection attempt is blocked by an environment named-pipe permission
error before tests start. Running the built xUnit test assemblies directly succeeds for the focused
checks above.
