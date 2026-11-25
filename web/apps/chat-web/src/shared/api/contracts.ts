/**
 * TypeScript mirrors of Audorix.KnockTwice.Contracts wire types. Keep in sync with
 * src/Core/Audorix.KnockTwice.Contracts (until an OpenAPI-generated client replaces them).
 */

export type HealthStatus = 'healthy' | 'degraded' | 'unhealthy';

export interface HealthCheckEntry {
  name: string;
  status: HealthStatus;
}

/** Body of GET /health/live and GET /health/ready. */
export interface HealthReportResponse {
  status: HealthStatus;
  checks: HealthCheckEntry[];
}

/** RFC 9457 problem document returned for API errors. */
export interface ProblemDocument {
  type?: string;
  title?: string;
  status?: number;
  code: string;
  traceId?: string;
  /** Present for validation_failed: request field → field-level error codes. */
  errors?: Record<string, string[]>;
  /** Present for setup_settings_invalid: configuration messages naming settings (never their values). */
  details?: string[];
}

export interface CsrfTokenResponse {
  token: string;
  headerName: string;
}

export interface AuthConfigurationResponse {
  registrationEnabled: boolean;
  requireConfirmedEmail: boolean;
  passwordMinimumLength: number;
  captcha: { provider: 'turnstile' | 'none'; siteKey: string | null };
  externalProviders: ('google' | 'microsoft')[];
  /** Development mode: registration creates a confirmed account and sends no email. */
  autoConfirmEmail: boolean;
  /** Installation administrators may populate demo data (development installations only). */
  demoDataEnabled: boolean;
}

// ---- Setup wizard (/api/v1/setup) ---------------------------------------------------------

export interface SetupStatusResponse {
  required: boolean;
  completed: boolean;
}

export type DatabaseProvider = 'Sqlite' | 'PostgreSql';
export type EmailProvider = 'Smtp' | 'Postmark';
export type CaptchaProvider = 'Turnstile' | 'None';

export interface SetupConfigurationResponse {
  environment: string;
  database: {
    locked: boolean;
    provider: DatabaseProvider | null;
    supportedProviders: DatabaseProvider[];
    sqlitePath: string;
    postgreSqlHost: string;
    postgreSqlPort: number;
    postgreSqlDatabase: string;
  };
  email: {
    provider: EmailProvider;
    fromAddress: string;
    fromName: string;
    smtpHost: string;
    smtpPort: number;
    smtpSecurity: string;
    smtpSecurityModes: string[];
  };
  options: {
    developmentMode: boolean;
    publicBaseUrl: string | null;
    defaultLanguage: string;
    supportedLanguages: string[];
    captchaProvider: CaptchaProvider;
    captchaKeysConfigured: boolean;
  };
  passwordMinimumLength: number;
}

export interface SetupDatabaseInput {
  provider: DatabaseProvider;
  sqlite?: { path: string };
  postgreSql?: { host: string; port: number; database: string; username: string; password: string; sslMode: string };
}

export interface SetupEmailInput {
  provider: EmailProvider;
  fromAddress: string;
  fromName: string;
  smtp?: { host: string; port: number; security: string; username: string; password: string };
  postmark?: { serverToken: string; messageStream: string };
}

export interface SetupCompleteRequest {
  database: SetupDatabaseInput;
  email: SetupEmailInput;
  administrator: { email: string; displayName: string; password: string };
  options: {
    developmentMode: boolean;
    publicBaseUrl: string;
    defaultLanguage: string;
    captchaProvider: CaptchaProvider;
    captchaSiteKey: string;
    captchaSecretKey: string;
  };
}

export interface SetupDatabaseTestResponse {
  databaseExists: boolean;
  result: 'connected' | 'will_be_created';
}

export interface SetupCompleteResponse {
  administratorEmail: string;
  restarting: boolean;
}

export interface CurrentUserResponse {
  id: string;
  email: string;
  emailConfirmed: boolean;
  displayName: string;
  avatar: AvatarResponse;
  preferences: UserPreferences;
  hasPassword: boolean;
  externalProviders: string[];
  roles: string[];
  permissions: string[];
  /** Display ID, shown as @handle (2026-11-24). */
  handle?: string | null;
}

/** A contact's drawer details (2026-11-24): the email only when they share it. */
export interface ContactDetailsResponse {
  handle: string | null;
  email: string | null;
  emailShared: boolean;
}

