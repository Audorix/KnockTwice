# Phase 27 - Claim from the support queue

Agents can take an open, unassigned conversation directly from its inbox row. The inbox uses the
existing claim endpoint, disables the row action while the request is in flight, then reloads the
queue and assignee state. Opening the row remains available for agents who want to inspect a case
before claiming it.

## Validation

- Focused support inbox test: 5/5 passed, including claiming an unassigned item, its removal from
  the queue, and the updated assignee in the normal inbox.
- Full web suite: 123/123 passed.
- Web type-check passed.
- `git diff --check` passed.
