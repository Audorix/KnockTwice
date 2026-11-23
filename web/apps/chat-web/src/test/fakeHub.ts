import { vi } from 'vitest';
import { HubConnectionState } from '@microsoft/signalr';
import type { HubLike } from '../features/realtime/realtimeClient';

/** In-memory stand-in for a connected SignalR hub; tests push server events with emit(). */
export class FakeHub {
  state = HubConnectionState.Disconnected;
  private handlers = new Map<string, (arg: unknown) => void>();

  start = vi.fn(async () => {
    this.state = HubConnectionState.Connected;
  });
  stop = vi.fn(async () => {
    this.state = HubConnectionState.Disconnected;
  });
  on = (name: string, handler: (arg: unknown) => void) => {
    this.handlers.set(name, handler);
  };
  off = () => undefined;
  invoke = vi.fn(async (name: string) => (name === 'GetPresence' ? [] : undefined));
  onreconnecting = () => undefined;
  onreconnected = () => undefined;
  onclose = () => undefined;

  emit(name: string, payload: unknown) {
    this.handlers.get(name)?.(payload);
  }

  asHub(): HubLike {
    return this as unknown as HubLike;
  }
}