/** Initials, and the profile photo's address when the person has one (redesign 2026-11). */
export interface AvatarResponse {
  initials: string;
  photoUrl?: string | null;
}

export interface UserSummary {
  id: string;
  displayName: string;
  avatar: AvatarResponse;
  /** The account was deleted (Phase 13); the client shows a localized "Deleted user". */
  deleted?: boolean;
}

/** A message found by search (Phase 14), with its conversation. */
export interface SearchResultResponse {
  conversation: ConversationSummaryResponse;
  message: ConversationEntryResponse;
}

export interface SearchResponse {
  /** Newest first (or oldest first with order=oldest). */
  items: SearchResultResponse[];
  nextCursor: string | null;
  /** First page only: all matches, and those with files, whatever the files filter. */
  counts?: SearchCountsResponse | null;
}

export interface SearchCountsResponse {
  total: number;
  withFiles: number;
}

/** A message and its neighbours in the main timeline, ascending (redesign 2026-11). */
export interface MessageContextResponse {
  items: ConversationEntryResponse[];
  hasOlder: boolean;
  hasNewer: boolean;
}

/** What deleting the signed-in account involves (Phase 13). */
export interface AccountDeletionCheckResponse {
  canDelete: boolean;
  blockingOrganizations: { organizationId: string; name: string }[];
  lastAdministrator: boolean;
  /** Confirm with the password; otherwise (external sign-in only) with the email address. */
  requiresPassword: boolean;
}

export interface ContactResponse {
  user: UserSummary;
  sinceUtc: string;
  /** Organizations both of you belong to (contacts list; null in the blocked list). */
  sharedOrganizations?: { id: string; name: string }[] | null;
}

export interface ContactRequestResponse {
  id: string;
  direction: 'incoming' | 'outgoing';
  user: UserSummary;
  createdAtUtc: string;
}

export type OrganizationRole = 'owner' | 'administrator' | 'member';
export type WorkspaceRole = 'administrator' | 'member';

// ---- Conversations (/api/v1/conversations, Phase 5) --------------------------------------

export type ParticipantRole = 'owner' | 'administrator' | 'member';

export interface ConversationSummaryResponse {
  id: string;
  /** "website": a visitor from an embedded widget (Phase 10), answered by the widget's responders. */
  type: 'direct' | 'group' | 'website' | 'channel';
  scope: 'personal' | 'organization';
  organizationId: string | null;
  organizationName: string | null;
  /** Group name, or the other person's name for direct conversations. */
  title: string;
  counterpart: UserSummary | null;
  /** "guest" for guests in channels (Phase 11). */
  myRole: ParticipantRole | 'guest';
  muted: boolean;
  lastSequence: number;
  lastReadSequence: number;
  unreadCount: number;
  lastActivityAtUtc: string;
  /** Channels (Phase 11). */
  workspaceId?: string | null;
  visibility?: 'public' | 'private' | null;
  /** Website conversations (Phase 11 support). */
  supportStatus?: 'open' | 'pending' | 'closed' | null;
  /** Phase 17: what notifies you here; `muted` is true for anything but "all". */
  notificationLevel?: NotificationLevel;
  /** Redesign 2026-11: pinned to the top of your own list. */
  pinned?: boolean;
  /** Redesign 2026-11: the newest message you may see (list endpoints only). */
  lastMessage?: ConversationLastMessage | null;
  /** Redesign 2026-11: an archived channel is read-only. */
  archivedAtUtc?: string | null;
}

/** Excerpt is plain text; empty for a message with only files; null when deleted. */
export interface ConversationLastMessage {
  id: string;
  author: UserSummary | null;
  excerpt: string | null;
  deleted: boolean;
  createdAtUtc: string;
}

/** A file shared in a conversation (redesign 2026-11). */
export interface SharedFileResponse {
  file: { id: string; fileName: string; contentType: string; kind: string; size: number };
  messageId: string;
  sequence: number;
  author: UserSummary | null;
  sharedAtUtc: string;
}

