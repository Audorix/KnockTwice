import type { CsrfTokenResponse, ProblemDocument } from './contracts';

/** Same-origin by default: Vite (development) or nginx (Docker) forwards API paths. */
const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
const defaultTimeoutMs = 8000;

export type ApiFailureKind = 'network' | 'timeout' | 'http';

/** A failed API call, normalized so presentation code never inspects fetch internals. */
export class ApiError extends Error {
  readonly kind: ApiFailureKind;
  readonly status: number | undefined;
  readonly code: string | undefined;
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;
  /** Operator-facing configuration messages (setup wizard only). */
  readonly details: readonly string[];

  constructor(
    kind: ApiFailureKind,
    options: { status?: number; code?: string; fieldErrors?: Record<string, string[]>; details?: string[]; cause?: unknown } = {},
  ) {
    super(`API request failed (${kind}${options.status ? ` ${options.status}` : ''})`, { cause: options.cause });
    this.name = 'ApiError';
    this.kind = kind;
    this.status = options.status;
    this.code = options.code;
    this.fieldErrors = options.fieldErrors ?? {};
    this.details = options.details ?? [];
  }
}

export interface ApiResponse<T> {
  status: number;
  body: T | undefined;
  durationMs: number;
}

export interface RequestOptions {
  signal?: AbortSignal;
  language?: string;
  timeoutMs?: number;
  /** HTTP statuses that are meaningful results rather than errors (e.g. 503 from readiness). */
  acceptStatuses?: readonly number[];
  /** Extra request headers, e.g. the setup wizard's X-Setup-Token. */
  headers?: Record<string, string>;
}

let languageProvider: () => string | undefined = () => undefined;

/** Lets the i18n module supply Accept-Language without a circular import. */
export function setLanguageProvider(provider: () => string | undefined): void {
  languageProvider = provider;
}

let deletedUserLabel: () => string | undefined = () => undefined;

/** Lets the i18n module supply the localized name shown for deleted accounts (Phase 13). */
export function setDeletedUserLabelProvider(provider: () => string | undefined): void {
  deletedUserLabel = provider;
}

/**
 * Deleted accounts arrive as `{ deleted: true, displayName: "Deleted user" }`. Every user summary
 * in a response gets the localized label instead, and so does the title of a direct conversation
 * with such a person, so no screen needs to know about deletion.
 */
export function labelDeletedUsers<T>(value: T, label = deletedUserLabel()): T {
  if (!label || value === null || typeof value !== 'object') return value;
  if (Array.isArray(value)) {
    for (const item of value) labelDeletedUsers(item, label);
    return value;
  }
  const record = value as Record<string, unknown>;
  if (record.deleted === true && typeof record.displayName === 'string' && 'avatar' in record) {
    record.displayName = label;
  }
  const counterpart = record.counterpart as Record<string, unknown> | null | undefined;
  if (counterpart?.deleted === true && typeof record.title === 'string' && record.type === 'direct') {
    record.title = label;
  }
  for (const child of Object.values(record)) {
    if (child !== null && typeof child === 'object') labelDeletedUsers(child, label);
  }
  return value;
}

export function getJson<T>(path: string, options: RequestOptions = {}): Promise<ApiResponse<T>> {
  return request<T>('GET', path, undefined, options);
}

/** POST JSON without the antiforgery token: only for endpoints outside the cookie session (setup wizard). */
export async function postJson<T>(path: string, body: unknown, options: RequestOptions = {}): Promise<T | undefined> {
  return (await request<T>('POST', path, body, options)).body;
}

// ---- State-changing requests: cookie session + antiforgery token -------------------------

let csrf: { token: string; header: string } | null = null;

/** Discard the antiforgery token; it is bound to the signed-in identity, so call after sign-in/out. */
export function resetCsrfToken(): void {
  csrf = null;
}

async function ensureCsrf(): Promise<{ token: string; header: string }> {
  if (!csrf) {
    const response = await request<CsrfTokenResponse>('GET', '/api/v1/auth/csrf');
    csrf = { token: response.body!.token, header: response.body!.headerName };
  }
  return csrf;
}

