import type { ContactRequestResponse, ContactResponse, UserSummary } from '../../shared/api/contracts';
import { getJson, sendJson } from '../../shared/api/httpClient';

/** Typed calls to /api/v1/contacts and user discovery. */

const id = (value: string) => encodeURIComponent(value);

export async function searchUsers(query: string): Promise<UserSummary[]> {
  return (await getJson<UserSummary[]>(`/api/v1/users/search?q=${encodeURIComponent(query)}`)).body ?? [];
}

export async function listContacts(): Promise<ContactResponse[]> {
  return (await getJson<ContactResponse[]>('/api/v1/contacts')).body ?? [];
}

export async function listRequests(): Promise<ContactRequestResponse[]> {
  return (await getJson<ContactRequestResponse[]>('/api/v1/contacts/requests')).body ?? [];
}

export async function listBlocked(): Promise<ContactResponse[]> {
  return (await getJson<ContactResponse[]>('/api/v1/contacts/blocked')).body ?? [];
}

export async function sendRequest(userId: string): Promise<'pending' | 'accepted'> {
  return (await sendJson<{ status: 'pending' | 'accepted' }>('POST', '/api/v1/contacts/requests', { userId }))!.status;
}

export const acceptRequest = (requestId: string) => sendJson('POST', `/api/v1/contacts/requests/${id(requestId)}/accept`);
export const rejectRequest = (requestId: string) => sendJson('POST', `/api/v1/contacts/requests/${id(requestId)}/reject`);
export const cancelRequest = (requestId: string) => sendJson('DELETE', `/api/v1/contacts/requests/${id(requestId)}`);
export const removeContact = (userId: string) => sendJson('DELETE', `/api/v1/contacts/${id(userId)}`);
export const blockUser = (userId: string) => sendJson('POST', `/api/v1/contacts/${id(userId)}/block`);
export const unblockUser = (userId: string) => sendJson('DELETE', `/api/v1/contacts/${id(userId)}/block`);
