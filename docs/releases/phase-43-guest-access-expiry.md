# Phase 43: Guest and invitation expiry

Accepted organization guests receive a 90-day access window from acceptance. The deadline is
returned by the guest API and shown in organization guest management. After expiry, a guest is
excluded from active guest lists, conversation lists, realtime recipients, and conversation access
checks. An administrator can renew access by issuing a fresh guest invitation; accepting it starts
a new 90-day window.

Guest invitations retain their 14-day lifetime. Organization invitations retain their existing
14-day lifetime as well. The guest view shows the expiry date on both pending guest invitations and
accepted guests.

SQLite and PostgreSQL migrations add the access deadline and give existing guest rows a fresh
90-day window when the migration runs, avoiding an unexpected lockout during upgrade. Expiry is
currently fixed at 90 days; organization-specific duration settings remain future work.

Verification covers the domain expiry boundary and renewal behavior, guest API expiry fields, and
the SQLite guest lifecycle integration test. PostgreSQL integration requires the configured test
database and was skipped when unavailable.
