import { useCallback, useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type { AttachmentLimitsResponse, PrivateChatResponse, PrivateMessageResponse, UserSummary } from '../../shared/api/contracts';
import { NavIcon, type NavIconName } from '../../shared/components/NavIcon';
import { ApiError } from '../../shared/api/httpClient';
import { Avatar } from '../../shared/components/Avatar';
import { ConfirmDialog } from '../../shared/components/ConfirmDialog';
import { FormAlert } from '../../shared/components/Field';
import { formatDateTime } from '../../shared/format';
import { acceptFor, AttachButton, AttachmentList, DraftFileList, formatBytes, useDraftFiles } from '../attachments/Attachments';
import { getLimits } from '../attachments/attachmentsApi';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { LockGlyph } from '../../shared/components/LockGlyph';
import { useRealtime } from '../realtime/RealtimeContext';
import type { PrivateChatEndReason } from '../realtime/realtimeClient';
import * as api from './privateChatsApi';

/**
 * Private chats (owner request, Phase 7). Deliberately different from regular conversations:
 * a dark, sealed look, the rules always visible, and nothing kept on the device. Messages live in
 * this component's state only and are dropped the moment the chat ends.
 */
export function PrivateChatPage() {
  const { query } = useLocation();
  const id = query.get('id');
  const withUser = query.get('with');
  return (
    <RequireUser>
      {id ? (
        <PrivateChatRoom key={id} chatId={id} />
      ) : withUser ? (
        <StartPrivateChat userId={withUser} name={query.get('name')} />
      ) : (
        <PrivateChatList />
      )}
    </RequireUser>
  );
}

/**
 * What a private chat is and how long it lives, worded for the person reading it (redesign
 * 2026-11-24: rows with an icon, a title and a sentence, then the screenshot note).
 */
export function PrivateRules({ initiatorName, isInitiator }: { initiatorName: string; isInitiator: boolean }) {
  const { t } = useTranslation();
  const items: { key: string; icon: NavIconName; title: string; text: string }[] = [
    { key: 'memory', icon: 'message', title: t('private.rules.memoryTitle'), text: t('private.rules.memory') },
    {
      key: 'lifetime',
      icon: 'clock',
      title: isInitiator ? t('private.rules.lifetimeMineTitle') : t('private.rules.lifetimeTheirsTitle', { name: initiatorName }),
      text: isInitiator ? t('private.rules.lifetimeMine') : t('private.rules.lifetimeTheirs', { name: initiatorName }),
    },
    { key: 'files', icon: 'clip', title: t('private.rules.filesTitle'), text: t('private.rules.files') },
    {
      key: 'devices',
      icon: 'browser',
      title: isInitiator ? t('private.rules.devicesMineTitle') : t('private.rules.devicesTheirsTitle'),
      text: isInitiator ? t('private.rules.devicesMine') : t('private.rules.devicesTheirs'),
    },
  ];
  return (
    <>
      <ul className="cx-private-rules">
        {items.map((item) => (
          <li key={item.key} className={`is-${item.key}`}>
            <span className="cx-private-rule-icon" aria-hidden="true">
              <NavIcon name={item.icon} size={22} />
            </span>
            <span>
              <strong className="d-block">{item.title}</strong>
              <span className="cx-muted">{item.text}</span>
            </span>
          </li>
        ))}
      </ul>
      <p className="cx-private-note">
        <NavIcon name="info" size={20} />
        {t('private.rules.screenshots')}
      </p>
    </>
  );
}

/** The redesigned page: breadcrumb, title, subtitle and the "Session only" badge (2026-11-24). */
function PrivatePage({ title, subtitle, crumb, badge = true, listLink = false, children }: {
  title: string;
  subtitle: string;
  crumb?: string;
  badge?: boolean;
  /** The breadcrumb's last step links back to the list (invitation and closed screens). */
  listLink?: boolean;
  children: React.ReactNode;
}) {
  const { t } = useTranslation();
  return (
    <div className="cx-private-page">
      <header className="cx-page-header">
        <div>
          <nav aria-label={t('nav.label')} className="cx-breadcrumb">
            <span>{t('nav.workspace')}</span>
            <span aria-hidden="true">/</span>
            {crumb ? (
              <>
                <Link to="/private">{t('private.listTitle')}</Link>
                <span aria-hidden="true">/</span>
                <span aria-current="page">{crumb}</span>
              </>
            ) : (
              listLink ? <Link to="/private">{t('private.listTitle')}</Link> : <span aria-current="page">{t('private.listTitle')}</span>
            )}
          </nav>
          <h1 id="private-title" className="cx-page-title mb-1">{title}</h1>
          <p className="cx-muted mb-0">{subtitle}</p>
        </div>
        {badge && (
          <span className="cx-session-badge" title={t('private.sessionOnlyHint')}>
            <NavIcon name="lock" size={18} />
            {t('private.sessionOnly')}
          </span>
        )}
      </header>
      {children}
    </div>
  );
}

/** The invitation card: who, its state, the rules, and the actions at the bottom. */
function InvitationCard({ person, name, status, heading, lead, rules, footerNote, actions, error }: {
  person: UserSummary;
  name: string;
  status: string;
  heading: string;
  lead: React.ReactNode;
  rules: React.ReactNode;
  footerNote?: string;
  actions: React.ReactNode;
  error: unknown;
}) {
  const { t } = useTranslation();
  return (
    <section className="cx-card cx-private-card" aria-labelledby="private-card-title">
      <Link to="/private" className="cx-back-link">
        <NavIcon name="back" size={18} />
        {t('private.backToList')}
      </Link>
      <div className="cx-private-person">
        <Avatar user={person} size="md" />
        <span className="min-w-0">
          <strong className="d-block text-truncate">{name}</strong>
          <span className="cx-muted">{t('private.directConversation')}</span>
        </span>
        <span className="cx-status-pill">{status}</span>
      </div>
      <h2 id="private-card-title" className="cx-private-card-title">{heading}</h2>
      <div className="cx-muted cx-private-lead">{lead}</div>
      {rules}
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <div className="cx-private-card-footer">
        {footerNote && <span className="cx-muted">{footerNote}</span>}
        <div className="cx-private-card-actions">{actions}</div>
      </div>
    </section>
  );
}

/**
 * The sender's view while the invitation is open (redesign 2026-11-24, owner mockup): who, the
 * waiting state with the expiry, that nothing was shared yet, the rules, and Cancel invitation.
 */
function WaitingCard({ person, name, expiresAtUtc, error, onCancel }: {
  person: UserSummary;
  name: string;
  expiresAtUtc: string;
  error: unknown;
  onCancel: () => void;
}) {
  const { t, i18n } = useTranslation();
  const first = name.split(/\s+/)[0] ?? name;
  return (
    <section className="cx-card cx-private-card cx-private-waiting" aria-labelledby="private-card-title">
      <div className="cx-private-person">
        <Avatar user={person} size="lg" />
        <span className="min-w-0">
          <strong className="d-block text-truncate">{name}</strong>
          <span className="cx-muted">{t('private.directConversation')}</span>
        </span>
        <span className="cx-status-pill is-waiting">
          <NavIcon name="clock" size={18} />
          {t('private.status.awaiting')}
        </span>
      </div>
      <h2 id="private-card-title" className="cx-private-card-title">{t('private.waitingTitle', { name: first })}</h2>
      <p className="cx-muted mb-2" role="status">{t('private.waitingSent', { name: first })}</p>
      <p className="cx-private-expiry">
        <NavIcon name="clock" size={20} />
        {t('private.expiresAt', { time: dayTime(expiresAtUtc, i18n.language) })}
      </p>
      <p className="cx-private-shared">
        <NavIcon name="message" size={22} />
        {t('private.nothingShared')}
      </p>
      <PrivateRules initiatorName="" isInitiator />
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <div className="cx-private-waiting-foot">
        <Link to="/private" className="cx-back-link mb-0">
          <NavIcon name="chevron-left" size={18} />
          {t('private.backToList')}
        </Link>
        <button type="button" className="btn btn-outline-danger" onClick={onCancel}>{t('private.cancelInvitation')}</button>
      </div>
    </section>
  );
}

/** "Cancel this invitation?" (owner mockup): a dialog instead of the browser's confirm box. */
function CancelInvitationDialog({ name, busy, onKeep, onCancel }: { name: string; busy: boolean; onKeep: () => void; onCancel: () => void }) {
  const { t } = useTranslation();
  return (
    <ConfirmDialog title={t('private.cancelConfirmTitle')} text={t('private.cancelConfirmText', { name })}
      keepLabel={t('private.keepWaiting')} confirmLabel={t('private.cancelInvitation')} busyLabel={t('auth.working')}
      closeLabel={t('private.closeDialog')} busy={busy} onKeep={onKeep} onConfirm={onCancel}>
      <p className="cx-private-shared">
        <NavIcon name="message" size={22} />
        {t('private.nothingShared')}
      </p>
      <p className="cx-muted">{t('private.sendLater')}</p>
    </ConfirmDialog>
  );
}

/** "End this private chat?": everything is erased for both people, so it asks first. */
function EndChatDialog({ name, busy, onKeep, onEnd }: { name: string; busy: boolean; onKeep: () => void; onEnd: () => void }) {
  const { t } = useTranslation();
  return (
    <ConfirmDialog title={t('private.endConfirmTitle')} text={t('private.endConfirmText', { name })}
      keepLabel={t('private.keepChatting')} confirmLabel={t('private.end')} busyLabel={t('auth.working')}
      closeLabel={t('private.closeDialog')} busy={busy} onKeep={onKeep} onConfirm={onEnd}>
      <p className="cx-private-shared">
        <NavIcon name="lock" size={22} />
        {t('private.endNothingKept')}
      </p>
      <p className="cx-muted">{t('private.endCannotUndo')}</p>
    </ConfirmDialog>
  );
}

/** After cancelling (owner mockup): the invitation is closed, nothing was shared, where to go next. */
function InvitationCancelled({ name }: { name: string }) {
  const { t } = useTranslation();
  const first = name.split(/\s+/)[0] ?? name;
  return (
    <PrivatePage title={t('private.listTitle')} subtitle={t('private.closedSubtitle')} badge={false} listLink>
      <section className="cx-card cx-private-closed" aria-labelledby="private-closed-title" role="status">
        <span className="cx-private-closed-icon" aria-hidden="true"><NavIcon name="message-x" size={34} /></span>
        <span className="cx-private-closed-pill">{t('private.cancelledPill')}</span>
        <h2 id="private-closed-title" className="cx-private-closed-title">{t('private.closedTitle')}</h2>
        <p className="cx-muted cx-private-closed-text">{t('private.closedText', { name, first })}</p>
        <p className="cx-private-shared">
          <NavIcon name="message" size={22} />
          {t('private.noConversationStarted')}
        </p>
        <div className="cx-private-closed-actions">
          <Link to="/conversations" className="btn btn-primary btn-lg">{t('private.backToConversations')}</Link>
          <Link to="/private" className="btn btn-outline-primary btn-lg">{t('private.backToList')}</Link>
        </div>
        <p className="cx-muted mb-0">{t('private.newFromDirect')}</p>
      </section>
    </PrivatePage>
  );
}

/** "5:22 PM" today, otherwise the date and time. */
function dayTime(iso: string, language: string): string {
  const date = new Date(iso);
  return date.toDateString() === new Date().toDateString()
    ? date.toLocaleTimeString(language, { hour: 'numeric', minute: '2-digit' })
    : formatDateTime(iso, language);
}

const initialsOf = (name: string) => name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '?';

function PrivateFrame({ title, subtitle, actions, children }: { title: string; subtitle?: string; actions?: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="cx-private" aria-labelledby="private-title">
      <header className="cx-private-header">
        <span className="cx-private-seal" aria-hidden="true">
          <LockGlyph />
        </span>
        <div className="min-w-0 flex-grow-1">
          <h1 id="private-title" className="cx-private-title text-truncate">
            {title}
          </h1>
          {subtitle && <p className="cx-private-subtitle mb-0">{subtitle}</p>}
        </div>
        {actions && <div className="d-flex gap-2 flex-wrap">{actions}</div>}
      </header>
      {children}
    </section>
  );
}

function StartPrivateChat({ userId, name }: { userId: string; name: string | null }) {
  const { t } = useTranslation();
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const who = name ?? t('conversations.someone');
  const first = who.split(/\s+/)[0] ?? who;
  const start = async () => {
    setBusy(true);
    setError(null);
    try {
      const chat = await api.startPrivateChat(userId);
      navigate(`/private?id=${encodeURIComponent(chat.id)}`, { replace: true });
    } catch (err) {
      setError(err);
      setBusy(false);
    }
  };
  return (
    <PrivatePage title={t('private.inviteTitle')} subtitle={t('private.inviteSubtitle')} crumb={t('private.invitationCrumb')}>
      <InvitationCard
        // Only the id and name are known before sending: the photo address works without a version and
        // falls back to the initials (404) for people without a photo.
        person={{ id: userId, displayName: who, avatar: { initials: initialsOf(who), photoUrl: `/api/v1/users/${encodeURIComponent(userId)}/photo` } }}
        name={who}
        status={t('private.status.notSent')}
        heading={t('private.beforeSending')}
        lead={t('private.mustAccept', { name: first })}
        rules={<PrivateRules initiatorName="" isInitiator />}
        footerNote={t('private.noMessagesSent')}
        error={error}
        actions={(
          <>
            <button type="button" className="btn btn-outline-secondary" onClick={() => window.history.back()}>
              {t('conversations.cancel')}
            </button>
            <button type="button" className="btn btn-primary cx-icon-button" disabled={busy} onClick={() => void start()}>
              <NavIcon name="send" size={18} />
              {busy ? t('auth.working') : t('private.sendInvitation')}
            </button>
          </>
        )}
      />
      <p className="cx-private-after cx-muted">{t('private.afterSending', { name: first })}</p>
    </PrivatePage>
  );
}

function PrivateChatList() {
  const { t, i18n } = useTranslation();
  const { client } = useRealtime();
  const [chats, setChats] = useState<PrivateChatResponse[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const reload = useCallback(() => {
    api.listPrivateChats().then(setChats, setError);
  }, []);
  useEffect(reload, [reload]);
  useEffect(() => {
    if (!client) return;
    const off = [client.onPrivateChat(reload), client.onReconnected(reload)];
    return () => off.forEach((o) => o());
  }, [client, reload]);

  const facts: { icon: NavIconName; title: string; text: string }[] = [
    { icon: 'clock', title: t('private.facts.sessionTitle'), text: t('private.facts.session') },
    { icon: 'history', title: t('private.facts.historyTitle'), text: t('private.facts.history') },
    { icon: 'browser', title: t('private.facts.browserTitle'), text: t('private.facts.browser') },
  ];

  return (
    <PrivatePage title={t('private.listTitle')} subtitle={t('private.listSubtitle')}>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <section className="cx-card cx-private-card" aria-labelledby="private-list-title">
        {chats === null ? (
          <p className="cx-muted mb-0">{t('auth.working')}</p>
        ) : chats.length === 0 ? (
          <div className="cx-private-empty">
            <PrivateIllustration />
            <h2 id="private-list-title">{t('private.noneTitle')}</h2>
            <p className="cx-muted">{t('private.noneText')}</p>
            <Link to="/conversations" className="btn btn-primary btn-lg cx-icon-button">
              <NavIcon name="chats" size={22} />
              {t('private.openConversations')}
            </Link>
            <p className="cx-muted small mt-2 mb-0">{t('private.openHint')}</p>
          </div>
        ) : (
          <>
            <h2 id="private-list-title" className="cx-section-title">{t('private.yourChats')}</h2>
            <ul className="list-unstyled cx-private-list">
              {chats.map((chat) => (
                <li key={chat.id}>
                  <Link to={`/private?id=${encodeURIComponent(chat.id)}`} className="cx-private-list-item">
                    {chat.counterpart && <Avatar user={chat.counterpart} size="sm" />}
                    <span className="flex-grow-1 min-w-0">
                      <strong className="d-block text-truncate">{chat.counterpart?.displayName ?? t('conversations.someone')}</strong>
                      <span className="cx-muted small">
                        {chat.status === 'invited'
                          ? chat.isInitiator
                            ? t('private.waitingShort')
                            : t('private.invitedShort')
                          : t('private.activeSince', { time: formatDateTime(chat.acceptedAtUtc ?? chat.createdAtUtc, i18n.language) })}
                      </span>
                    </span>
                    <span className={`cx-status-pill${chat.status === 'active' ? ' is-active' : ''}`}>
                      {t(chat.status === 'active' ? 'private.status.active' : chat.isInitiator ? 'private.status.waiting' : 'private.status.invitation')}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          </>
        )}
        <ul className="list-unstyled cx-private-facts">
          {facts.map((fact) => (
            <li key={fact.title}>
              <NavIcon name={fact.icon} size={30} />
              <strong>{fact.title}</strong>
              <span className="cx-muted">{fact.text}</span>
            </li>
          ))}
        </ul>
      </section>
      <aside className="cx-card cx-private-before">
        <NavIcon name="info" size={22} />
        <span>
          <strong className="d-block">{t('private.beforeYouStart')}</strong>
          <span className="cx-muted">{t('private.beforeYouStartText')}</span>
        </span>
      </aside>
    </PrivatePage>
  );
}

/** A chat bubble with a lock badge (drawn here). */
function PrivateIllustration() {
  return (
    <svg className="cx-private-illustration" viewBox="0 0 120 80" width="168" height="112" aria-hidden="true" focusable="false">
      <g fill="none" stroke="currentColor" strokeWidth="3.2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M22 8h40a10 10 0 0 1 10 10v20a10 10 0 0 1-10 10H40l-14 10v-10h-4a10 10 0 0 1-10-10V18A10 10 0 0 1 22 8z" />
        <path d="M76 32c6 0 10 2 12 6M48 58c4 6 12 8 24 8h6" />
      </g>
      <g fill="currentColor">
        <circle cx="31" cy="28" r="3" />
        <circle cx="42" cy="28" r="3" />
        <circle cx="53" cy="28" r="3" />
      </g>
      <circle className="cx-private-illustration-badge" cx="94" cy="56" r="17" />
      <g fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round">
        <rect x="87" y="55" width="14" height="10" rx="2" />
        <path d="M90 55v-3a4 4 0 0 1 8 0v3" />
      </g>
    </svg>
  );
}

function newClientId(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function upsertMessage(list: PrivateMessageResponse[], message: PrivateMessageResponse): PrivateMessageResponse[] {
  if (list.some((m) => m.id === message.id)) return list;
  return [...list, message].sort((a, b) => a.sequence - b.sequence);
}

function PrivateChatRoom({ chatId }: { chatId: string }) {
  const { t, i18n } = useTranslation();
  const { user } = useAuth();
  const { client } = useRealtime();
  const [chat, setChat] = useState<PrivateChatResponse | null>(null);
  const [messages, setMessages] = useState<PrivateMessageResponse[]>([]);
  const [ended, setEnded] = useState<PrivateChatEndReason | 'gone' | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  /** Who the cancelled invitation was for: the closed page names them. */
  const [cancelledFor, setCancelledFor] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [draft, setDraft] = useState('');
  const [sending, setSending] = useState(false);
  const [limits, setLimits] = useState<AttachmentLimitsResponse | null>(null);
  const [fileNotice, setFileNotice] = useState<string | null>(null);
  const logEnd = useRef<HTMLDivElement>(null);
  const upload = useCallback((file: File) => api.uploadPrivateFile(chatId, file), [chatId]);
  const drafts = useDraftFiles(upload);
  const clearDrafts = drafts.clear;
  const me = user?.id ?? '';

  /** Forget everything that was shown: the chat is over. */
  const finish = useCallback(
    (reason: PrivateChatEndReason | 'gone') => {
      setMessages([]);
      setDraft('');
      clearDrafts();
      setChat(null);
      setEnded(reason);
    },
    [clearDrafts],
  );

  const load = useCallback(async () => {
    try {
      const detail = await api.getPrivateChat(chatId);
      setChat(detail.chat);
      setMessages(detail.messages);
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) finish('gone');
      else setError(err);
    }
  }, [chatId, finish]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    getLimits().then(setLimits, () => setLimits(null));
  }, []);

  useEffect(() => {
    if (!client) return;
    const off = [
      client.onPrivateMessage((event) => {
        if (event.chatId !== chatId) return;
        setMessages((current) => upsertMessage(current, event.message));
        // Files change the chat's memory use; the summary carries the current figure.
        if (event.message.attachments.length > 0) void load();
      }),
      client.onPrivateChat((event) => {
        if (event.chatId !== chatId) return;
        if (event.status === 'ended') finish(event.reason ?? 'ended');
        else void load();
      }),
      client.onReconnected(() => void load()),
    ];
    return () => off.forEach((o) => o());
  }, [client, chatId, finish, load]);

  // Nothing private outlives the view: clear state when leaving the page.
  useEffect(() => () => setMessages([]), []);

  useEffect(() => {
    logEnd.current?.scrollIntoView?.({ block: 'end' });
  }, [messages.length]);

  const act = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(err);
    }
  };

  const submit = async (event?: FormEvent) => {
    event?.preventDefault();
    const body = draft.trim();
    if ((!body && drafts.readyIds.length === 0) || drafts.busy || sending) return;
    setSending(true);
    setError(null);
    const message = { clientMessageId: newClientId(), body, attachmentIds: drafts.readyIds };
    try {
      // A retry with the same clientMessageId is answered with the original message.
      let saved: PrivateMessageResponse;
      try {
        saved = await api.sendPrivateMessage(chatId, message);
      } catch (first) {
        if (!(first instanceof ApiError) || first.kind === 'http') throw first;
        saved = await api.sendPrivateMessage(chatId, message);
      }
      setMessages((current) => upsertMessage(current, saved));
      setDraft('');
      drafts.clear();
      if (message.attachmentIds.length > 0) void load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) finish('gone');
      else setError(err);
    } finally {
      setSending(false);
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void submit();
    }
  };

  if (cancelledFor !== null) return <InvitationCancelled name={cancelledFor} />;

  if (ended) {
    return (
      <PrivateFrame title={t('private.endedTitle')} subtitle={t('private.tagline')}>
        <div className="cx-private-body cx-private-ended" role="status">
          <p className="lead">{t(`private.endReasons.${ended}`)}</p>
          <p>{t('private.erased')}</p>
          <Link to="/conversations" className="btn cx-private-primary">
            {t('private.backToConversations')}
          </Link>
        </div>
      </PrivateFrame>
    );
  }

  if (!chat) {
    return (
      <PrivateFrame title={t('private.title')}>
        <div className="cx-private-body">
          {error !== null ? <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert> : <p className="cx-private-muted mb-0">{t('auth.working')}</p>}
        </div>
      </PrivateFrame>
    );
  }

  const other = chat.counterpart?.displayName ?? t('conversations.someone');
  const initiatorName = chat.isInitiator ? (user?.displayName ?? '') : (chat.initiator?.displayName ?? other);
  /** Cancels the invitation or ends the chat (initiator only), after the dialog's confirmation. */
  const endChat = async (cancelled: boolean) => {
    setCancelling(true);
    setError(null);
    try {
      await api.endPrivateChat(chatId);
      finish('ended');
      if (cancelled) setCancelledFor(other);
    } catch (err) {
      setError(err);
    } finally {
      setCancelling(false);
      setConfirming(false);
    }
  };
  const endButton = chat.isInitiator ? (
    <button type="button" className="btn btn-sm cx-private-danger" onClick={() => setConfirming(true)}>
      {t('private.end')}
    </button>
  ) : null;

  if (chat.status === 'invited' && chat.isInitiator) {
    const person = chat.counterpart ?? { id: 'someone', displayName: other, avatar: { initials: initialsOf(other) } };
    const cancel = () => endChat(true);
    return (
      <PrivatePage title={t('private.invitedTitle')} subtitle={t('private.agreementSubtitle')} badge={false} listLink>
        <WaitingCard person={person} name={other} expiresAtUtc={chat.invitationExpiresAtUtc} error={error} onCancel={() => setConfirming(true)} />
        {confirming && <CancelInvitationDialog name={other} busy={cancelling} onKeep={() => setConfirming(false)} onCancel={() => void cancel()} />}
      </PrivatePage>
    );
  }

  if (chat.status === 'invited') {
    const person = chat.counterpart ?? { id: 'someone', displayName: other, avatar: { initials: initialsOf(other) } };
    return (
      <PrivatePage title={chat.isInitiator ? t('private.inviteTitle') : t('private.invitedTitle')} subtitle={t('private.inviteSubtitle')}
        crumb={t('private.invitationCrumb')}>
        <InvitationCard
          person={person}
          name={other}
          status={t(chat.isInitiator ? 'private.status.waiting' : 'private.status.invitation')}
          heading={chat.isInitiator ? t('private.waitingTitle', { name: other }) : t('private.invitation', { name: other })}
          lead={chat.isInitiator
            ? <span role="status">{t('private.waiting', { name: other, time: formatDateTime(chat.invitationExpiresAtUtc, i18n.language) })}</span>
            : t('private.invitationLead')}
          rules={<PrivateRules initiatorName={initiatorName} isInitiator={chat.isInitiator} />}
          footerNote={chat.isInitiator ? t('private.noMessagesSent') : t('private.nothingUntilAccept')}
          error={error}
          actions={chat.isInitiator ? endButton : (
            <>
              <button
                type="button"
                className="btn btn-outline-secondary"
                onClick={() =>
                  void act(async () => {
                    await api.endPrivateChat(chatId);
                    finish('declined');
                  })
                }
              >
                {t('private.decline')}
              </button>
              <button type="button" className="btn btn-primary" onClick={() => void act(async () => setChat(await api.acceptPrivateChat(chatId)))}>
                {t('private.accept')}
              </button>
            </>
          )}
        />
      </PrivatePage>
    );
  }

  const lifetime = chat.isInitiator ? t('private.bannerMine') : t('private.bannerTheirs', { name: initiatorName });
  return (
    <PrivateFrame title={t('private.withTitle', { name: other })} subtitle={lifetime} actions={endButton}>
      <div className="cx-private-banner" role="note">
        <strong>{t('private.bannerTitle')}</strong> {t('private.bannerText')}{' '}
        <span className="cx-private-muted">
          {t('private.memoryUse', { used: formatBytes(chat.usedBytes, i18n.language), max: formatBytes(chat.maxBytes, i18n.language) })}
        </span>
      </div>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {fileNotice && <FormAlert tone="info">{fileNotice}</FormAlert>}

      <div className="cx-private-timeline" role="log" aria-label={t('private.history')} aria-live="polite">
        {messages.length === 0 ? (
          <p className="cx-private-muted text-center my-4">{t('private.empty')}</p>
        ) : (
          <ol className="list-unstyled mb-0">
            {messages.map((m) => {
              const mine = m.authorId === me;
              return (
                <li key={m.id} className={`cx-private-message${mine ? ' is-mine' : ''}`}>
                  <div className="cx-private-bubble">
                    {m.body && <p className="cx-message-body">{m.body}</p>}
                    <AttachmentList items={m.attachments} urlFor={(item, download) => api.privateFileUrl(chatId, item.id, download)} />
                    <time className="cx-private-time" dateTime={m.createdAtUtc}>
                      {formatDateTime(m.createdAtUtc, i18n.language)}
                    </time>
                  </div>
                </li>
              );
            })}
          </ol>
        )}
        <div ref={logEnd} />
      </div>

      <form className="cx-private-composer" onSubmit={(e) => void submit(e)}>
        <DraftFileList files={drafts.files} onRemove={drafts.remove} />
        <label htmlFor="private-composer" className="visually-hidden">
          {t('messages.compose')}
        </label>
        <div className="cx-composer-row">
          <AttachButton
            id={`private-attach-${chatId}`}
            accept={acceptFor(limits?.allowedExtensions)}
            onFiles={(chosen) => {
              setFileNotice(null);
              drafts.add(
                chosen,
                limits ? { maxFileSize: chat.maxFileSize, maxPerMessage: limits.maxPerMessage, allowedExtensions: limits.allowedExtensions } : null,
                setFileNotice,
                {
                  tooLarge: t('attachments.tooLarge', { size: formatBytes(chat.maxFileSize, i18n.language) }),
                  type: t('attachments.typeNotAllowed'),
                  tooMany: t('attachments.tooMany', { count: limits?.maxPerMessage ?? 0 }),
                },
              );
            }}
          />
          <textarea
            id="private-composer"
            className="form-control"
            rows={2}
            maxLength={4000}
            autoComplete="off"
            placeholder={t('private.placeholder')}
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={onKeyDown}
          />
          <button type="submit" className="btn cx-private-primary" disabled={sending || drafts.busy || (!draft.trim() && drafts.readyIds.length === 0)}>
            {t('messages.send')}
          </button>
        </div>
      </form>
      {confirming && <EndChatDialog name={other} busy={cancelling} onKeep={() => setConfirming(false)} onEnd={() => void endChat(false)} />}
    </PrivateFrame>
  );
}
