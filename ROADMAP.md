# Knock Twice Roadmap

Knock Twice by Audorix is a self-hosted messaging platform for team conversations, customer support, and collaboration with clients and partners. This roadmap describes the next five planned releases. The numbers show the order of delivery and are independent of software version tags. Scope and timing may change as we learn from pilots.

## Planned releases

| Release | Focus | What it makes possible |
| :-- | :-- | :-- |
| **1** | [Company setup and access](#release-1-company-setup-and-access) | Set up a workspace, control access, and send formatted messages and code snippets. |
| **2** | [Customer support desk](#release-2-customer-support-desk) | Turn website conversations into support cases a team can manage together. |
| **3** | [Mobile and shared media](#release-3-mobile-and-shared-media) | Keep up with conversations on mobile and find or play shared media in context. |
| **4** | [Partner collaboration and data control](#release-4-partner-collaboration-and-data-control) | Work with external partners, connect company drives and business systems, and move or recover conversation data. |
| **5** | [AI for conversations and business teams](#release-5-ai-for-conversations-and-business-teams) | Translate messages, turn recordings into searchable notes, and give teams specialized assistants, including a support agent that can work with tickets. |

## Release 1: Company setup and access

Make Knock Twice easier to set up and give teams the tools to write clear business messages and share technical details.

- **Rich text and code editor (business priority).** Add a formatting toolbar to the chat composer for bold, italics, links, lists, and quotes. Support inline code and a dedicated code block editor with language selection, syntax highlighting, preserved indentation and line breaks, and a copy button on sent snippets. Use it for procedures, incident updates, logs, and code review discussions. Render snippets as text and keep existing messages readable.
- **Organization setup.** Guide owners through branding, the first workspace, role assignment, and invitations. Add verified domains and optional organization SSO. Automatic enrollment requires an explicit administrator choice.
- **Granular permissions.** Add scoped roles and groups at installation, organization, and workspace levels. For example, an organization can restrict private chat to an approved group. Add effective-access reviews, guest and invitation expiry, and auditable changes to permissions.
- **Avatar choices.** Add locally generated DiceBear avatars to the profile editor, alongside uploaded pictures and initials. Select styles with clear licensing and attribution.
- **Storage provider foundation.** Support Local, S3-compatible storage (including Cloudflare R2), and Azure Blob Storage through one attachment storage interface. Let administrators select an approved destination, verify its connection, and see what operations it supports. Keep a provider reference on existing attachments so a change of default leaves them readable. Move existing data through an explicit, verified migration.
- **Bidirectional storage migration.** Add an administrator wizard for copying managed chat attachments between Local, supported S3-compatible destinations (including Cloudflare R2), and Azure Blob Storage, including cloud-to-local and cloud-to-cloud moves. A dry run checks access, available space or quotas where reported, and file limits, and lists the files, bytes, and blockers. Run the copy as a background job with progress, pause, resume, and safe retries. Keep file and message IDs, access rules, and retention state intact.
- **Verified cutover and rollback.** Keep serving attachments from the source during the copy. Reconcile uploads, changes, and deletions before switching references, with a brief write pause if needed. Verify destination content and readable attachments before cutover. Retain permitted source copies for a defined rollback window. Before rolling back, reconcile new uploads and changes since cutover so nothing is lost or unintentionally restored. Require a separate audited action to clean up the source after verification and the rollback window.
- **Install, upgrade, and recover.** Document and verify a clean install, an upgrade from the previous release, rollback or recovery from a failed upgrade, and restoration of messages together with their attachments.

**Editor release check:** Verify that pasting, sending, editing, and reopening a formatted message preserves its structure. Copying a sent code block should reproduce the original snippet, including indentation and line breaks.

**Migration release check:** Test Local → cloud → Local, a provider-to-provider move, interruption and resume, and rollback after a new upload. Failed or unsupported items remain readable at the source and appear in the migration report.

**Pilot gate:** Core permissions, one supported storage provider, and a proven recovery path are enough to start the support pilot. DiceBear, optional SSO, and additional storage providers can ship independently.

## Release 2: Customer support desk

Get a company from installing the website widget to handling its first support case.

- **Widget setup.** Give owners a preview, a copyable install snippet, a domain check, and a test conversation they can follow from the website to the agent inbox.
- **Routing and queues.** Assign new conversations to eligible available agents with capacity. Queue visitors when agents are busy, and let them leave a message and reply address when the team is offline. Give each visitor a case reference.
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
