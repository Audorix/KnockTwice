# Knock Twice Roadmap

Knock Twice by Audorix is a self-hosted messaging platform for team conversations, customer support, and collaboration with clients and partners. This roadmap describes the next five planned releases. The numbers show the order of delivery and are independent of software version tags. Scope and timing may change as we learn from pilots.

## Planned releases

| Release | Focus | What it makes possible |
| :-- | :-- | :-- |
| **1** | [Company setup and access](#release-1-company-setup-and-access) | Set up a workspace, control access, send formatted messages and code snippets, and manage support chats in a flexible desktop layout. |
| **2** | [Customer support desk](#release-2-customer-support-desk) | Turn website conversations into support cases a team can manage together. |
| **3** | [Mobile and shared media](#release-3-mobile-and-shared-media) | Keep up with conversations on mobile and find or play shared media in context. |
| **4** | [Partner collaboration and data control](#release-4-partner-collaboration-and-data-control) | Work with external partners, connect company drives and business systems, and move or recover conversation data. |
| **5** | [AI for conversations and business teams](#release-5-ai-for-conversations-and-business-teams) | Translate messages, turn recordings into searchable notes, and give teams specialized assistants, including a support agent that can work with tickets. |

## Release 1: Company setup and access

Make Knock Twice easier to set up and give teams the tools to write clear business messages and share technical details.

- **Rich text and code editor (business priority).** Add a formatting toolbar to the chat composer for bold, italics, links, lists, and quotes. Support inline code and a dedicated code block editor with language selection, syntax highlighting, preserved indentation and line breaks, and a copy button on sent snippets. Use it for procedures, incident updates, logs, and code review discussions. Render snippets as text and keep existing messages readable.
- **Organization setup.** Guide owners through branding, the first workspace, role assignment, and invitations. Add verified domains and optional organization SSO. Automatic enrollment requires an explicit administrator choice.
- **Granular permissions.** Add scoped roles and groups at installation, organization, and workspace levels. Cover private-chat use; support access and case handling; organization membership and management; channel creation and administration; and installation administration for users, roles, permissions, groups, settings, and providers. For example, an organization can restrict private chat to an approved group. Add effective-access reviews, guest and invitation expiry, and auditable changes to permissions.
- **Role-gated message translation (added to Release 1).** Let administrators configure approved AI translation providers and choose the active provider for translation. Users with the `messages.translate` permission can translate an incoming message to a selected language, or opt in to automatic translation of support messages. Keep the original available and disclose that message text is sent to the configured provider. Denied or unassigned users cannot invoke translation.
- **Desktop support chat layout (added to Release 1).** Let agents choose a two-chat or four-chat desktop view, maximize any open chat across the workspace, and restore the previous multi-chat layout with its existing pane assignments. Keep the maximize/restore control available on each chat and remember the layout per user and organization. This layout capability is part of Release 1; the broader customer support desk remains planned in Release 2.
- **Multi-node SignalR delivery (added to Release 1).** Support multiple API nodes over shared PostgreSQL and Redis. Keep the Redis SignalR backplane separate from cache and queue provider selection, require one shared Redis endpoint and environment-specific channel prefix, and fail readiness rather than silently switching to local delivery. Configure WebSocket upgrades and cookie affinity for negotiation, WebSocket, and fallback requests. Restore authorized conversation subscriptions after reconnect and use durable history/change APIs to catch up. Prove cross-node channel and direct-conversation delivery in both directions, a no-backplane negative control, and node-loss reconnect/catch-up through one public endpoint. Report process-local features and worker coordination separately.
- **Avatar choices.** Add locally generated DiceBear avatars to the profile editor, alongside uploaded pictures and initials. Select styles with clear licensing and attribution.
- **Storage provider foundation.** Support Local, S3-compatible storage (including Cloudflare R2), and Azure Blob Storage through one attachment storage interface. Let administrators select an approved destination, verify its connection, and see what operations it supports. Keep a provider reference on existing attachments so a change of default leaves them readable. Move existing data through an explicit, verified migration.
- **Bidirectional storage migration.** Add an administrator wizard for copying managed chat attachments between Local, supported S3-compatible destinations (including Cloudflare R2), and Azure Blob Storage, including cloud-to-local and cloud-to-cloud moves. A dry run checks access, available space or quotas where reported, and file limits, and lists the files, bytes, and blockers. Run the copy as a background job with progress, pause, resume, and safe retries. Keep file and message IDs, access rules, and retention state intact.
- **Verified cutover and rollback.** Keep serving attachments from the source during the copy. Reconcile uploads, changes, and deletions before switching references, with a brief write pause if needed. Verify destination content and readable attachments before cutover. Retain permitted source copies for a defined rollback window. Before rolling back, reconcile new uploads and changes since cutover so nothing is lost or unintentionally restored. Require a separate audited action to clean up the source after verification and the rollback window.
- **Install, upgrade, and recover.** Document and verify a clean install, an upgrade from the previous release, rollback or recovery from a failed upgrade, and restoration of messages together with their attachments.

**Editor release check:** Verify that pasting, sending, editing, and reopening a formatted message preserves its structure. Copying a sent code block should reproduce the original snippet, including indentation and line breaks.

**Support layout release check:** Open two and four assigned chats, maximize each chat in turn, and restore the prior layout. Confirm the other pane assignments remain available after restore, the selected layout is remembered per user and organization, and the control works when the inbox is expanded or the workspace switches to its stacked layout.

**Migration release check:** Test Local → cloud → Local, a provider-to-provider move, interruption and resume, and rollback after a new upload. Failed or unsupported items remain readable at the source and appear in the migration report.

**Multi-node release check:** Run the real API on two nodes with shared PostgreSQL and Redis behind a load balancer. Confirm clients are affined to different nodes and negotiation/connection requests stay on their assigned nodes. Verify channel and direct-conversation events in both directions before history fetch, verify delivery fails with the backplane disabled, then enable it and repeat. Stop one node and verify reconnect, authorized group rejoin, durable catch-up, and subsequent live delivery.

**Pilot gate:** Core permissions, one supported storage provider, and a proven recovery path are enough to start the support pilot. DiceBear, optional SSO, and additional storage providers can ship independently.

### Implementation sequence and current status

Release 1 is still open. The rich-text/code-editor slice is implemented (Phase 19), and locally
generated avatars are available (Phase 44). The owner/admin setup checklist guides people to
branding, the default workspace, and team invitations/roles (Phase 30); verified domains and
optional organization SSO are implemented (Phases 31-34). Group-based scoped permissions now cover
private chats, support, organization management, workspace channels, and installation administration
(Phases 36-42). Guest access expiry and the existing invitation expiry are visible in management
(Phase 43). Local, S3-compatible, and Azure Blob attachment storage are configured through the admin
provider page (Phase 45). Phase 46 adds effective-access reviews for installation, organization,
and workspace contexts in each admin user's details, including inherited group-to-role paths,
permission decisions, Deny precedence, and existing policy outcomes. Phase 47 adds a non-migrating
storage preflight with inventory totals, per-file source checks, and a destination access probe.
Phase 48 adds persistent attachment-copy jobs with per-file SHA-256 and size verification, progress,
pause/resume, and retry after transient failures. Phase 49 adds role-gated, provider-backed message
translation with saved original/translated text. Phase 50 verifies the copied inventory, pauses new
attachment uploads while references and the default provider change, and supports a reverse copy for
rollback. Phase 51 verifies a clean SQLite install and a full database-plus-attachment restore, and
confirms that both provider migration snapshots match the current model. A Release build and the
seven-check SQLite restore drill pass. A previous-version upgrade, failed-upgrade recovery, and
PostgreSQL restore drill still need a release environment. The rich-text acceptance check and a live
Azure connection test also remain to be verified. Phase 52 adds the multi-node SignalR acceptance
scope, an HAProxy-affined PostgreSQL/Redis staging profile, and an external real-SignalR integration
harness. The Redis backplane, shared presence/rate limits, client reconnect-time group rejoin, and
Redis readiness check already exist from Phase 21; the two-node deployment proof is still pending
because the Docker daemon is unavailable in the current environment. Release 1 stays open until
these gates are closed.

The desktop support chat layout is included in Release 1. The current implementation adds a
per-chat maximize/restore control to the two- and four-chat workspace, preserves the pane assignments
when a chat is maximized, and remembers the maximized chat per user and organization.

Widget setup and support-queue work in Phases 25-29 was advanced earlier in response to the owner's
direct Release 2 requests. That work is a deliberate sequencing exception; it does not mark Release 1
complete. Work resumed on Release 1 with organization setup (Phase 30), verified domain ownership
(Phase 31), and an explicit automatic-enrollment control (Phase 32). Phase 33 added owner/admin
management of organization-scoped OIDC configuration and protected client secrets at rest. Phase
34 adds OIDC sign-in, verified identity matching, explicit domain-based enrollment, session
creation, and an admin settings panel with a shareable sign-in URL. Organization SSO now works as
an opt-in sign-in method; it does not yet force every member to use SSO. Phases 36-37 deliver the
installation-scope foundation: users belong to groups, groups receive roles, and each role
permission is Allow, Deny, or unassigned. Deny overrides grants from other roles, including built-in
roles. Existing direct custom-role assignments are migrated into groups. Phase 38 adds
organization-scoped roles and groups and applies `private_chats.use` to organization-context chats.
Phase 39 implements organization-scoped support access, case reading, and case management through
group-assigned roles. Unconfigured organizations preserve legacy support behavior; configured
decisions use effective Allow/Deny grants, with Deny taking precedence. Phase 40 implements
organization discovery, settings, member/invitation administration, role/group management, and
workspace administration through the same group-based model. Unconfigured keys preserve current
organization behavior. Phase 41 implements workspace-scoped `channels.create` and
`channels.manage` permissions, with a separate workspace roles/groups menu and group assignment.
Existing channel policy remains in force until a workspace key is configured; afterward, Allow is
required and Deny overrides other channel-manager paths. Phase 42 implements distinct installation
permissions for roles, permissions, groups, providers, and settings. Existing built-in and custom
legacy access remains effective until each corresponding granular key is configured; once configured,
the granular Allow/Deny assignment governs that capability. Phase 43 gives accepted guests a 90-day
access window, retains the existing 14-day invitation expiry, and shows both deadlines in the guest
management view. Phase 29 test execution remains a queue
verification follow-up; it does not authorize more Release 2 feature work ahead of Release 1.
Phase 44 adds six locally generated DiceBear styles to profile settings. The style and seed sync
through the profile API, while SVG rendering happens in the browser; uploaded photos and initials
remain available. The selected styles are CC0 1.0.
Phase 45 adds Azure Blob Storage to the Local and S3-compatible attachment provider choices.
Administrators can configure its private container, test connection by writing, reading, and deleting
a probe, and see that the provider supports uploads, downloads, and deletion. Each message attachment
records its provider; changing the default through administration pins legacy records to the active
provider before saving the new default. This stage does not copy existing bytes. The next storage
stages are verified cutover and rollback.
Phase 47 begins storage migration with `POST /api/v1/admin/storage-migrations/dry-run`. It lists
up to 2,000 eligible files, totals the full inventory, checks source readability, and probes the
destination with a temporary write/read/delete object. It reports provider capacity as unknown
when the backend exposes no quota API. It changes no attachment references or active provider.
Phase 48 lets an administrator start a persistent copy job after a clear preflight. Jobs copy the
same object key to Local, S3-compatible, or Azure Blob storage, verify destination bytes against the
attachment's stored size and SHA-256, expose progress, and support pause/resume. Failed files retry
with capped exponential backoff and become visible terminal failures after five attempts. A retry
overwrites the deterministic object key, making repeated copies idempotent. Attachment provider
references and the active provider remain unchanged until the separate cutover stage.
Phase 46 adds admin effective-access review contexts for installation, organization, and workspace.
Each review includes the selected scope and inherited scopes, group membership paths, and the
effective permission result. Review contexts are limited to organizations and workspaces where the
user has active membership. Existing scope policies remain visible until their permission key is
configured; Deny still overrides Allow.
Phase 49 adds role-gated, on-demand message translation. Users granted `messages.translate` through
group-assigned roles can select a target language and view the translation beside the unchanged
original. Administrators can configure multiple named translation provider profiles (OpenAI-compatible
or Ollama), test them, and select one active provider. The user sees a disclosure before sending
message text to that provider. Authorized support agents can also opt in to automatic translation of
incoming support messages in their selected language.

## Release 2: Customer support desk

Get a company from installing the website widget to handling its first support case.

- **Widget setup.** Give owners a preview, a copyable install snippet, a domain check, and a test conversation they can follow from the website to the agent inbox.
- **Routing and queues.** Assign new conversations to eligible available agents with capacity. Queue visitors when agents are busy, and let them leave a message and reply address when the team is offline. Give each visitor a case reference. Keep routing work behind a provider contract: the database is the default provider, Redis is an optional provider now, and Azure Service Bus or RabbitMQ can be added later. The database remains the source of truth for cases whichever transport is selected.
- **Agent console.** Let an agent switch among several assigned conversations, claim a queued case, transfer ownership, and resolve it. Show unread activity and distinguish queued, active, and resolved cases.
- **Case management.** Add priorities, tags, internal notes, saved replies, business hours, and first-response targets. Give supervisors a view of queue age and agent load. Keep internal notes visible only to authorized staff.
- **Customer portal and email.** Let verified visitors resume eligible cases and see their public history. Pilot email intake and threaded replies once widget routing is stable.

**Pilot gate:** Verify setup, after-hours intake, visitor reconnects, agent handoffs, capacity limits, and simultaneous pickup by two agents. Track first-response time, queue abandonment, and successful handoffs.

## Release 3: Mobile and shared media

Make everyday chat easier to follow, especially on a phone.

- **Installable mobile web app.** Add compact navigation, message deep links, reconnect indicators, and recoverable drafts for persistent conversations. Retried sends should produce one message.
- **Catch-up and notifications.** Build on Activity with **Unread**, **Mentions**, and **Needs my reply** views. Add quiet hours, per-channel controls, and separate alerts for support assignments.
- **Media gallery and playback.** Add a conversation gallery with previews and links back to source messages. Play supported audio and video files inline, with a download option for unsupported formats. Add voice notes with a preview before sending.
- **Search and shared resources.** Add saved filters, attachment filename search where supported, and shared pins for important messages and files.

**Release gate:** Verify mobile reconnect and retry behavior, playback across supported storage providers, and removal of revoked or deleted content from search, galleries, and pins.

## Release 4: Partner collaboration and data control

Help organizations bring partners into selected conversations and manage the life of their data.

- **Partner rooms.** Extend channel guest access for clients and suppliers with host-owned rooms, expiring invitations, and a clear view of what guests can access.
- **Archive and recovery.** Make personal conversation archiving reversible. Provide a defined restore window for supported deleted records and retained attachments, with clear retention and purge behavior.
- **Export and import.** Offer permission-controlled exports by conversation and date range, starting with JSON and readable HTML. Add one importer for a source used by a pilot customer, with a dry run, a report on skipped records, and safe retries.
- **Microsoft 365 and Google Workspace drives.** Add a Microsoft Graph connector for OneDrive for Business and SharePoint document libraries, plus a Google Drive API connector for Google Workspace Shared Drives. Start with selecting or linking an existing file from chat. Keep the file in its source drive and preserve the source permissions. Adding a link to a conversation does not grant drive access.
- **Managed attachments on company drives.** Pilot an administrator-selected, organization-owned SharePoint library or Google Shared Drive as an optional destination for new chat uploads. Use a dedicated destination and narrowly scoped access. Apply Knock Twice's conversation permissions to downloads through the app and document who can access the storage directly through the provider. Verify upload, download, deletion, retained-file recovery, throttling, and access after the configuring administrator leaves. Keep media on an approved object storage backend when a drive cannot meet the required playback or throughput.
- **Company drive migration.** Extend the migration wizard in both directions for qualified SharePoint and Google Shared Drive storage used for managed uploads. Report any provider limits before copying. Linked OneDrive, SharePoint, and Google Drive documents stay at their source. Move a linked document only by explicitly importing it as a managed copy with permission.
- **Business integrations.** Add scoped service accounts and signed outbound webhooks for support events. Include delivery history, retries, and replay. Prove one integration with a pilot customer before expanding coverage.
- **Ticketing connector.** Let an agent create or link an external ticket from a support case, then see its reference and status beside the conversation. Start with one pilot customer's ticketing system. Send an editable summary and only approved fields. Use stable request IDs to prevent duplicate tickets, and keep private notes and full transcripts out of the default payload. For a linked case, the ticketing system owns ticket status.
- **Operator recovery tools.** Build on Release 1's recovery procedure with backup status and restore verification for the database and stored attachments.

**Release gate:** Confirm that guest removal takes effect, restores and imports preserve valid access, exports expire, and retried imports or webhook deliveries can be handled without duplicates.

## Release 5: AI for conversations and business teams

Give organizations AI tools for multilingual conversations, meeting follow-up, customer support, and department-specific work. Each organization chooses the capabilities it enables and the providers allowed to process its data.

### 5.1 Provider foundation and translation

- **Shared provider layer.** Define separate capabilities for translation, speech-to-text, and text generation or retrieval. Let administrators configure a provider and model for each capability, so they can switch one workflow independently or use an approved self-hosted endpoint where compatible.
- **Bring your own key (BYOK).** Support organization-owned credentials kept in server-side secret storage. Add a connection test, key rotation, per-capability budgets and usage reporting. Show administrators which provider receives each content type and whether processing is local or external. Provider or model fallback must be explicitly approved by the organization.
- **Message translation.** Start with on-demand translation of eligible messages. Show the translation beside the original and keep code blocks, links, and formatting intact. Add a configurable multilingual support workflow after validating translation quality with pilot users.

### 5.2 Transcription and meeting follow-up

- **Transcribe uploaded audio.** Start with voice notes and meeting recordings already shared in an authorized conversation. Use background jobs with progress and retry. Show a timestamped transcript linked to the recording, with speaker labels where supported. Let authorized users correct the transcript and jump from a line to playback.
- **Meeting notes.** Generate a draft summary, decisions, and action items with timestamps back to the source. Let people review and correct the draft before sharing it. Make approved transcripts searchable within the source conversation's access scope.
- **Recording sources.** Begin with uploaded files and validate that the organization permits transcription of the recording. Evaluate meeting platform imports after the upload workflow is proven. Live capture and native calling are separate decisions.

### 5.3 AI support across website chat and ticketing

- **AI participants.** Add clearly labeled assistants that administrators can enable and scope. Start with reply drafts, conversation summaries, and editable handoff briefs for human agents.
- **Approved knowledge and insights.** Ground answers in selected FAQs, help articles, and authorized documents. Include source links and flag missing evidence. Add on-demand topic tags and support trend reports. Require human approval for customer replies and actions during the pilot.

- **Customer-facing website chat.** Let organizations enable a clearly identified AI support agent in the widget, with a visible option to reach a human. During the supervised pilot, the agent collects structured intake and drafts answers for human approval. After validation, allow explicitly enabled answers to routine questions from approved help content. Hand off when the customer asks, the agent lacks an answer, or the issue requires a person. Carry the conversation and a short summary into the support queue, including after hours.
- **Ticket actions.** Let the assistant prepare an editable ticket with the issue summary, category, and relevant context from the chat. A human agent approves creation or updates during the pilot. Once customer identity and case access are verified, the assistant may report customer-visible ticket status. Keep ticket changes behind scoped permissions and record who approved them. Continue accepting cases into Knock Twice if the connector is down, and show pending or failed ticket actions on the agent side.
- **End-to-end pilot.** Verify website intake, an AI answer draft, human takeover, ticket creation and linking, and customer status lookup. Check that retries do not create duplicates, unverified visitors cannot view ticket history, and internal notes stay out of the widget and outbound ticket by default.

### 5.4 Department assistants for business teams

- **Specialized AI participants.** Let authorized administrators create clearly labeled AI participants such as Accounting Assistant and Finance Assistant. Assign each to approved workspaces or channels, and let people invoke it by mention or direct conversation where permitted. Configure approved sources, capabilities, and tools per assistant. Each uses the organization's provider and BYOK controls.
- **Accounting pilot.** Answer internal invoice and expense policy questions with citations, flag missing invoice fields for review, and draft month-end close checklists. Start with approved policy documents and read-only access to specifically authorized invoice records.
- **Finance pilot.** Summarize approved budget versus actual reports, surface variances with links to the underlying figures, and draft explanations for a finance reviewer. Keep source period and currency visible. Use reported values and deterministic calculations for figures, with AI used to explain and summarize them.
- **Permissions and approvals.** Scope each request to what both the requester and assistant may access. Check permissions before retrieval and before sharing output in a channel, so confidential figures stay within the authorized audience. Start with read-only tools and drafts. Posting to a ledger, approving invoices, and moving funds are outside the pilot. Record assistant use and review decisions without copying sensitive content into routine logs.

**Release gate:** Check that translations preserve the original, transcripts and summaries inherit source access and retention rules, and source removal is reflected in derived results. Temporary private chats stay outside all AI processing. Verify that disabling a capability stops new or queued dispatches, credentials stay out of clients and logs, and provider failures leave core chat and support usable. Review translation and transcript quality, citation accuracy, draft acceptance, and cost before expanding each pilot. Verify that department assistants cannot read across organizations or share restricted records with unauthorized channel members.

## Product boundaries

Temporary private chats stay outside persistent search, media indexing, offline storage, archive, recovery, export, and AI processing. Organization and conversation permissions apply to messages, attachments, search, integrations, and background jobs. Temporary private chat files stay out of persistent storage providers and company drives.

Native mobile apps, live calls, screen sharing, federation, and unattended AI actions are candidates for later releases.

## Storage integration references

- [Microsoft Graph: Working with files in OneDrive and SharePoint](https://learn.microsoft.com/graph/api/resources/onedrive?view=graph-rest-1.0)
- [Microsoft Graph: Selected permissions for OneDrive and SharePoint](https://learn.microsoft.com/graph/permissions-selected-overview)
- [Google Drive API: Shared drives](https://developers.google.com/workspace/drive/api/guides/about-shareddrives)

## Feedback

Open an issue to share a use case, request a feature, or describe a gap in a planned release. Include who needs it, what they are trying to accomplish, and how they handle it today.
