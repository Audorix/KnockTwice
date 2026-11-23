# Phase 18 — Message tools: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Scope: [Implementation, Phase 18](../IMPLEMENTATION.md#phase-18--message-tools). Applicable ADRs:
032 (new), 030 (threads), 031 (mentions), 025 (private chats), Phase 7 attachment rules.

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 18 is | Message tools (over bots and webhooks, calls, or rich text) |
| Who pins | Either person in direct conversations; owners and administrators in groups and channels; up to 50 per conversation; a "pinned" entry in history |
| How forwarding works | A copy with credit ("Forwarded from {author}"), no link back; files copied; optional comment; not from or to website chats (nor private chats) |
| Saved messages | Bookmarks shown only while the message can be read; nothing copied |

## Delivered

| Area | What |
| --- | --- |
| Domain | `MessagePin`, `SavedMessage`; `ConversationEventKind.Pinned` (refers to the message through the reply reference); `ConversationEvent.ForwardedFromUserId` / `MarkForwarded`. |
| Application | `ConversationService.SetPinnedAsync` / `ListPinsAsync` / `SetSavedAsync` / `ListSavedAsync`; `MessagingService.ForwardAsync` (idempotent, comment first, credited copy); `AttachmentService.CopyForForwardAsync` (stored copies under new keys, quota checked); pins and bookmarks removed with a deleted message; bookmarks removed with a deleted account; entries carry `Pinned`, `ForwardedFrom` and the caller's `Saved` (`WithCallerStateAsync`). |
| API | `GET …/pins`, `PUT …/messages/{id}/pin`, `PUT …/messages/{id}/saved`, `POST …/messages/{id}/forward`, `GET /api/v1/saved`; kind `pinned`; error texts in English and Spanish. |
| Web app | **More** menu on messages (Save, Pin, Forward); "N pinned" button with the pins list; forward dialog (find a conversation, optional comment, one idempotency key per dialog); "Forwarded from" line, "pinned"/"saved" markers; **Saved messages** tab on the Mentions page; mentions, search results, saved and pinned messages open at the message (older pages load until it is there) and highlight it; replies kept in a thread open their thread. English and Spanish. |
| Migration `AddMessageTools` | Both databases. |
| Documentation | ADR-032, Implementation Phase 18, API guide, testing guide, changelog. |

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| Pins follow the roles and the limit; deleted messages lose their pin | **Pass** (SQLite and PostgreSQL) for roles, idempotency, history entry and deletion; the limit of 50 is enforced in the service (not exercised by a test). |
| Saved messages are private and disappear when unreadable | **Pass** (both databases). |
| Forwarded copies carry the author, have their own files, never expose the source | **Pass** (both databases): the recipient opens the copied file but not the original. |

## Validation results

- Full .NET run (Release, both databases): 420/420, including `MessageToolsTests` 6/6.
- Web tests 67/67 (one AppShell test failed once in a full run and passed on rerun); type-check clean.
- Release build without warnings; OpenAPI re-exported (142 paths); no secrets in the repository.
- Not checked in a browser this phase.

## Open items

- Jumping to a message far back loads up to 30 pages of history first; a server "history around a
  message" call would make that one request.
- Forwarded files are stored again (no deduplication).
- Not included: notifications for pins, notes or folders for saved messages, forwarding to several
  conversations at once.