export interface SharedFilesResponse {
  items: SharedFileResponse[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface ConversationParticipantResponse {
  user: UserSummary;
  /** "guest" for guests in channels (Phase 11). */
  role: ParticipantRole | 'guest';
  joinedAtUtc: string;
  /** ADR-023 watermarks: received up to / read up to this sequence. */
  lastDeliveredSequence: number;
  lastReadSequence: number;
}

export interface ConversationDetailResponse {
  conversation: ConversationSummaryResponse;
  description: string | null;
  participants: ConversationParticipantResponse[];
}

export type ConversationEntryKind =
  | 'message'
  | 'created'
  | 'member_added'
  | 'member_removed'
  | 'member_left'
  | 'renamed'
  | 'description_changed'
  | 'role_changed'
  | 'assigned'
  | 'status_changed'
  | 'visibility_changed'
  | 'archived'
  | 'pinned';

export interface ConversationEntryResponse {
  id: string;
  sequence: number;
  kind: ConversationEntryKind;
  actor: UserSummary | null;
  subject: UserSummary | null;
  text: string | null;
  createdAtUtc: string;
  /** Message text; null for other kinds and deleted messages. */
  body?: string | null;
  replyTo?: ReplyPreview | null;
  clientMessageId?: string | null;
  /** Highest version wins (edits and deletion increase it). */
  version?: number;
  editedAtUtc?: string | null;
  deletedAtUtc?: string | null;
  /** Files sent with the message (ready ones only; none on deleted messages). */
  attachments?: MessageAttachmentResponse[] | null;
  /** Phase 16: emoji reactions, in the order they were first used. */
  reactions?: ReactionResponse[] | null;
  /** Phase 16: set on a thread reply (the thread's first message). */
  threadRootId?: string | null;
  /** A thread reply that is also shown in the main conversation. */
  shownInConversation?: boolean;
  /** Set on a message that has a thread. */
  thread?: ThreadSummaryResponse | null;
  /** Phase 17: who the message mentions. */
  mentions?: MentionsResponse | null;
  /** Phase 18: pinned in its conversation. */
  pinned?: boolean;
  /** Phase 18: a forwarded copy; the original's author. */
  forwardedFrom?: UserSummary | null;
  /** Phase 18: saved by you (absent in realtime events). */
  saved?: boolean | null;
}

export interface PinnedMessageResponse {
  entry: ConversationEntryResponse;
  pinnedBy: UserSummary | null;
  pinnedAtUtc: string;
}

export interface SavedItemResponse {
  conversationId: string;
  conversationType: string;
  conversationTitle: string | null;
  entry: ConversationEntryResponse;
  savedAtUtc: string;
}

export interface SavedPageResponse {
  items: SavedItemResponse[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface MentionsResponse {
  userIds: string[];
  everyone: boolean;
  here: boolean;
}

export type NotificationLevel = 'all' | 'mentions' | 'nothing';

export interface MentionItemResponse {
  conversationId: string;
  conversationType: string;
  conversationTitle: string | null;
  entry: ConversationEntryResponse;
  unread: boolean;
}

export interface MentionPageResponse {
  items: MentionItemResponse[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface ReactionResponse {
  emoji: string;
  count: number;
  userIds: string[];
}

export interface ThreadSummaryResponse {
  replyCount: number;
  lastReplyAtUtc: string | null;
  /** The caller's state; absent in realtime events. */
  following: boolean | null;
  unread: number | null;
}

export interface ThreadResponse {
  root: ConversationEntryResponse;
  items: ConversationEntryResponse[];
  nextCursor: string | null;
  hasMore: boolean;
  following: boolean;
  lastReadSequence: number;
}

export interface ReactionChangedEvent {
  version: number;
  conversationId: string;
  messageId: string;
  reactions: ReactionResponse[];
}

export type ThemePreference = 'system' | 'light' | 'dark';

export interface UserPreferences {
  uiLanguage: string;
  /** Missing in responses from servers older than Phase 8. */
  theme?: ThemePreference;
  desktopNotifications?: boolean;
  notificationPreviews?: boolean;
  /** Phase 15: unread-message emails. */
  emailNotifications?: 'batched' | 'daily' | 'off';
  /** Contacts see the email address (2026-11-24; off by default). */
  shareEmailWithContacts?: boolean;
}

export type AttachmentKind = 'image' | 'file';

export interface MessageAttachmentResponse {
  id: string;
  fileName: string;
  /** Detected from the content by the server. */
  contentType: string;
  kind: AttachmentKind;
  size: number;
}

export type AttachmentStatus = 'pending_upload' | 'quarantined' | 'ready' | 'rejected' | 'deleted';

export interface AttachmentResponse extends MessageAttachmentResponse {
  conversationId: string;
  status: AttachmentStatus;
  messageId: string | null;
  createdAtUtc: string;
}

export interface AttachmentLimitsResponse {
  maxFileSize: number;
  maxPerMessage: number;
  /** Bytes per person; 0 = unlimited. */
  userQuota: number;
  allowedExtensions: string[];
  scanningEnabled: boolean;
}

/** A private chat: in server memory only, for as long as the initiator's sign-in session lasts. */
export interface PrivateChatResponse {
  id: string;
  status: 'invited' | 'active';
  isInitiator: boolean;
  counterpart: UserSummary | null;
  initiator: UserSummary | null;
  createdAtUtc: string;
  acceptedAtUtc: string | null;
  invitationExpiresAtUtc: string;
  usedBytes: number;
  maxBytes: number;
  maxFileSize: number;
}

export interface PrivateAttachmentResponse {
  id: string;
  fileName: string;
  contentType: string;
  kind: AttachmentKind;
  size: number;
}

export interface PrivateMessageResponse {
  id: string;
  sequence: number;
  author: UserSummary | null;
  authorId: string;
  clientMessageId: string;
  body: string;
  attachments: PrivateAttachmentResponse[];
  createdAtUtc: string;
}

export interface PrivateChatDetailResponse {
  chat: PrivateChatResponse;
  messages: PrivateMessageResponse[];
}

export interface ReplyPreview {
  id: string;
  sequence: number;
  author: UserSummary | null;
  excerpt: string | null;
  deleted: boolean;
}

export interface ConversationHistoryResponse {
  items: ConversationEntryResponse[];
  nextCursor: string | null;
  hasMore: boolean;
}

/** A page of the conversation list (Phase 22). Pass nextCursor as `after` for the next page. */
export interface ConversationListPageResponse {
  items: ConversationSummaryResponse[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface ConversationReadResponse {
  lastReadSequence: number;
  unreadCount: number;
}

export interface OrganizationResponse {
  id: string;
  name: string;
  myRole: OrganizationRole;
  createdAtUtc: string;
  myPermissions: string[];
  /** Phase 11: the caller is a support agent; organization configuration. Missing from older servers. */
  supportAgent?: boolean;
  guestsEnabled?: boolean;
  channelCreation?: 'members' | 'administrators';
  /** Square picture and wide background image (2026-11-24); versioned addresses, null when none. */
  photoUrl?: string | null;
  bannerUrl?: string | null;
}

export interface OrganizationMemberResponse {
  user: UserSummary;
  role: OrganizationRole;
  joinedAtUtc: string;
  supportAgent?: boolean;
}

export interface InvitationResponse {
  id: string;
  email: string;
  role: OrganizationRole;
  createdAtUtc: string;
  expiresAtUtc: string;
}

export interface OrganizationMembersResponse {
  members: OrganizationMemberResponse[];
  invitations: InvitationResponse[] | null;
}

export interface MyInvitationResponse {
  id: string;
  organizationId: string;
  organizationName: string;
  role: OrganizationRole;
  invitedBy: UserSummary | null;
  expiresAtUtc: string;
}

export interface WorkspaceResponse {
  id: string;
  organizationId: string;
  name: string;
  myRole: WorkspaceRole | null;
  createdAtUtc: string;
}

export interface WorkspaceMemberResponse {
  user: UserSummary;
  role: WorkspaceRole;
  joinedAtUtc: string;
}

export interface SessionResponse {
  id: string;
  description: string;
  createdAtUtc: string;
  lastSeenAtUtc: string;
  expiresAtUtc: string;
  persistent: boolean;
  current: boolean;
}

// ---- Website widgets (Phase 10) ------------------------------------------------------------

export type WidgetPosition = 'right' | 'left';
export type WidgetLanguage = 'auto' | 'en' | 'es';

/** A widget as its organization's managers see it; the signing secret is never included. */
export interface WidgetResponse {
  id: string;
  organizationId: string;
  name: string;
  title: string;
  welcomeMessage: string | null;
  accentColor: string;
  position: WidgetPosition;
  defaultLanguage: WidgetLanguage;
  allowAnonymous: boolean;
  enabled: boolean;
  allowedOrigins: string[];
  responders: UserSummary[];
  createdAtUtc: string;
  updatedAtUtc: string;
}

/** Returned once on creation and on secret rotation. */
export interface WidgetSecretResponse {
  widget: WidgetResponse;
  signingSecret: string;
}

/** Null or missing fields stay unchanged. */
export interface UpdateWidgetRequest {
  name?: string;
  title?: string;
  welcomeMessage?: string;
  accentColor?: string;
  position?: WidgetPosition;
  defaultLanguage?: WidgetLanguage;
  allowAnonymous?: boolean;
  allowedOrigins?: string[];
  enabled?: boolean;
  responderIds?: string[];
}

export interface WidgetConfigResponse {
  id: string;
  title: string;
  welcomeMessage: string | null;
  accentColor: string;
  position: WidgetPosition;
  defaultLanguage: WidgetLanguage;
  allowAnonymous: boolean;
  /** 0 when attachments are off. */
  maxFileSize: number;
}

export interface VisitorSessionResponse {
  token: string;
  expiresAtUtc: string;
  visitorId: string;
  displayName: string;
  identified: boolean;
}

export interface VisitorConversationResponse {
  conversationId: string | null;
  visitorUserId: string;
  items: ConversationEntryResponse[];
  nextCursor: string | null;
  hasMore: boolean;
  responders: UserSummary[];
  lastReadSequence: number;
}

// ---- Business communication (Phase 11) ----------------------------------------------------

export type ChannelVisibility = 'public' | 'private';

export interface TeamSummary {
  id: string;
  name: string;
}

export interface ChannelResponse {
  id: string;
  organizationId: string;
  workspaceId: string;
  workspaceName: string;
  name: string;
  description: string | null;
  visibility: ChannelVisibility;
  memberCount: number;
  joined: boolean;
  /** manager | member | guest, or null when the caller does not take part. */
  myRole: 'manager' | 'member' | 'guest' | null;
  canManage: boolean;
  teams: TeamSummary[];
  createdAtUtc: string;
  lastActivityAtUtc: string;
  /** Redesign 2026-11: set while the channel is archived (read-only). */
  archivedAtUtc?: string | null;
}

export interface ChannelMemberResponse {
  user: UserSummary;
  role: 'manager' | 'member' | 'guest';
  joinedAtUtc: string;
}

export interface ChannelDetailResponse {
  channel: ChannelResponse;
  members: ChannelMemberResponse[];
}

export interface TeamResponse {
  id: string;
  workspaceId: string;
  name: string;
  description: string | null;
  memberCount: number;
  myRole: 'manager' | 'member' | null;
  canManage: boolean;
  createdAtUtc: string;
}

export interface TeamMemberResponse {
  user: UserSummary;
  role: 'manager' | 'member';
  joinedAtUtc: string;
}

export interface GuestInvitationResponse {
  id: string;
  email: string;
  channelId: string;
  channelName: string;
  invitedBy: UserSummary | null;
  createdAtUtc: string;
  expiresAtUtc: string;
}

export interface MyGuestInvitationResponse {
  id: string;
  organizationId: string;
  organizationName: string;
  channelId: string;
  channelName: string;
  invitedBy: UserSummary | null;
  expiresAtUtc: string;
}

export interface GuestResponse {
  user: UserSummary;
  joinedAtUtc: string;
  channels: { id: string; name: string }[];
}

/** `closed` is shown as "Resolved"; `pending` waits for the customer (redesign 2026-11). */
export type SupportStatus = 'open' | 'pending' | 'closed';

export interface SupportConversationResponse {
  conversationId: string;
  visitorName: string;
  widgetId: string | null;
  widgetName: string | null;
  status: SupportStatus;
  assignedTo: UserSummary | null;
  participating: boolean;
  unreadCount: number;
  createdAtUtc: string;
  lastActivityAtUtc: string;
  /** What the conversation is about, set by support staff. */
  subject?: string | null;
}

export interface SupportInboxItemResponse {
  conversation: SupportConversationResponse;
  /** The subject, or the first message while there is none. */
  title: string | null;
  lastMessage: { text: string; fromCustomer: boolean; sentAtUtc: string } | null;
}

export interface SupportInboxPageResponse {
  items: SupportInboxItemResponse[];
  total: number;
  counts: { open: number; pending: number; closed: number; all: number };
}

/** An internal note: visible to the support team, never to the customer. */
export interface SupportNoteResponse {
  id: string;
  conversationId: string;
  author: UserSummary | null;
  body: string;
  createdAtUtc: string;
}
