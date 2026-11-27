# Phase 25 - Widget setup preview

Release 2 increment: the widget setup page now previews the visitor-facing chat while settings are
edited. The title, welcome message, accent colour and screen corner update immediately. The preview
is local and does not create a visitor session or support conversation.

The copyable installation snippet, allowed-origin settings and visitor-to-agent flow remain the
existing configuration and delivery path.

## Validation

- Widget administration tests: 2/2 passed, including live preview updates for title, welcome,
  colour and corner.
- Web type-check passed.
- `git diff --check` passed.
