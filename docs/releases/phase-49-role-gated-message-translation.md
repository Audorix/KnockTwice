# Phase 49: Role-gated message translation

## Delivered

- Added `messages.translate` as an installation permission. Administrators grant or deny it through
  the existing group-to-role permission controls; users are not assigned roles directly.
- Added an on-demand translation endpoint for messages the caller is already allowed to read. The
  endpoint checks message/conversation access and enforces the permission server side. Deleted or
  empty messages cannot be translated.
- Added a provider adapter for OpenAI Chat Completions compatible endpoints, including Ollama, with
  endpoint, model, secret, timeout, and output-size validation. Administrators can save multiple
  named provider profiles, test each one, and activate one for translation. API keys are write-only
  in the admin UI. Translation is disabled until a provider is configured.
- Added message translation controls for authorized users. Each user can select a target language;
  the preference is stored in that browser. Translation is shown alongside the unchanged original,
  and the user can translate again to another language.
- Added an opt-in support translation preference for users with `messages.translate`. When enabled,
  visible support messages and incoming messages as they are viewed are translated into the selected
  language. Work is limited to messages in view and is queued with at most two automatic provider
  requests at a time; users can switch it off at any time.
- Added an in-context disclosure that the message is sent to the organization's configured AI
  provider when the user requests translation.
- Saved each translation in the database alongside an immutable snapshot of the original text,
  target language, and source-text hash. Later requests reuse that translation while the source is
  unchanged; an edited message gets a new translation record. The original message body is never
  overwritten, and translation records are removed with their source message.

## Boundaries

Translation is off by default. Authorized users can request a translation per message or explicitly
enable automatic translation for support conversations in that browser. Each translation is saved
server-side with its original-text snapshot; translations are not broadcast into conversation
history. The original message remains authoritative. Users without `messages.translate` cannot
access the controls or invoke the endpoint.
Automatic translation only processes support messages as they enter the visible timeline; it does not
run as a background job.

One provider profile is active for installation-wide translation at a time. Supported choices are
`OpenAICompatible` and `Ollama`; the latter uses an OpenAI-compatible API surface. Fallback chains,
usage budgets, and organization-specific provider selection are not part of this slice.

## Verification

The unit tests cover language and text validation and confirm the permission appears in the
installation permission catalog. Provider connection testing is available from the admin provider
settings. Automated test execution is not recorded here.
