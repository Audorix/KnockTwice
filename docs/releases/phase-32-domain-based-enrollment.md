# Phase 32 — Explicit domain-based enrollment

Verified domains can now be used for automatic organization enrollment, but only after an owner or
administrator enables the setting. It starts off for every organization and cannot be enabled until
at least one domain is verified.

An account joins only after its email is confirmed and its domain exactly matches a verified
organization domain. The new member receives the `Member` role and access to the first workspace.
Enrollment does not add the user to other workspaces. Disabling the setting stops future enrollment
and leaves existing members in place. Administrators must disable enrollment before removing the
last verified domain.

SQLite and PostgreSQL migrations add the setting with a false default. The API, integration-test,
and unit-test projects build successfully with zero warnings or errors. The SQLite migration chain
was applied to a disposable database, and an idempotent PostgreSQL migration script was generated.
The web typecheck and focused organization-page tests pass (2 tests).

The focused backend integration test could not execute in this environment: xUnit failed while
connecting to its test-runner named pipe with `UnauthorizedAccessException`, before any tests ran.
The integration-test project itself builds successfully.
