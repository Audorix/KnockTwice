# Phase 31 — Verified organization domains

Organization owners and administrators can add a DNS domain and verify ownership by publishing the
challenge shown in organization Settings. The API queries the server's configured DNS resolver and
stores the verification time after a match. Domain names are normalized with IDN support, and each
domain can be claimed by only one organization.

Verification is separate from organization membership. The application does not automatically add
people with matching email addresses; automatic enrollment remains off until a separate explicit
administrator setting and its sign-in behavior are implemented. Organization SSO is also a later
Release 1 slice.

The schema change is additive in SQLite and PostgreSQL. Rollback drops the `organization_domains`
table, so export any domain verification state before rolling the database back.

Validation: web type-check and the focused organization page tests pass. API and integration-test
projects build cleanly. The SQLite migrations apply to a disposable database, and the PostgreSQL
idempotent migration script includes the new table. Backend test execution is blocked in this
environment because xUnit cannot open its test-platform named pipe (`UnauthorizedAccessException`).
