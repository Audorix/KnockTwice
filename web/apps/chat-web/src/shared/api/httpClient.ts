import type { ProblemDocument } from './contracts';

/** Same-origin by default: Vite (development) or nginx (Docker) forwards API paths. */
const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
const defaultTimeoutMs = 8000;

export type ApiFailureKind = 'network' | 'timeout' | 'http';

/** A failed API call, normalized so presentation code never inspects fetch internals. */
export class ApiError extends Error {
  readonly kind: ApiFailureKind;
  readonly status: number | undefined;
  readonly code: string | undefined;

  constructor(kind: ApiFailureKind, options: { status?: number; code?: string; cause?: unknown } = {}) {
    super(`API request failed (${kind}${options.status ? ` ${options.status}` : ''})`, { cause: options.cause });
    this.name = 'ApiError';
    this.kind = kind;
    this.status = options.status;
    this.code = options.code;
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
}

export async function getJson<T>(path: string, options: RequestOptions = {}): Promise<ApiResponse<T>> {
  const timeout = AbortSignal.timeout(options.timeoutMs ?? defaultTimeoutMs);
  const signal = options.signal ? AbortSignal.any([options.signal, timeout]) : timeout;
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (options.language) headers['Accept-Language'] = options.language;

  const started = performance.now();
  let response: Response;
  try {
    response = await fetch(`${baseUrl}${path}`, { method: 'GET', headers, signal, cache: 'no-store' });
  } catch (error) {
    if (options.signal?.aborted) throw error; // caller cancelled: propagate the AbortError untouched
    throw new ApiError(timeout.aborted ? 'timeout' : 'network', { cause: error });
  }
  const durationMs = performance.now() - started;

  const body = await readJson<T | ProblemDocument>(response);
  if (response.ok || options.acceptStatuses?.includes(response.status)) {
    return { status: response.status, body: body as T | undefined, durationMs };
  }

  const code = body && typeof body === 'object' && 'code' in body ? String(body.code) : undefined;
  throw new ApiError('http', code === undefined ? { status: response.status } : { status: response.status, code });
}

async function readJson<T>(response: Response): Promise<T | undefined> {
  const type = response.headers.get('content-type') ?? '';
  if (!type.includes('json')) return undefined;
  try {
    return (await response.json()) as T;
  } catch {
    return undefined;
  }
}
