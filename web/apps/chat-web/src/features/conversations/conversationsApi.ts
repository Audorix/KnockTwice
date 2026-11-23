import type {
  ConversationDetailResponse,
  ConversationEntryResponse,
  ConversationHistoryResponse,
  ConversationReadResponse,
  ConversationSummaryResponse,
  MentionPageResponse,
  NotificationLevel,
  ParticipantRole,
  PinnedMessageResponse,
  SavedPageResponse,
  ReactionResponse,
  ThreadResponse,
} from '../../shared/api/contracts';
import { getJson, sendJson } from '../../shared/api/httpClient';

/** Typed calls to /api/v1/conversations. */

const base = (conversationId: string) => `/api/v1/conversations/${encodeURIComponent(conversationId)}`;

export async function listConversations(): Promise<ConversationSummaryResponse[]> {
  return (await getJson<ConversationSummaryResponse[]>('/api/v1/conversations')).body ?? [];
}

/** Opens (or creates) the direct conversation; pass organizationId for an organization conversation. */
export async function openDirect(userId: string, organizationId?: string): Promise<ConversationSummaryResponse> {
  return (await sendJson<ConversationSummaryResponse>('POST', '/api/v1/conversations/direct', { userId, organizationId: organizationId ?? null }))!;
}

export async function createGroup(title: string, userIds: string[], organizationId?: string): Promise<ConversationSummaryResponse> {
  return (await sendJson<ConversationSummaryResponse>('POST', '/api/v1/conversations/groups', { title, userIds, organizationId: organizationId ?? null }))!;
}

export async function getConversation(conversationId: string): Promise<ConversationDetailResponse> {
  return (await getJson<ConversationDetailResponse>(base(conversationId))).body!;
}

export async function getHistory(conversationId: string, before?: string): Promise<ConversationHistoryResponse> {
  const query = before ? `?before=${encodeURIComponent(before)}` : '';
  return (await getJson<ConversationHistoryResponse>(`${base(conversationId)}/history${query}`)).body!;
}

export async function updateConversation(conversationId: string, changes: { title?: string; description?: string }): Promise<ConversationDetailResponse> {
  return (await sendJson<ConversationDetailResponse>('PUT', base(conversationId), changes))!;
}

export async function markRead(conversationId: string, sequence: number): Promise<ConversationReadResponse> {
  return (await sendJson<ConversationReadResponse>('POST', `${base(conversationId)}/read`, { sequence }))!;
}

export const setMuted = (conversationId: string, muted: boolean) => sendJson('PUT', `${base(conversationId)}/settings`, { muted });

/** Phase 17: all messages, only mentions, or nothing. */
export const setNotificationLevel = (conversationId: string, notificationLevel: NotificationLevel) =>
  sendJson('PUT', `${base(conversationId)}/settings`, { notificationLevel });

export async function listMentions(before?: string): Promise<MentionPageResponse> {
  const query = before ? `?before=${encodeURIComponent(before)}` : '';
  return (await getJson<MentionPageResponse>(`/api/v1/mentions${query}`)).body!;
}

// ---- Message tools (Phase 18) ----

export async function listPins(conversationId: string): Promise<PinnedMessageResponse[]> {
  return (await getJson<PinnedMessageResponse[]>(`${base(conversationId)}/pins`)).body ?? [];
}

export const setPinned = (conversationId: string, messageId: string, pinned: boolean) =>
  sendJson('PUT', `${messageUrl(conversationId, messageId)}/pin`, { pinned });

export const setSaved = (conversationId: string, messageId: string, saved: boolean) =>
  sendJson('PUT', `${messageUrl(conversationId, messageId)}/saved`, { saved });

export async function forwardMessage(
  conversationId: string,
  messageId: string,
  forward: { targetConversationId: string; clientMessageId: string; comment?: string },
): Promise<ConversationEntryResponse> {
  return (await sendJson<ConversationEntryResponse>('POST', `${messageUrl(conversationId, messageId)}/forward`, { ...forward, comment: forward.comment ?? null }))!;
}

export async function listSaved(before?: string): Promise<SavedPageResponse> {
  const query = before ? `?before=${encodeURIComponent(before)}` : '';
  return (await getJson<SavedPageResponse>(`/api/v1/saved${query}`)).body!;
}

export async function countUnreadMentions(): Promise<number> {
  return (await getJson<{ unread: number }>('/api/v1/mentions/unread')).body?.unread ?? 0;
}

export const addParticipants = (conversationId: string, userIds: string[]) => sendJson('POST', `${base(conversationId)}/participants`, { userIds });

export const removeParticipant = (conversationId: string, userId: string) =>
  sendJson('DELETE', `${base(conversationId)}/participants/${encodeURIComponent(userId)}`);

export const changeParticipantRole = (conversationId: string, userId: string, role: ParticipantRole) =>
  sendJson('PUT', `${base(conversationId)}/participants/${encodeURIComponent(userId)}`, { role });

export const leaveConversation = (conversationId: string) => sendJson('POST', `${base(conversationId)}/leave`);

/** Sends a message. Retrying with the same clientMessageId is safe: the server returns the original. */
export async function sendMessage(
  conversationId: string,
  message: {
    clientMessageId: string;
    body: string;
    replyToId?: string | null;
    attachmentIds?: string[];
    threadRootId?: string | null;
    alsoInConversation?: boolean;
    mentionUserIds?: string[];
  },
): Promise<ConversationEntryResponse> {
  return (await sendJson<ConversationEntryResponse>('POST', `${base(conversationId)}/messages`, {
    ...message,
    replyToId: message.replyToId ?? null,
    attachmentIds: message.attachmentIds ?? [],
    threadRootId: message.threadRootId ?? null,
    alsoInConversation: message.alsoInConversation ?? false,
    mentionUserIds: message.mentionUserIds ?? [],
  }))!;
}

const messageUrl = (conversationId: string, messageId: string) => `${base(conversationId)}/messages/${encodeURIComponent(messageId)}`;

/** Phase 16: adds or removes the caller's reaction; returns the message's reactions. */
export async function setReaction(conversationId: string, messageId: string, emoji: string, reacted: boolean): Promise<ReactionResponse[]> {
  return (await sendJson<ReactionResponse[]>('PUT', `${messageUrl(conversationId, messageId)}/reactions`, { emoji, reacted })) ?? [];
}

export async function getThread(conversationId: string, messageId: string, before?: string): Promise<ThreadResponse> {
  const query = before ? `?before=${encodeURIComponent(before)}` : '';
  return (await getJson<ThreadResponse>(`${messageUrl(conversationId, messageId)}/thread${query}`)).body!;
}

export const markThreadRead = (conversationId: string, messageId: string, sequence: number) =>
  sendJson<{ unread: number }>('POST', `${messageUrl(conversationId, messageId)}/thread/read`, { sequence });

export const setThreadFollowing = (conversationId: string, messageId: string, following: boolean) =>
  sendJson('PUT', `${messageUrl(conversationId, messageId)}/thread/follow`, { following });

export async function editMessage(conversationId: string, messageId: string, body: string): Promise<ConversationEntryResponse> {
  return (await sendJson<ConversationEntryResponse>('PUT', `${base(conversationId)}/messages/${encodeURIComponent(messageId)}`, { body }))!;
}

export const deleteMessage = (conversationId: string, messageId: string) =>
  sendJson('DELETE', `${base(conversationId)}/messages/${encodeURIComponent(messageId)}`);

/** ADR-023 delivery acknowledgement for this device. */
export const markDelivered = (conversationId: string, sequence: number) => sendJson('POST', `${base(conversationId)}/delivered`, { sequence });
