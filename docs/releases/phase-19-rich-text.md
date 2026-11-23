# Phase 19 — Rich text: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Scope: [Implementation, Phase 19](../IMPLEMENTATION.md#phase-19--rich-text). Applicable ADRs:
033 (new), 031 (mentions), 029 (notifications), 028 (search).

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 19 is | Rich text (over bots and webhooks, calls, or scheduled messages and drafts) |
| Link previews | None: links are only clickable; the server never fetches them |
| Storage | The Markdown source as typed; plain text (markers removed) in email, push, search results and excerpts |
| Composer | Keyboard shortcuts and a formatting toolbar (no preview toggle) |

## Delivered

| Area | What |
| --- | --- |
| Application | `MessageText.ToPlain` / `Excerpt` used for notification emails, push and reply previews. No schema or API changes. |
| Web app | `RichText` renderer (bold, italic, strike, code, code blocks, quotes, lists, links, highlighted mentions; React elements only, safe links only) in conversations, threads, pending messages and the visitor widget; `toPlainText` for toasts, search results, mentions, saved and pinned lists, reply and forward previews; formatting toolbar and shortcuts (`useFormatting`, `applyFormat`) in the conversation and thread composers; updated composer hint. English and Spanish. |
| Documentation | ADR-033, Implementation Phase 19, API guide, testing guide, changelog. |

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| No HTML interpreted; only http, https and mailto links clickable | **Pass** (web tests: an `<img onerror>` stays text, `javascript:` stays text, links get `noopener noreferrer nofollow`). |
| Ordinary text not mistaken for formatting | **Pass** (server and web tests: `snake_case_name`, `2 * 3 * 4`, `a*b*c`). |
| Email and push without markers | **Pass** (`MessageTextTests`; both use the same excerpt). |

## Validation results

- Full .NET run (Release, both databases): 431/431, including `MessageTextTests` 11/11.
- Web tests 76/76 (renderer and formatting 8/8, composer formatting); type-check clean.
- No API or schema change (OpenAPI unchanged); no secrets in the repository.
- Not checked in a browser this phase.

## Open items

- No nested lists, headings, tables or images; no preview before sending.
- Older clients and third-party API users see the markers unless they render them (API guide).
