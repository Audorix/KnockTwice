# Knock Twice Roadmap

Knock Twice by Audorix is a self-hosted messaging platform for team conversations, customer support, and collaboration with clients and partners. This roadmap describes the next five planned releases. The numbers show the order of delivery and are independent of software version tags. Scope and timing may change as we learn from pilots.

## Planned releases

| Release | Focus | What it makes possible |
| :-- | :-- | :-- |
| **1** | [Company setup and access](#release-1-company-setup-and-access) | Set up a workspace, control access, and send formatted messages and code snippets. |
| **2** | [Customer support desk](#release-2-customer-support-desk) | Turn website conversations into support cases a team can manage together. |
| **3** | [Mobile and shared media](#release-3-mobile-and-shared-media) | Keep up with conversations on mobile and find or play shared media in context. |
| **4** | [Partner collaboration and data control](#release-4-partner-collaboration-and-data-control) | Work with external partners, connect business systems, and move or recover conversation data. |
| **5** | [AI-assisted support](#release-5-ai-assisted-support) | Give agents source-backed drafts and handoff summaries they can review. |

## Release 1: Company setup and access

Make Knock Twice easier to set up and give teams the tools to write clear business messages and share technical details.

- **Rich text and code editor (business priority).** Add a formatting toolbar to the chat composer for bold, italics, links, lists, and quotes. Support inline code and a dedicated code block editor with language selection, syntax highlighting, preserved indentation and line breaks, and a copy button on sent snippets. Use it for procedures, incident updates, logs, and code review discussions. Render snippets as text and keep existing messages readable.
- **Organization setup.** Guide owners through branding, the first workspace, role assignment, and invitations. Add verified domains and optional organization SSO. Automatic enrollment requires an explicit administrator choice.
- **Granular permissions.** Add scoped roles and groups at installation, organization, and workspace levels. For example, an organization can restrict private chat to an approved group. Add effective-access reviews, guest and invitation expiry, and auditable changes to permissions.
- **Avatar choices.** Add locally generated DiceBear avatars to the profile editor, alongside uploaded pictures and initials. Select styles with clear licensing and attribution.
- **Storage providers.** Keep Local storage available, qualify Cloudflare R2 through the S3-compatible storage layer, and add Azure Blob Storage. Verify that changing providers preserves access to existing attachments through retained provider references or an explicit migration.
- **Install, upgrade, and recover.** Document and verify a clean install, an upgrade from the previous release, rollback or recovery from a failed upgrade, and restoration of messages together with their attachments.

**Editor release check:** Verify that pasting, sending, editing, and reopening a formatted message preserves its structure. Copying a sent code block should reproduce the original snippet, including indentation and line breaks.

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
- **Business integrations.** Add scoped service accounts and signed outbound webhooks for support events. Include delivery history, retries, and replay. Prove one integration with a pilot customer before expanding coverage.
- **Operator recovery tools.** Build on Release 1's recovery procedure with backup status and restore verification for the database and stored attachments.

**Release gate:** Confirm that guest removal takes effect, restores and imports preserve valid access, exports expire, and retried imports or webhook deliveries can be handled without duplicates.

## Release 5: AI-assisted support

Start with opt-in AI assistants that help human agents answer and hand off cases.

- **AI participants.** Add clearly labeled assistants that organization administrators can enable and scope. Start with drafts and summaries in a supervised support pilot.
- **Approved knowledge sources.** Ground drafts in selected FAQs, help articles, and authorized documents. Include source links and flag when the available material does not support an answer.
- **Handoffs and analysis.** Give agents editable handoff briefs with the issue, key facts, and open questions. Add on-demand summaries, topic tags, and support trend reports.
- **Governance and quality.** Use a provider-neutral interface with organization-level limits and data controls. Require human approval for customer replies and actions during the pilot. Review draft acceptance, citation accuracy, cost per assisted case, and handoff completion before expanding access.

**Pilot gate:** Verify that assistants respect organization and conversation permissions, disabling AI stops processing, and agents can continue working if the AI provider is unavailable.

## Product boundaries

Temporary private chats stay outside persistent search, media indexing, offline storage, archive, recovery, export, and AI processing. Organization and conversation permissions apply to messages, attachments, search, integrations, and background jobs.

Native mobile apps, live calls, screen sharing, federation, and unattended AI actions are candidates for later releases.

## Feedback

Open an issue to share a use case, request a feature, or describe a gap in a planned release. Include who needs it, what they are trying to accomplish, and how they handle it today.
