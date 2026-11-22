import { describe, expect, it, vi } from 'vitest';
import { checkReadiness } from './readinessApi';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

describe('checkReadiness', () => {
  it('reports available for 200 healthy and sends the UI language', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(200, { status: 'healthy', checks: [{ name: 'database', status: 'healthy' }] }),
    );

    const result = await checkReadiness(undefined, 'es');

    expect(result.state).toBe('available');
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe('/health/ready');
    expect((init?.headers as Record<string, string>)['Accept-Language']).toBe('es');
  });

  it('distinguishes "API answered but not ready" (503) from unreachable', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(503, { status: 'unhealthy', checks: [] }));

    expect((await checkReadiness()).state).toBe('notReady');
  });

  it('reports unreachable for network failures and unexpected statuses', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new TypeError('Failed to fetch'));
    expect((await checkReadiness()).state).toBe('unreachable');

    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response('<html>bad gateway</html>', { status: 502 }));
    expect((await checkReadiness()).state).toBe('unreachable');
  });

  it('propagates caller cancellation instead of reporting a state', async () => {
    const controller = new AbortController();
    vi.spyOn(globalThis, 'fetch').mockImplementation(() => {
      controller.abort();
      return Promise.reject(new DOMException('Aborted', 'AbortError'));
    });

    await expect(checkReadiness(controller.signal)).rejects.toThrow('Aborted');
  });
});
