# Phase 20 — Drafts, scheduled messages and reminders: delivery record

Recorded per [Implementation §12.5](../IMPLEMENTATION.md). Date: 2026-11-23.
Scope: [Implementation, Phase 20](../IMPLEMENTATION.md#phase-20--drafts-scheduled-messages-and-reminders).
Applicable ADRs: 034 (new), 029 (notifications), 030 (threads), Phase 7 attachment rules.

## Owner decisions of 2026-11-23

| Question | Decision |
| --- | --- |
| What Phase 20 is | Scheduled messages and drafts (over bots and webhooks, calls, or status and availability) |
| A scheduled message that can no longer be sent | Not sent; kept in the Scheduled list with the reason until removed. Up to 30 days ahead, 50 waiting per person, text and files |
| What drafts sync | Text and reply target, per conversation and thread, on the server; no files; removed after 30 days |
| How reminders reach you | In the app, by push, and by email when you are away |

## Delivered

| Area | What |
| --- | --- |
| Domain | `MessageDraft`, `ScheduledMessage` (+ `ScheduledMessageFile`), `Reminder`. |
| Application | `PlanningService` (drafts; scheduling, editing, cancelling, sending as the author through `MessagingService.SendAsync` and recording failures; reminders, due processing, readable-only listing); `NotificationService.NotifyReminderAsync` (push and email when away); reminder email (EN/ES). |
| API | `PUT …/draft`, `GET /api/v1/drafts`; `POST …/scheduled`, `GET/PUT/DELETE /api/v1/scheduled`; `POST …/messages/{id}/reminder`, `GET/DELETE /api/v1/reminders`; realtime `PlanningChanged` and `ReminderDue`; `PlanningDispatchService` (setting `Planning:DispatchInterval`). Uploads of waiting schedules kept by the attachment cleanup; drafts, schedules and reminders removed with the account. |
| Web app | Drafts restored and saved (debounced) in conversations and threads, "Draft" in the list; **Send later…** with quick times and a custom time; **More → Remind me…**; the Mentions item became **Activity** with Scheduled and Reminders tabs (edit, cancel, remove; done); reminder toasts and desktop notifications; badge counts due reminders. English and Spanish. |
| Migration `AddPlanning` | Both databases. |
| Documentation | ADR-034, Implementation Phase 20, API guide, configuration, testing guide, changelog. |

## Acceptance criteria

| Criterion | Result |
| --- | --- |
| Scheduled messages go out on time with the normal rules; refused ones stay with the reason | **Pass** (SQLite and PostgreSQL). |
| Files of waiting schedules are not cleaned up | Enforced in the cleanup query; the 24-hour wait was not exercised by a test. |
| Reminders come due once, notify, and go when done | **Pass** (both databases) for the email path; push uses the same delivery as message push; the in-app toast is covered by the realtime event and web code (not by an automated end-to-end test). |

## Validation results

- Full .NET run (Release, both databases): 435 of 437. The two failures were Phase 20 tests on
  PostgreSQL that scheduled only 1–2 seconds ahead; under the full run's load the request arrived
  after that time and was correctly refused. The tests now use 5–8 seconds and wait up to 40 seconds;
  `PlanningTests` then passed 6/6 on both databases.
- Web tests 79/79; type-check clean.
- OpenAPI re-exported (150 paths); no secrets in the repository.
- Not checked in a browser this phase.

## Open items

- Recurring messages, snoozing reminders, drafts with files.
- A stopped API sends scheduled messages late (on restart), never twice.