/** POST/PUT/DELETE with JSON and the antiforgery header. A stale token is refreshed once. */
export async function sendJson<T>(
  method: 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown, options: { timeoutMs?: number } = {},
): Promise<T | undefined> {
  for (let attempt = 0; ; attempt++) {
    const token = await ensureCsrf();
    try {
      const response = await request<T>(method, path, body, { headers: { [token.header]: token.token }, ...options });
      return response.body;
    } catch (error) {
      if (attempt === 0 && error instanceof ApiError && error.code === 'csrf_token_invalid') {
        resetCsrfToken();
        continue;
      }
      throw error;
    }
  }
}

/**
 * Uploads one file as multipart/form-data (field "file") with the antiforgery header. The
 * browser sets the multipart boundary; the server streams and validates the content.
 */
export async function uploadFile<T>(path: string, file: Blob, fileName: string, timeoutMs = 120000): Promise<T> {
  for (let attempt = 0; ; attempt++) {
    const token = await ensureCsrf();
    const form = new FormData();
    form.append('file', file, fileName);
    const timeout = AbortSignal.timeout(timeoutMs);
    const headers: Record<string, string> = { Accept: 'application/json', [token.header]: token.token };
    const language = languageProvider();
    if (language) headers['Accept-Language'] = language;
    let response: Response;
    try {
      response = await fetch(`${baseUrl}${path}`, { method: 'POST', headers, body: form, signal: timeout, cache: 'no-store', credentials: 'same-origin' });
    } catch (error) {
      throw new ApiError(timeout.aborted ? 'timeout' : 'network', { cause: error });
    }

    const parsed = await readJson<T | ProblemDocument>(response);
    if (response.ok) return parsed as T;
    const problem = parsed && typeof parsed === 'object' && 'code' in parsed ? (parsed as ProblemDocument) : undefined;
    if (attempt === 0 && problem?.code === 'csrf_token_invalid') {
      resetCsrfToken();
      continue;
    }
    throw new ApiError('http', {
      status: response.status,
      ...(problem?.code ? { code: problem.code } : {}),
      ...(problem?.errors ? { fieldErrors: problem.errors } : {}),
    });
  }
}

/** Same-origin URL of an API resource, for links and images (the cookie authorizes them). */
export function apiUrl(path: string): string {
  return `${baseUrl}${path}`;
}

// ---- Transport ----------------------------------------------------------------------------

async function request<T>(
  method: string,
  path: string,
  body?: unknown,
  options: RequestOptions = {},
): Promise<ApiResponse<T>> {
  const timeout = AbortSignal.timeout(options.timeoutMs ?? defaultTimeoutMs);
  const signal = options.signal ? AbortSignal.any([options.signal, timeout]) : timeout;
  const headers: Record<string, string> = { Accept: 'application/json', ...options.headers };
  const language = options.language ?? languageProvider();
  if (language) headers['Accept-Language'] = language;
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  const started = performance.now();
  let response: Response;
  try {
    response = await fetch(`${baseUrl}${path}`, {
      method,
      headers,
      signal,
      cache: 'no-store',
      credentials: 'same-origin',
      ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
    });
  } catch (error) {
    if (options.signal?.aborted) throw error; // caller cancelled: propagate the AbortError untouched
    throw new ApiError(timeout.aborted ? 'timeout' : 'network', { cause: error });
  }
  const durationMs = performance.now() - started;

  const parsed = await readJson<T | ProblemDocument>(response);
  if (response.ok || options.acceptStatuses?.includes(response.status)) {
    return { status: response.status, body: parsed as T | undefined, durationMs };
  }

  const problem = parsed && typeof parsed === 'object' && 'code' in parsed ? (parsed as ProblemDocument) : undefined;
  throw new ApiError('http', {
    status: response.status,
    ...(problem?.code ? { code: problem.code } : {}),
    ...(problem?.errors ? { fieldErrors: problem.errors } : {}),
    ...(problem?.details ? { details: problem.details } : {}),
  });
}

async function readJson<T>(response: Response): Promise<T | undefined> {
  const type = response.headers.get('content-type') ?? '';
  if (!type.includes('json')) return undefined;
  try {
    return labelDeletedUsers((await response.json()) as T);
  } catch {
    return undefined;
  }
}
