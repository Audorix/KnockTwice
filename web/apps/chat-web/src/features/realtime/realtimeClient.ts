import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import type { ConversationEntryResponse, PrivateMessageResponse, ReactionChangedEvent } from '../../shared/api/contracts';

export type { ReactionChangedEvent };

/**
 * Client for the `/hubs/chat` SignalR contract (see Audorix.ChatX.Contracts.V1.Realtime).
 * It owns reconnection: SignalR's automatic retries first, then a slower manual retry loop.
 * After every (re)connect it rejoins the conversations the UI asked for and reloads the
 * presence snapshot, so a reconnect replaces state instead of adding to it.
 */

export const hubPath = '/hubs/chat';

export type RealtimeStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';
export type PresenceValue = 'online' | 'offline';

export interface UserPresenceChangedEvent {
  version: number;
  userId: string;
  status: PresenceValue;
  atUtc: string;
}

export interface TypingStatusChangedEvent {
  version: number;
  conversationId: string;
  userId: string;
  isTyping: boolean;
  expiresAtUtc: string | null;
}

export interface ConversationUpdatedEvent {
  version: number;
  conversationId: string;
  /** created | updated | members | read | removed (no access any more) | inbox (support inbox changed, Phase 11). */
  change: 'created' | 'updated' | 'members' | 'read' | 'removed' | 'inbox';
  lastSequence: number;
}

export type MessageEventType = 'MessageCreated' | 'MessageUpdated' | 'MessageDeleted';

/** At least once: deduplicate by eventId and keep the highest entry.version. */
export interface MessageEvent {
  version: number;
  eventId: string;
  conversationId: string;
  entry: ConversationEntryResponse;
}

export type ReceiptEventType = 'MessageDelivered' | 'MessageRead';

export interface PlanningChangedEvent {
  version: number;
  kind: 'drafts' | 'scheduled' | 'reminders';
}

export interface ReminderDueEvent {
  version: number;
  reminderId: string;
  conversationId: string;
  messageId: string;
  threadRootId: string | null;
  authorName: string | null;
  excerpt: string | null;
}

export interface ReceiptEvent {
  version: number;
  conversationId: string;
  userId: string;
  sequence: number;
}

export type PrivateChatEndReason = 'ended' | 'declined' | 'expired' | 'session_ended' | 'unavailable' | 'shutdown';

/** A private chat was offered (invited), accepted (active) or ended; after "ended" forget everything shown. */
export interface PrivateChatChangedEvent {
  version: number;
  chatId: string;
  status: 'invited' | 'active' | 'ended';
  reason: PrivateChatEndReason | null;
}

/** A private chat message: never stored by the server, keep it in memory only. */
export interface PrivateMessageEvent {
  version: number;
  chatId: string;
  message: PrivateMessageResponse;
}

export interface UserPresenceResponse {
  userId: string;
  status: PresenceValue;
}

/** The subset of HubConnection used here, so tests can supply a fake. */
export type HubLike = Pick<HubConnection, 'start' | 'stop' | 'on' | 'off' | 'invoke' | 'onreconnecting' | 'onreconnected' | 'onclose' | 'state'>;

export type HubFactory = () => HubLike;

const defaultFactory: HubFactory = () =>
  new HubConnectionBuilder()
    .withUrl(hubPath, { withCredentials: true })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
    .configureLogging(LogLevel.Warning)
    .build();

/** Stable error code from a failed hub invocation ("…HubException: conversation_not_found"). */
export function hubErrorCode(error: unknown): string | undefined {
  if (!(error instanceof Error)) return undefined;
  const match = /HubException: ([a-z_]+)\s*$/.exec(error.message);
  return match?.[1];
}

type Listener<T> = (value: T) => void;

