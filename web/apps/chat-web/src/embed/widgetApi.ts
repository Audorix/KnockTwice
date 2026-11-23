import type {
  ConversationEntryResponse,
  ConversationReadResponse,
  MessageAttachmentResponse,
  ProblemDocument,
  VisitorConversationResponse,
  VisitorSessionResponse,
  WidgetConfigResponse,
} from '../shared/api/contracts';
import { ApiError } from '../shared/api/httpClient';

/**
 * Client of `/api/v1/widget` for the embedded chat. Visitors authenticate with a bearer session
 * token (never a cookie), kept in this frame's storage, which browsers partition per embedding
 * site. The embed page and the API share an origin, so no CORS is involved.
 */

const base = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

export interface StoredSession {
  token: string;
  identified: boolean;
  displayName: string;
  expiresAtUtc: string;
}

function storageKey(widgetId: string): string {
  return `chatx.widget.${widgetId}`;
}

export function loadSession(widgetId: string): StoredSession | null {
  try {
    const raw = window.localStorage.getItem(storageKey(widgetId));
    if (!raw) return null;
    const session = JSON.parse(raw) as StoredSession;
    return typeof session.token === 'string' && Date.parse(session.expiresAtUtc) > Date.now() ? session : null;
  } catch {
    return null;
  }
}

export function saveSession(widgetId: string, session: StoredSession | null): void {
  try {
    if (session) window.localStorage.setItem(storageKey(widgetId), JSON.stringify(session));
    else window.localStorage.removeItem(storageKey(widgetId));
  } catch {
    // Storage may be blocked in third-party frames; the session then lasts for this page view.
  }
}

async function call<T>(method: string, path: string, options: { token?: string; body?: unknown; form?: FormData; language?: string; timeoutMs?: number } = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (options.token) headers.Authorization = `Bearer ${options.token}`;
  if (options.language) headers['Accept-Language'] = options.language;
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';
  const timeout = AbortSignal.timeout(options.timeoutMs ?? 10000);
  let response: Response;
  try {
    response = await fetch(`${base}${path}`, {
      method,
      headers,
      cache: 'no-store',
      credentials: 'omit',
      signal: timeout,
      ...(options.form ? { body: options.form } : options.body !== undefined ? { body: JSON.stringify(options.body) } : {}),
    });
  } catch (error) {
    throw new ApiError(timeout.aborted ? 'timeout' : 'network', { cause: error });
  }

  const type = response.headers.get('content-type') ?? '';
  const parsed: unknown = type.includes('json') ? await response.json().catch(() => undefined) : undefined;
  if (response.ok) return parsed as T;
  const problem = parsed as ProblemDocument | undefined;
  throw new ApiError('http', {
    status: response.status,
    ...(problem?.code ? { code: problem.code } : {}),
    ...(problem?.errors ? { fieldErrors: problem.errors } : {}),
  });
}

export const widgetApi = {
  config: (widgetId: string, language?: string) => call<WidgetConfigResponse>('GET', `/api/v1/widget/${widgetId}/config`, { ...(language ? { language } : {}) }),

  startSession: (widgetId: string, parentOrigin: string, language: string, identityToken?: string | null) =>
    call<VisitorSessionResponse>('POST', `/api/v1/widget/${widgetId}/sessions`, {
      body: { parentOrigin, language, ...(identityToken ? { identityToken } : {}) },
      language,
    }),

  endSession: (token: string) => call<void>('DELETE', '/api/v1/widget/visitor/session', { token }),

  conversation: (token: string, before?: string) =>
    call<VisitorConversationResponse>('GET', `/api/v1/widget/visitor/conversation${before ? `?before=${encodeURIComponent(before)}` : ''}`, { token }),

  send: (token: string, clientMessageId: string, body: string, attachmentIds?: string[]) =>
    call<ConversationEntryResponse>('POST', '/api/v1/widget/visitor/messages', {
      token,
      body: { clientMessageId, body, ...(attachmentIds?.length ? { attachmentIds } : {}) },
    }),

  read: (token: string, sequence: number) => call<ConversationReadResponse>('POST', '/api/v1/widget/visitor/read', { token, body: { sequence } }),

  upload: (token: string, file: File) => {
    const form = new FormData();
    form.append('file', file, file.name);
    return call<MessageAttachmentResponse>('POST', '/api/v1/widget/visitor/attachments', { token, form, timeoutMs: 120000 });
  },

  /** Attachment content as a blob (the bearer token cannot ride on an <img> request). */
  async download(token: string, attachmentId: string): Promise<Blob> {
    const response = await fetch(`${base}/api/v1/widget/visitor/attachments/${attachmentId}/content`, {
      headers: { Authorization: `Bearer ${token}` },
      credentials: 'omit',
      cache: 'no-store',
    });
    if (!response.ok) throw new ApiError('http', { status: response.status });
    return response.blob();
  },
};

export const widgetHubPath = '/hubs/widget';
export const apiBase = base;
