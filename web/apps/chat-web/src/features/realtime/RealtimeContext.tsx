import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useAuth } from '../auth/AuthContext';
import { RealtimeClient, type HubFactory, type PresenceValue, type RealtimeStatus } from './realtimeClient';
import { markDelivered } from '../conversations/conversationsApi';

interface RealtimeState {
  status: RealtimeStatus;
  /** Presence of the people the signed-in user may see (contacts and organization colleagues). */
  presence: ReadonlyMap<string, PresenceValue>;
  client: RealtimeClient | null;
}

const emptyPresence: ReadonlyMap<string, PresenceValue> = new Map();
const RealtimeContext = createContext<RealtimeState | null>(null);

/** Connects to the realtime hub while a user is signed in, and disconnects on sign-out. */
export function RealtimeProvider({ children, factory }: { children: ReactNode; factory?: HubFactory }) {
  const { user } = useAuth();
  const userId = user?.id;
  const [status, setStatus] = useState<RealtimeStatus>('disconnected');
  const [presence, setPresence] = useState<ReadonlyMap<string, PresenceValue>>(emptyPresence);
  const [client, setClient] = useState<RealtimeClient | null>(null);

  useEffect(() => {
    if (!userId) return;
    const realtime = new RealtimeClient(factory);
    const unsubscribe = [
      realtime.onStatus(setStatus),
      realtime.onPresenceSnapshot((snapshot) => setPresence(new Map(snapshot.map((p) => [p.userId, p.status])))),
      realtime.onPresence((event) =>
        setPresence((current) => {
          if (current.get(event.userId) === event.status) return current;
          const next = new Map(current);
          next.set(event.userId, event.status);
          return next;
        }),
      ),
    ];
    // ADR-023: this device acknowledges delivery of others' messages, at most once per
    // conversation every 500 ms with the highest sequence seen.
    const pendingAcks = new Map<string, number>();
    const ackTimers = new Map<string, ReturnType<typeof setTimeout>>();
    unsubscribe.push(
      realtime.onMessage((type, event) => {
        if (type !== 'MessageCreated' || event.entry.actor?.id === userId) return;
        pendingAcks.set(event.conversationId, Math.max(pendingAcks.get(event.conversationId) ?? 0, event.entry.sequence));
        if (ackTimers.has(event.conversationId)) return;
        ackTimers.set(
          event.conversationId,
          setTimeout(() => {
            ackTimers.delete(event.conversationId);
            const sequence = pendingAcks.get(event.conversationId);
            pendingAcks.delete(event.conversationId);
            if (sequence) void markDelivered(event.conversationId, sequence).catch(() => undefined);
          }, 500),
        );
      }),
    );
    setClient(realtime);
    void realtime.start();
    return () => {
      ackTimers.forEach((timer) => clearTimeout(timer));
      unsubscribe.forEach((u) => u());
      void realtime.stop();
      setClient(null);
      setPresence(emptyPresence);
      setStatus('disconnected');
    };
  }, [userId, factory]);

  const value = useMemo(() => ({ status, presence, client }), [status, presence, client]);
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

/** Realtime state; outside a provider (e.g. in isolated tests) it reports "disconnected" and no presence. */
export function useRealtime(): RealtimeState {
  return useContext(RealtimeContext) ?? { status: 'disconnected', presence: emptyPresence, client: null };
}

export function usePresence(userId: string): PresenceValue | undefined {
  return useRealtime().presence.get(userId);
}
