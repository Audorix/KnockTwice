import type {
  PrivateAttachmentResponse,
  PrivateChatDetailResponse,
  PrivateChatResponse,
  PrivateMessageResponse,
} from '../../shared/api/contracts';
import { apiUrl, getJson, sendJson, uploadFile } from '../../shared/api/httpClient';

/**
 * Typed calls to /api/v1/private-chats. Nothing returned here may be persisted by the client
 * (no localStorage, no IndexedDB): private chats exist only while they last.
 */

const base = (chatId: string) => `/api/v1/private-chats/${encodeURIComponent(chatId)}`;

export async function listPrivateChats(): Promise<PrivateChatResponse[]> {
  return (await getJson<PrivateChatResponse[]>('/api/v1/private-chats')).body ?? [];
}

export async function startPrivateChat(userId: string): Promise<PrivateChatResponse> {
  return (await sendJson<PrivateChatResponse>('POST', '/api/v1/private-chats', { userId }))!;
}

export async function getPrivateChat(chatId: string): Promise<PrivateChatDetailResponse> {
  return (await getJson<PrivateChatDetailResponse>(base(chatId))).body!;
}

export async function acceptPrivateChat(chatId: string): Promise<PrivateChatResponse> {
  return (await sendJson<PrivateChatResponse>('POST', `${base(chatId)}/accept`))!;
}

/** Ends the chat (initiator) or declines the invitation (invitee). */
export const endPrivateChat = (chatId: string) => sendJson('DELETE', base(chatId));

export async function sendPrivateMessage(
  chatId: string,
  message: { clientMessageId: string; body: string; attachmentIds: string[] },
): Promise<PrivateMessageResponse> {
  return (await sendJson<PrivateMessageResponse>('POST', `${base(chatId)}/messages`, message))!;
}

export function uploadPrivateFile(chatId: string, file: File): Promise<PrivateAttachmentResponse> {
  return uploadFile<PrivateAttachmentResponse>(`${base(chatId)}/attachments`, file, file.name);
}

export const privateFileUrl = (chatId: string, fileId: string, download = false) =>
  apiUrl(`${base(chatId)}/attachments/${encodeURIComponent(fileId)}/content${download ? '?download=true' : ''}`);
