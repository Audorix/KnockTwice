import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { navigate, useLocation } from '../../app/router';
import type { ConversationEntryResponse, ConversationSummaryResponse } from '../../shared/api/contracts';
import { useAuth } from '../auth/AuthContext';
import { countUnreadMentions, listConversations } from '../conversations/conversationsApi';
import { listReminders } from '../planning/planningApi';
import { listPrivateChats } from '../private/privateChatsApi';
import { useRealtime } from '../realtime/RealtimeContext';
import { toPlainText } from '../../shared/richText';

/**
 * Notifications (Phase 8, owner decision): unread badges and the tab title count, in-app toasts
 * for messages outside the open chat, and optional desktop notifications (profile setting plus
 * the browser's permission) while the page is hidden. Notification levels (Phase 17) apply:
 * "only mentions" notifies when you are mentioned, "nothing" never. Private
 * chat messages never show their text outside the private chat itself.
 */

export interface Toast {
  id: string;
  title: string;
  body: string;
  href: string;
  isPrivate: boolean;
}

interface NotificationsState {
  /** Unread messages across conversations that are not muted. */
  unread: number;
  /** Private chat invitations waiting for an answer. */
  invitations: number;
  /** Phase 17: mentions of you in unread messages. */
  mentions: number;
  /** Phase 20: reminders that came due and are not done yet. */
  dueReminders: number;
  toasts: readonly Toast[];
  dismiss: (id: string) => void;
}

const empty: NotificationsState = { unread: 0, invitations: 0, mentions: 0, dueReminders: 0, toasts: [], dismiss: () => undefined };

/** Whether a message mentions the user: by name, @everyone, or @here (only online people receive it live). */
export function mentionsUser(entry: ConversationEntryResponse, userId: string): boolean {
  const m = entry.mentions;
  return Boolean(m && (m.userIds.includes(userId) || m.everyone || m.here));
}
const NotificationsContext = createContext<NotificationsState>(empty);
const toastLifetimeMs = 6000;

export function desktopNotificationsSupported(): boolean {
  return typeof window !== 'undefined' && 'Notification' in window;
}

