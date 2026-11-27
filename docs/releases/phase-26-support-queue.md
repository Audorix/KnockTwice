# Phase 26 - Support queue visibility

Support inbox pages now include `counts.unassignedOpen`, the count of visible open website
conversations without an assignee. The support page shows this count in an **Unassigned queue**
control that filters the inbox to those cases. The count is computed before the selected-assignment
filter, so the queue remains visible while other assignment filters are active.

## Validation

- The API integration test checks the queue count against unassigned open cases on SQLite and
  PostgreSQL.
- The web test checks the queue count, filtering behavior, and removal of assigned cases from the
  queue view.
