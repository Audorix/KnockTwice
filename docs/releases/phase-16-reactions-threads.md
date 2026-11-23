# Phase 16 — Reactions and threads: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Scope: [Implementation, Phase 16](../IMPLEMENTATION.md#phase-16--reactions-and-threads). Applicable ADRs:
030 (new), 022 (sequences), 023 (read positions), 029 (notifications), 025 (private chats unaffected).

## Demo data (requested before this phase)

**Populate demo data** also creates 500 channels (public and private) in the demo companies' workspaces,
with members scattered among the demo people and 2,500 channel messages. It stays idempotent, and
**Clear demo data** removes the channels with the rest of the demo data. Totals: 300 people, 50
companies, about 960 contacts, 150 groups, about 330 direct conversations, 500 channels and about
12,500 messages.

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 16 is | Reactions and threads |
| Which reactions | Any emoji; at most 20 different ones per message; not in website or private chats |
| How threads look | A side panel; replies stay out of the conversation unless "Also send to the conversation" is ticked; threads have their own unread state; followers are notified |
| Quote replies | Kept alongside threads |

## Delivered

| Area | What |
| --- | --- |
| Domain | `MessageReaction` (emoji validation: one text element, a symbol, no letters/digits/spaces, at most 32 units; 20 distinct per message); `ThreadFollow` (following, thread read position); thread fields on `ConversationEvent` (`ThreadRootId`, `ShownInConversation`, `ThreadReplyCount`, `ThreadLastReplyAtUtc`). |
| Application | `MessagingService.SetReactionAsync` (publishes `ReactionChanged`); sending with `threadRootId`/`alsoInConversation` (authors of the first message and of replies follow automatically; the root's count updates; deleting a reply lowers it); `ConversationService.GetThreadAsync`, `MarkThreadReadAsync`, `SetThreadFollowingAsync`; history with reactions and per-caller thread summaries; main timeline and unread counts without hidden replies. |
| Notifications | Hidden thread replies notify followers only (push and email); echoed replies as any message. |
| API | `PUT …/messages/{id}/reactions`, `GET …/messages/{id}/thread`, `POST …/thread/read`, `PUT …/thread/follow`; new entry fields; realtime `ReactionChanged` (not sent to website visitors). Error texts in English and Spanish. |
| Web app | Reaction chips (yours highlighted, names in the tooltip) and a **React** picker with six quick emoji, 32 more and a field for any emoji; **Reply in thread**, "N replies" with an unread badge, a side panel with follow / stop following, older replies, a composer with "Also send to the conversation"; echoed replies link to their thread. Live updates for reactions and thread replies. English and Spanish. |
| Migration `AddReactionsAndThreads` | Both databases. Retention purges and **Clear demo data** detach replies whose first message is removed. |
| Documentation | ADR-030, Implementation Phase 16, API guide, testing guide, changelog. |

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| Reactions toggle per person, are validated and limited, and reach others in real time | **Pass** (SQLite and PostgreSQL) for the API rules; the realtime event is published to all participants and handled by the web app (web test for chips and picker). |
| Thread replies stay out of the main timeline and its unread count unless echoed; the first message shows the count and the caller's unread replies | **Pass** (both databases). |
| Both database providers behave the same; website chats have neither | **Pass** for both providers; the website restriction is enforced in the service and hidden in the web app (not covered by a dedicated test). |

## Validation results

- Full .NET run (Release, both databases): 395 of 396; the one integration failure did not repeat —
  the integration suite rerun passed 150/150.
- Phase 16 integration tests 4/4 (both databases); web tests 63/63; type-check clean; Release build
  without warnings; OpenAPI re-exported (135 paths).
- Browser check (desktop, throwaway SQLite installation): reaction chip shown and added from the
  picker (highlighted as yours), thread summary "1 reply", thread panel with the first message, its
  reactions and the reply, an echoed reply appearing live in the conversation with "Replied in a
  thread · View thread" and the count rising to 2. The check found the picker clipped by the card
  edge; it now opens leftwards. Phone width was not checked on screen (the local network of the
  check environment dropped requests); the panel takes the full width below 26 rem.

## Open items

- Not included: thread-level mute, nested threads, notifications for reactions, attachments from the
  thread panel (the API accepts them), reactions in the visitor widget.
- In-app (desktop) notifications skip thread replies that are not shown in the conversation; the
  web app does not know who follows a thread. Followers get them by push and email.
- A thread reply that arrives while the conversation is loading can be counted twice on the first
  message until the next reload (display only).
