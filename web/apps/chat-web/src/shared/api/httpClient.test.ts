import { beforeEach, describe, expect, it } from 'vitest';
import { header, json, mockFetch } from '../../test/fetchRoutes';
import { ApiError, resetCsrfToken, sendJson } from './httpClient';

describe('sendJson', () => {
  beforeEach(() => resetCsrfToken());

  it('fetches the antiforgery token once and sends it on every state-changing request', async () => {
    let csrfCalls = 0;
    const seen: (string | undefined)[] = [];
    mockFetch({
      'GET /api/v1/auth/csrf': () => {
        csrfCalls++;
        return json(200, { token: 'token-1', headerName: 'X-CSRF-TOKEN' });
      },
      'PUT /api/v1/users/me': (init) => {
        seen.push(header(init, 'X-CSRF-TOKEN'));
        expect(init?.credentials).toBe('same-origin');
        return json(200, { ok: true });
      },
    });

    await sendJson('PUT', '/api/v1/users/me', { displayName: 'A' });
    await sendJson('PUT', '/api/v1/users/me', { displayName: 'B' });

    expect(csrfCalls).toBe(1);
    expect(seen).toEqual(['token-1', 'token-1']);
  });

  it('refreshes a stale token once and retries', async () => {
    const tokens = ['stale', 'fresh'];
    const seen: (string | undefined)[] = [];
    mockFetch({
      'GET /api/v1/auth/csrf': () => json(200, { token: tokens.shift(), headerName: 'X-CSRF-TOKEN' }),
      'POST /api/v1/auth/logout': (init) => {
        seen.push(header(init, 'X-CSRF-TOKEN'));
        return seen.length === 1 ? json(400, { code: 'csrf_token_invalid' }) : new Response(null, { status: 204 });
      },
    });

    await sendJson('POST', '/api/v1/auth/logout');

    expect(seen).toEqual(['stale', 'fresh']);
  });

  it('exposes the error code and field errors of problem responses', async () => {
    mockFetch({
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'POST /api/v1/auth/register': () => json(400, { code: 'validation_failed', errors: { password: ['password_too_short'] } }),
    });

    const error = await sendJson('POST', '/api/v1/auth/register', {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('validation_failed');
    expect((error as ApiError).fieldErrors).toEqual({ password: ['password_too_short'] });
  });
});
