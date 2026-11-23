import type { AttachmentLimitsResponse, AttachmentResponse } from '../../shared/api/contracts';
import { apiUrl, getJson, sendJson, uploadFile } from '../../shared/api/httpClient';

/** Typed calls to the attachment endpoints (Phase 7). */

let limits: Promise<AttachmentLimitsResponse> | null = null;

/** The installation's limits, fetched once per page load (the server still enforces them). */
export function getLimits(): Promise<AttachmentLimitsResponse> {
  limits ??= getJson<AttachmentLimitsResponse>('/api/v1/attachments/limits').then(
    (r) => r.body!,
    (error: unknown) => {
      limits = null;
      throw error;
    },
  );
  return limits;
}

export function uploadAttachment(conversationId: string, file: File): Promise<AttachmentResponse> {
  return uploadFile<AttachmentResponse>(`/api/v1/conversations/${encodeURIComponent(conversationId)}/attachments`, file, file.name);
}

export async function getAttachment(attachmentId: string): Promise<AttachmentResponse> {
  return (await getJson<AttachmentResponse>(`/api/v1/attachments/${encodeURIComponent(attachmentId)}`)).body!;
}

/** Removes an upload that was not sent. */
export const deleteAttachment = (attachmentId: string) => sendJson('DELETE', `/api/v1/attachments/${encodeURIComponent(attachmentId)}`);

export const attachmentUrl = (attachmentId: string, download = false) =>
  apiUrl(`/api/v1/attachments/${encodeURIComponent(attachmentId)}/content${download ? '?download=true' : ''}`);
