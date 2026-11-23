import { describe, expect, it, vi } from 'vitest';
import { HubConnectionState } from '@microsoft/signalr';
import { hubErrorCode, RealtimeClient, type HubLike, type RealtimeStatus } from './realtimeClient';

/** In-memory stand-in for a SignalR connection that records invocations. */
class FakeHub {
  state = HubConnectionState.Disconnected;
  invocations: unknown[][] = [];
  failStart = false;
  presence: { userId: string; status: string }[] = [];
  private handlers = new Map<string, (arg: unknown) => void>();
  private reconnecting?: () => void;
  private reconnected?: () => void;
  private closed?: () => void;

  start = vi.fn(async () => {
    if (this.failStart) throw new Error('offline');
    this.state = HubConnectionState.Connected;
  });
  stop = vi.fn(async () => {
    this.state = HubConnectionState.Disconnected;
  });
  on = (name: string, handler: (arg: unknown) => void) => {
    this.handlers.set(name, handler);
  };
  off = () => undefined;
  invoke = vi.fn(async (name: string, ...args: unknown[]) => {
    this.invocations.push([name, ...args]);
    return name === 'GetPresence' ? this.presence : undefined;
  });
  onreconnecting = (cb: () => void) => {
    this.reconnecting = cb;
  };
  onreconnected = (cb: () => void) => {
    this.reconnected = cb;
  };
  onclose = (cb: () => void) => {
    this.closed = cb;
  };

  emit(name: string, payload: unknown) {
    this.handlers.get(name)?.(payload);
  }
  dropAndRecover() {
    this.state = HubConnectionState.Reconnecting;
    this.reconnecting?.();
    this.state = HubConnectionState.Connected;
    this.reconnected?.();
  }
  close() {
    this.state = HubConnectionState.Disconnected;
    this.closed?.();
  }
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('RealtimeClient', () => {
  it('loads presence after connecting and forwards live events', async () => {
    const hub = new FakeHub();
    hub.presence = [{ userId: 'ben', status: 'online' }];
    const client = new RealtimeClient(() => hub as unknown as HubLike);
    const snapshots: unknown[] = [];
    const events: unknown[] = [];
    client.onPresenceSnapshot((s) => snapshots.push(s));
    client.onPresence((e) => events.push(e));

    await client.start();
    hub.emit('UserPresenceChanged', { version: 1, userId: 'ben', status: 'offline', atUtc: '2026-11-22T12:00:00Z' });

    expect(client.status).toBe('connected');
    expect(snapshots).toEqual([[{ userId: 'ben', status: 'online' }]]);
    expect(events).toEqual([expect.objectContaining({ userId: 'ben', status: 'offline' })]);
  });

  it('rejoins conversations and replaces presence after a reconnect, without duplicates', async () => {
    const hub = new FakeHub();
    const client = new RealtimeClient(() => hub as unknown as HubLike);
    const statuses: RealtimeStatus[] = [];
    client.onStatus((s) => statuses.push(s));
    await client.start();
    await client.joinConversation('c1');
    await client.joinConversation('c1');
    hub.invocations = [];

    hub.dropAndRecover();
    await flush();

    expect(statuses).toEqual(['connecting', 'connected', 'reconnecting', 'connected']);
    expect(hub.invocations).toEqual([['JoinConversation', 'c1'], ['GetPresence']]);
  });

  it('only sends typing for joined conversations and forgets revoked ones on rejoin', async () => {
    const hub = new FakeHub();
    const client = new RealtimeClient(() => hub as unknown as HubLike);
    await client.start();
    await client.sendTyping('c1', true);
    expect(hub.invocations.filter(([name]) => name === 'SendTypingStatus')).toEqual([]);

    await client.joinConversation('c1');
    await client.sendTyping('c1', true);
    expect(hub.invocations).toContainEqual(['SendTypingStatus', 'c1', true]);

    hub.invoke.mockImplementationOnce(async () => {
      throw new Error("An unexpected error occurred invoking 'JoinConversation' on the server. HubException: conversation_not_found");
    });
    hub.dropAndRecover();
    await flush();
    hub.invocations = [];
    await client.sendTyping('c1', true);
    expect(hub.invocations).toEqual([]);
  });

  it('keeps retrying with backoff when the connection cannot be established', async () => {
    vi.useFakeTimers();
    try {
      const hub = new FakeHub();
      hub.failStart = true;
      const client = new RealtimeClient(() => hub as unknown as HubLike);
      await client.start();
      expect(client.status).toBe('disconnected');

      hub.failStart = false;
      await vi.advanceTimersByTimeAsync(5000);
      expect(hub.start).toHaveBeenCalledTimes(2);
      expect(client.status).toBe('connected');

      hub.close();
      expect(client.status).toBe('disconnected');
      await vi.advanceTimersByTimeAsync(5000);
      expect(client.status).toBe('connected');

      await client.stop();
      expect(hub.stop).toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });

  it('extracts stable error codes from hub exceptions', () => {
    expect(hubErrorCode(new Error("An unexpected error occurred invoking 'X' on the server. HubException: unauthorized"))).toBe('unauthorized');
    expect(hubErrorCode(new Error('network'))).toBeUndefined();
  });
});
