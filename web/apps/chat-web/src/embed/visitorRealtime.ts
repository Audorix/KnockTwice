import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import type { MessageEvent, MessageEventType, ReceiptEvent, TypingStatusChangedEvent } from '../features/realtime/realtimeClient';
import { apiBase, widgetHubPath } from './widgetApi';

export type VisitorHubLike = Pick<HubConnection, 'start' | 'stop' | 'on' | 'invoke' | 'onreconnected' | 'onclose' | 'state'>;

export interface VisitorRealtimeHandlers {
  onMessage: (type: MessageEventType, event: MessageEvent) => void;
  onTyping: (event: TypingStatusChangedEvent) => void;
  onRead: (event: ReceiptEvent) => void;
  /** Events may have been missed while disconnected: reload the conversation. */
  onReconnected: () => void;
}

const defaultFactory = (token: () => string): VisitorHubLike =>
  new HubConnectionBuilder()
    .withUrl(`${apiBase}${widgetHubPath}`, { accessTokenFactory: token, withCredentials: false })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();

/**
 * The visitor's connection to `/hubs/widget`. The server only ever puts it in the visitor's own
 * conversation; call {@link join} again after the first message created that conversation.
 */
export class VisitorRealtime {
  private hub: VisitorHubLike | null = null;
  private readonly seen = new Set<string>();

  constructor(
    private readonly token: () => string,
    private readonly handlers: VisitorRealtimeHandlers,
    private readonly factory: (token: () => string) => VisitorHubLike = defaultFactory,
  ) {}

  async start(): Promise<void> {
    if (this.hub) return;
    const hub = this.factory(this.token);
    this.hub = hub;
    for (const type of ['MessageCreated', 'MessageUpdated', 'MessageDeleted'] as const) {
      hub.on(type, (event: MessageEvent) => {
        if (this.seen.has(event.eventId)) return; // delivery is at least once
        this.seen.add(event.eventId);
        if (this.seen.size > 1000) this.seen.clear();
        this.handlers.onMessage(type, event);
      });
    }
    hub.on('TypingStatusChanged', (event: TypingStatusChangedEvent) => this.handlers.onTyping(event));
    hub.on('MessageRead', (event: ReceiptEvent) => this.handlers.onRead(event));
    hub.onreconnected(() => this.handlers.onReconnected());
    try {
      await hub.start();
    } catch {
      // Realtime is an enhancement: messages still load and send over HTTP.
    }
  }

  async join(): Promise<void> {
    if (this.hub?.state === HubConnectionState.Connected) await this.hub.invoke('JoinConversation').catch(() => undefined);
  }

  async typing(isTyping: boolean): Promise<void> {
    if (this.hub?.state === HubConnectionState.Connected) await this.hub.invoke('SendTypingStatus', isTyping).catch(() => undefined);
  }

  async stop(): Promise<void> {
    const hub = this.hub;
    this.hub = null;
    if (hub) await hub.stop().catch(() => undefined);
  }
}