export class RealtimeClient {
  private hub: HubLike | null = null;
  private stopped = true;
  private retryTimer: ReturnType<typeof setTimeout> | undefined;
  private retryDelayMs = 5000;
  private readonly conversations = new Set<string>();
  private readonly statusListeners = new Set<Listener<RealtimeStatus>>();
  private readonly presenceListeners = new Set<Listener<UserPresenceChangedEvent>>();
  private readonly snapshotListeners = new Set<Listener<UserPresenceResponse[]>>();
  private readonly typingListeners = new Set<Listener<TypingStatusChangedEvent>>();
  private readonly conversationListeners = new Set<Listener<ConversationUpdatedEvent>>();
  private readonly reconnectListeners = new Set<() => void>();
  private readonly messageListeners = new Set<(type: MessageEventType, event: MessageEvent) => void>();
  private readonly receiptListeners = new Set<(type: ReceiptEventType, event: ReceiptEvent) => void>();
  private readonly privateChatListeners = new Set<Listener<PrivateChatChangedEvent>>();
  private readonly privateMessageListeners = new Set<Listener<PrivateMessageEvent>>();
  private readonly reactionListeners = new Set<Listener<ReactionChangedEvent>>();
  private readonly planningListeners = new Set<Listener<PlanningChangedEvent>>();
  private readonly reminderListeners = new Set<Listener<ReminderDueEvent>>();
  private readonly seenEvents = new Set<string>();
  status: RealtimeStatus = 'disconnected';

  constructor(
    private readonly factory: HubFactory = defaultFactory,
    private readonly maxRetryDelayMs = 30000,
  ) {}

  onStatus(listener: Listener<RealtimeStatus>): () => void {
    this.statusListeners.add(listener);
    return () => this.statusListeners.delete(listener);
  }

  onPresence(listener: Listener<UserPresenceChangedEvent>): () => void {
    this.presenceListeners.add(listener);
    return () => this.presenceListeners.delete(listener);
  }

  /** Receives the complete visible presence after every (re)connect. */
  onPresenceSnapshot(listener: Listener<UserPresenceResponse[]>): () => void {
    this.snapshotListeners.add(listener);
    return () => this.snapshotListeners.delete(listener);
  }

  onTyping(listener: Listener<TypingStatusChangedEvent>): () => void {
    this.typingListeners.add(listener);
    return () => this.typingListeners.delete(listener);
  }

  onConversationUpdated(listener: Listener<ConversationUpdatedEvent>): () => void {
    this.conversationListeners.add(listener);
    return () => this.conversationListeners.delete(listener);
  }

  /** Message created/updated/deleted events, each delivered once per client (duplicates by eventId are dropped). */
  onMessage(listener: (type: MessageEventType, event: MessageEvent) => void): () => void {
    this.messageListeners.add(listener);
    return () => this.messageListeners.delete(listener);
  }

  onReceipt(listener: (type: ReceiptEventType, event: ReceiptEvent) => void): () => void {
    this.receiptListeners.add(listener);
    return () => this.receiptListeners.delete(listener);
  }

  /** Phase 16: a message's reactions changed; the event carries the complete new set. */
  onReaction(listener: Listener<ReactionChangedEvent>): () => void {
    this.reactionListeners.add(listener);
    return () => this.reactionListeners.delete(listener);
  }

  /** Phase 20: your drafts, scheduled messages or reminders changed. */
  onPlanning(listener: Listener<PlanningChangedEvent>): () => void {
    this.planningListeners.add(listener);
    return () => this.planningListeners.delete(listener);
  }

  /** Phase 20: a reminder came due. */
  onReminderDue(listener: Listener<ReminderDueEvent>): () => void {
    this.reminderListeners.add(listener);
    return () => this.reminderListeners.delete(listener);
  }

  onPrivateChat(listener: Listener<PrivateChatChangedEvent>): () => void {
    this.privateChatListeners.add(listener);
    return () => this.privateChatListeners.delete(listener);
  }

  onPrivateMessage(listener: Listener<PrivateMessageEvent>): () => void {
    this.privateMessageListeners.add(listener);
    return () => this.privateMessageListeners.delete(listener);
  }

  /** Called after every reconnect: events may have been missed, so views reload their data. */
  onReconnected(listener: () => void): () => void {
    this.reconnectListeners.add(listener);
    return () => this.reconnectListeners.delete(listener);
  }