export function NotificationsProvider({ children }: { children: ReactNode }) {
  const { t, i18n } = useTranslation();
  const { user } = useAuth();
  const { client } = useRealtime();
  const { pathname, query } = useLocation();
  const [conversations, setConversations] = useState<ConversationSummaryResponse[]>([]);
  const [invitations, setInvitations] = useState(0);
  const [mentions, setMentions] = useState(0);
  const [dueReminders, setDueReminders] = useState(0);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const baseTitle = useRef(typeof document !== 'undefined' ? document.title : '');
  const where = useRef({ pathname, id: query.get('id') });
  where.current = { pathname, id: query.get('id') };
  const prefs = useRef(user?.preferences);
  prefs.current = user?.preferences;
  const known = useRef(new Map<string, ConversationSummaryResponse>());

  const reload = useCallback(() => {
    listConversations().then(
      (list) => {
        known.current = new Map(list.map((c) => [c.id, c]));
        setConversations(list);
      },
      () => undefined,
    );
    listPrivateChats().then(
      (chats) => setInvitations(chats.filter((c) => c.status === 'invited' && !c.isInitiator).length),
      () => undefined,
    );
    countUnreadMentions().then(setMentions, () => undefined);
    listReminders().then((all) => setDueReminders(all.filter((r) => r.status === 'due').length), () => undefined);
  }, []);

  const dismiss = useCallback((id: string) => setToasts((current) => current.filter((x) => x.id !== id)), []);

  const notify = useCallback(
    (toast: Toast, desktopBody: string) => {
      const hidden = document.visibilityState === 'hidden';
      if (!hidden) {
        setToasts((current) => [...current.filter((x) => x.href !== toast.href), toast].slice(-3));
        setTimeout(() => dismiss(toast.id), toastLifetimeMs);
        return;
      }

      if (!prefs.current?.desktopNotifications || !desktopNotificationsSupported() || Notification.permission !== 'granted') return;
      try {
        const notification = new Notification(toast.title, { body: desktopBody, tag: toast.href, ...(document.documentElement.lang ? { lang: document.documentElement.lang } : {}) });
        notification.onclick = () => {
          window.focus();
          navigate(toast.href);
          notification.close();
        };
      } catch {
        // Some browsers only allow notifications from a service worker; the badge still shows.
      }
    },
    [dismiss],
  );

  useEffect(() => {
    if (!user || !client) {
      setConversations([]);
      setInvitations(0);
      setMentions(0);
      setDueReminders(0);
      setToasts([]);
      return;
    }

    reload();
    let pending: ReturnType<typeof setTimeout> | undefined;
    const soon = () => {
      if (pending) return;
      pending = setTimeout(() => {
        pending = undefined;
        reload();
      }, 400);
    };

    const off = [
      client.onMessage((type, event) => {
        soon();
        if (type !== 'MessageCreated' || event.entry.actor?.id === user.id) return;
        const conversation = known.current.get(event.conversationId);
        const level = conversation?.notificationLevel ?? (conversation?.muted ? 'mentions' : 'all');
        const mentioned = mentionsUser(event.entry, user.id);
        // Levels (Phase 17): "mentions" notifies only when mentioned, "nothing" never.
        if (level === 'nothing' || (level === 'mentions' && !mentioned)) return;
        // Thread replies kept out of the conversation reach followers by push and email only (ADR-030), unless they mention you.
        if (event.entry.threadRootId && !event.entry.shownInConversation && !mentioned) return;
        const open = where.current.pathname === '/conversations' && where.current.id === event.conversationId;
        if (open && document.visibilityState === 'visible') return;
        const author = event.entry.actor?.displayName ?? t('conversations.someone');
        const title = mentioned
          ? t('notifications.mentionedBy', { name: author })
          : conversation?.type === 'group' ? t('notifications.inGroup', { name: author, group: conversation.title }) : author;
        const text = toPlainText(event.entry.body) || t('notifications.sentFiles', { count: event.entry.attachments?.length ?? 1 });
        notify(
          { id: event.eventId, title, body: text, href: `/conversations?id=${encodeURIComponent(event.conversationId)}`, isPrivate: false },
          prefs.current?.notificationPreviews === false ? t('notifications.newMessage') : text,
        );
      }),
      client.onConversationUpdated(soon),
      client.onReconnected(soon),
      client.onPlanning((event) => event.kind === 'reminders' && soon()),
      // Phase 20: a reminder came due (also shown on the desktop when the page is hidden).
      client.onReminderDue((event) => {
        soon();
        const href =
          `/conversations?id=${encodeURIComponent(event.conversationId)}&message=${encodeURIComponent(event.messageId)}` +
          (event.threadRootId ? `&thread=${encodeURIComponent(event.threadRootId)}` : '');
        const body = event.excerpt ? `${event.authorName ?? t('conversations.someone')}: ${event.excerpt}` : t('planning.reminderFrom', { name: event.authorName ?? t('conversations.someone') });
        notify(
          { id: `reminder-${event.reminderId}`, title: t('planning.reminderTitle'), body, href, isPrivate: false },
          prefs.current?.notificationPreviews === false ? t('planning.reminderFrom', { name: event.authorName ?? t('conversations.someone') }) : body,
        );
      }),
      client.onPrivateChat((event) => {
        soon();
        if (event.status !== 'invited') return;
        // The invitation toast (PrivateChatInvites) covers the visible page; desktop only when hidden.
        if (document.visibilityState === 'hidden') {
          notify({ id: `invite-${event.chatId}`, title: t('notifications.privateInvite'), body: '', href: `/private?id=${encodeURIComponent(event.chatId)}`, isPrivate: true }, '');
        }
      }),
      client.onPrivateMessage((event) => {
        if (event.message.authorId === user.id) return;
        const open = where.current.pathname === '/private' && where.current.id === event.chatId;
        if (open && document.visibilityState === 'visible') return;
        const author = event.message.author?.displayName ?? t('conversations.someone');
        notify(
          {
            id: event.message.id,
            title: t('notifications.privateFrom', { name: author }),
            body: t('notifications.privateHidden'),
            href: `/private?id=${encodeURIComponent(event.chatId)}`,
            isPrivate: true,
          },
          t('notifications.privateHidden'),
        );
      }),
    ];
    return () => {
      clearTimeout(pending);
      off.forEach((o) => o());
    };
  }, [user, client, reload, notify, t]);

  // Reading a conversation changes the counts; refresh when the open conversation changes.
  useEffect(() => {
    if (user && client) reload();
  }, [pathname, user, client, reload]);

  const unread = useMemo(() => conversations.filter((c) => !c.muted).reduce((sum, c) => sum + c.unreadCount, 0), [conversations]);

  // A language change resets the document title (i18n), so the count is put back after it too.
  useEffect(() => {
    const base = t('app.documentTitle') || baseTitle.current || 'Knock Twice';
    document.title = unread > 0 ? `(${unread > 99 ? '99+' : unread}) ${base}` : base;
  }, [unread, i18n.language, t]);

  const value = useMemo(() => ({ unread, invitations, mentions, dueReminders, toasts, dismiss }), [unread, invitations, mentions, dueReminders, toasts, dismiss]);
  return <NotificationsContext.Provider value={value}>{children}</NotificationsContext.Provider>;
}

export function useNotifications(): NotificationsState {
  return useContext(NotificationsContext);
}

/** In-app toasts for messages that arrive outside the open conversation. */
export function NotificationToasts() {
  const { t } = useTranslation();
  const { toasts, dismiss } = useNotifications();
  return (
    <div className="cx-toasts" role="region" aria-label={t('notifications.label')} aria-live="polite">
      {toasts.map((toast) => (
        <div key={toast.id} className={`cx-toast${toast.isPrivate ? ' is-private' : ''}`}>
          <button
            type="button"
            className="cx-toast-body"
            onClick={() => {
              dismiss(toast.id);
              navigate(toast.href);
            }}
          >
            <strong className="d-block text-truncate">{toast.title}</strong>
            {toast.body && <span className="cx-toast-text">{toast.body}</span>}
          </button>
          <button type="button" className="btn-close btn-sm" aria-label={t('notifications.dismiss')} onClick={() => dismiss(toast.id)} />
        </div>
      ))}
    </div>
  );
}
