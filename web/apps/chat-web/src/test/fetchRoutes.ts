import { vi } from 'vitest';

export type Route = (init: RequestInit | undefined) => Response | Promise<Response>;

export function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': status >= 400 ? 'application/problem+json' : 'application/json' },
  });
}

export const authConfig = {
  registrationEnabled: true,
  requireConfirmedEmail: true,
  passwordMinimumLength: 10,
  captcha: { provider: 'none', siteKey: null },
  externalProviders: [],
  autoConfirmEmail: false,
  demoDataEnabled: false,
};

/** Routes fetch calls by "METHOD path" to handlers; unmatched calls fail the test loudly. */
export function mockFetch(routes: Record<string, Route>) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const key = `${init?.method ?? 'GET'} ${url}`;
    const route = routes[key];
    if (!route) throw new Error(`Unexpected fetch: ${key}`);
    return route(init);
  });
}

export const header = (init: RequestInit | undefined, name: string) => (init?.headers as Record<string, string> | undefined)?.[name];