  async start(): Promise<void> {
    if (!this.stopped) return;
    this.stopped = false;
    const hub = this.factory();
    this.hub = hub;
    hub.on('UserPresenceChanged', (event: UserPresenceChangedEvent) => this.presenceListeners.forEach((l) => l(event)));
    hub.on('TypingStatusChanged', (event: TypingStatusChangedEvent) => this.typingListeners.forEach((l) => l(event)));
    hub.on('ConversationUpdated', (event: ConversationUpdatedEvent) => this.conversationListeners.forEach((l) => l(event)));
    for (const type of ['MessageCreated', 'MessageUpdated', 'MessageDeleted'] as const) {
      hub.on(type, (event: MessageEvent) => {
        if (this.seenEvents.has(event.eventId)) return;
        this.seenEvents.add(event.eventId);
        if (this.seenEvents.size > 2000) this.seenEvents.clear();
        this.messageListeners.forEach((l) => l(type, event));
      });
    }
    for (const type of ['MessageDelivered', 'MessageRead'] as const) {
      hub.on(type, (event: ReceiptEvent) => this.receiptListeners.forEach((l) => l(type, event)));
    }
    hub.on('ReactionChanged', (event: ReactionChangedEvent) => this.reactionListeners.forEach((l) => l(event)));
    hub.on('PlanningChanged', (event: PlanningChangedEvent) => this.planningListeners.forEach((l) => l(event)));
    hub.on('ReminderDue', (event: ReminderDueEvent) => this.reminderListeners.forEach((l) => l(event)));
    hub.on('PrivateChatChanged',(event: PrivateChatChangedEvent) => this.privateChatListeners.forEach((l) => l(event)));
    hub.on('PrivateMessageCreated', (event: PrivateMessageEvent) => this.privateMessageListeners.forEach((l) => l(event)));
    hub.onreconnecting(() => this.setStatus('reconnecting'));
    hub.onreconnected(() => {
      void this.afterConnect();
      this.reconnectListeners.forEach((l) => l());
    });
    hub.onclose(() => {
      if (this.stopped || this.hub !== hub) return;
      // Automatic retries are exhausted (or the server closed the connection): keep trying slowly.
      this.setStatus('disconnected');
      this.scheduleRetry();
    });
    await this.connect();
  }

  async stop(): Promise<void> {
    this.stopped = true;
    clearTimeout(this.retryTimer);
    const hub = this.hub;
    this.hub = null;
    this.conversations.clear();
    this.setStatus('disconnected');
    if (hub) await hub.stop();
  }

  /** Remembers the conversation and joins it now if connected; it is rejoined after every reconnect. */
  async joinConversation(conversationId: string): Promise<void> {
    this.conversations.add(conversationId);
    if (this.isConnected()) await this.hub!.invoke('JoinConversation', conversationId);
  }

  async leaveConversation(conversationId: string): Promise<void> {
    this.conversations.delete(conversationId);
    if (this.isConnected()) await this.hub!.invoke('LeaveConversation', conversationId);
  }

  /** Call with `true` while the user types (at most every few seconds) and `false` when they stop. */
  async sendTyping(conversationId: string, isTyping: boolean): Promise<void> {
    if (this.isConnected() && this.conversations.has(conversationId)) {
      await this.hub!.invoke('SendTypingStatus', conversationId, isTyping);
    }
  }

  private isConnected(): boolean {
    return this.hub?.state === HubConnectionState.Connected;
  }

  private async connect(): Promise<void> {
    const hub = this.hub;
    if (!hub || this.stopped) return;
    this.setStatus('connecting');
    try {
      await hub.start();
      this.retryDelayMs = 5000;
      await this.afterConnect();
    } catch {
      if (this.stopped || this.hub !== hub) return;
      this.setStatus('disconnected');
      this.scheduleRetry();
    }
  }

  private async afterConnect(): Promise<void> {
    const hub = this.hub;
    if (!hub) return;
    this.setStatus('connected');
    for (const id of this.conversations) {
      try {
        await hub.invoke('JoinConversation', id);
      } catch (error) {
        // Access may have been revoked while disconnected: stop asking for it.
        if (hubErrorCode(error) === 'conversation_not_found') this.conversations.delete(id);
      }
    }
    try {
      const snapshot = await hub.invoke<UserPresenceResponse[]>('GetPresence');
      this.snapshotListeners.forEach((l) => l(snapshot));
    } catch {
      // Presence is best effort; live events still arrive.
    }
  }

  private scheduleRetry(): void {
    clearTimeout(this.retryTimer);
    const delay = this.retryDelayMs;
    this.retryDelayMs = Math.min(this.retryDelayMs * 2, this.maxRetryDelayMs);
    this.retryTimer = setTimeout(() => void this.connect(), delay);
  }

  private setStatus(status: RealtimeStatus): void {
    if (this.status === status) return;
    this.status = status;
    this.statusListeners.forEach((l) => l(status));
  }
}
