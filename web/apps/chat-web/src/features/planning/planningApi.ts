import type { ConversationEntryResponse } from '../../shared/api/contracts';
import { getJson, sendJson } from '../../shared/api/httpClient';

/** Phase 20 (ADR-034): drafts, scheduled messages and reminders. */

export interface DraftResponse {
  conversationId: string;
  threadRootId: string | null;
  body: string;
  replyToId: string | null;
  updatedAtUtc: string;
}

export interface ScheduledMessageResponse {
  id: string;
  conversationId: string;
  conversationType: string | null;
  conversationTitle: string | null;
  body: string;
  sendAtUtc: string;
  status: 'pending' | 'failed';
  failureCode: string | null;
  fileCount: number;
  threadRootId: string | null;
  replyToId: string | null;
}

export interface ReminderResponse {
  id: string;
  conversationId: string;
  conversationType: string;
  conversationTitle: string | null;
  entry: ConversationEntryResponse;
  remindAtUtc: string;
  status: 'pending' | 'due';
}

const conversation = (id: string) => `/api/v1/conversations/${encodeURIComponent(id)}`;

export async function listDrafts(): Promise<DraftResponse[]> {
  return (await getJson<DraftResponse[]>('/api/v1/drafts')).body ?? [];
}

export const saveDraft = (conversationId: string, draft: { body: string; replyToId?: string | null; threadRootId?: string | null }) =>
  sendJson('PUT', `${conversation(conversationId)}/draft`, { body: draft.body, replyToId: draft.replyToId ?? null, threadRootId: draft.threadRootId ?? null });

export async function scheduleMessage(
  conversationId: string,
  message: { body: string; sendAtUtc: string; replyToId?: string | null; threadRootId?: string | null; alsoInConversation?: boolean; attachmentIds?: string[]; mentionUserIds?: string[] },
): Promise<ScheduledMessageResponse> {
  return (await sendJson<ScheduledMessageResponse>('POST', `${conversation(conversationId)}/scheduled`, {
    ...message,
    replyToId: message.replyToId ?? null,
    threadRootId: message.threadRootId ?? null,
    alsoInConversation: message.alsoInConversation ?? false,
    attachmentIds: message.attachmentIds ?? [],
    mentionUserIds: message.mentionUserIds ?? [],
  }))!;
}

export async function listScheduled(): Promise<ScheduledMessageResponse[]> {
  return (await getJson<ScheduledMessageResponse[]>('/api/v1/scheduled')).body ?? [];
}

export const updateScheduled = (id: string, body: string, sendAtUtc: string) =>
  sendJson<ScheduledMessageResponse>('PUT', `/api/v1/scheduled/${encodeURIComponent(id)}`, { body, sendAtUtc });

export const deleteScheduled = (id: string) => sendJson('DELETE', `/api/v1/scheduled/${encodeURIComponent(id)}`);

export const setReminder = (conversationId: string, messageId: string, remindAtUtc: string) =>
  sendJson<ReminderResponse>('POST', `${conversation(conversationId)}/messages/${encodeURIComponent(messageId)}/reminder`, { remindAtUtc });

export async function listReminders(): Promise<ReminderResponse[]> {
  return (await getJson<ReminderResponse[]>('/api/v1/reminders')).body ?? [];
}

export const deleteReminder = (id: string) => sendJson('DELETE', `/api/v1/reminders/${encodeURIComponent(id)}`);
