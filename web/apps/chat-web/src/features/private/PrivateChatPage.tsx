import { useCallback, useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type { AttachmentLimitsResponse, PrivateChatResponse, PrivateMessageResponse } from '../../shared/api/contracts';
import { ApiError } from '../../shared/api/httpClient';
import { Avatar } from '../../shared/components/Avatar';
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

/** What a private chat is and how long it lives, worded for the person reading it. */
export function PrivateRules({ initiatorName, isInitiator }: { initiatorName: string; isInitiator: boolean }) {
  const { t } = useTranslation();
  const items: { key: string; text: string }[] = [
    { key: 'memory', text: t('private.rules.memory') },
    { key: 'lifetime', text: isInitiator ? t('private.rules.lifetimeMine') : t('private.rules.lifetimeTheirs', { name: initiatorName }) },
    { key: 'files', text: t('private.rules.files') },
    { key: 'devices', text: isInitiator ? t('private.rules.devicesMine') : t('private.rules.devicesTheirs') },
    { key: 'screens', text: t('private.rules.screenshots') },
  ];
  return (
    <ol className="cx-private-rules">
      {items.map((item) => (
        <li key={item.key} className={`is-${item.key}`}>
          {item.text}
        </li>
      ))}
    </ol>
  );
}

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
    <PrivateFrame title={t('private.startTitle', { name: name ?? t('conversations.someone') })} subtitle={t('private.tagline')}>
      <div className="cx-private-body">
        <p>{t('private.startIntro', { name: name ?? t('conversations.someone') })}</p>
        <PrivateRules initiatorName="" isInitiator />
        {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        <div className="d-flex gap-2 flex-wrap">
          <button type="button" className="btn cx-private-primary" disabled={busy} onClick={() => void start()}>
            {busy ? t('auth.working') : t('private.sendInvitation')}
          </button>
          <button type="button" className="btn cx-private-ghost" onClick={() => window.history.back()}>
            {t('conversations.cancel')}
          </button>
        </div>
      </div>
    </PrivateFrame>
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

  return (
    <PrivateFrame title={t('private.listTitle')} subtitle={t('private.tagline')}>
      <div className="cx-private-body">
        {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        {chats === null ? (
          <p className="cx-private-muted">{t('auth.working')}</p>
        ) : chats.length === 0 ? (
          <p className="cx-private-muted">{t('private.listEmpty')}</p>
        ) : (
          <ul className="list-unstyled cx-private-list">
            {chats.map((chat) => (
              <li key={chat.id}>
                <Link to={`/private?id=${encodeURIComponent(chat.id)}`} className="cx-private-list-item">
                  {chat.counterpart && <Avatar user={chat.counterpart} size="sm" />}
                  <span className="flex-grow-1 min-w-0">
                    <span className="d-block text-truncate">{chat.counterpart?.displayName ?? t('conversations.someone')}</span>
                    <span className="cx-private-muted small">
                      {chat.status === 'invited'
                        ? chat.isInitiator
                          ? t('private.waitingShort')
                          : t('private.invitedShort')
                        : t('private.activeSince', { time: formatDateTime(chat.acceptedAtUtc ?? chat.createdAtUtc, i18n.language) })}
                    </span>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </PrivateFrame>
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
  const endButton = chat.isInitiator ? (
    <button
      type="button"
      className="btn btn-sm cx-private-danger"
      onClick={() => {
        if (window.confirm(t('private.endConfirm'))) void act(async () => {
          await api.endPrivateChat(chatId);
          finish('ended');
        });
      }}
    >
      {chat.status === 'invited' ? t('private.cancelInvitation') : t('private.end')}
    </button>
  ) : null;

  if (chat.status === 'invited') {
    return (
      <PrivateFrame title={t('private.withTitle', { name: other })} subtitle={t('private.tagline')} actions={endButton}>
        <div className="cx-private-body">
          {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
          {chat.isInitiator ? (
            <p className="lead" role="status">
              {t('private.waiting', { name: other, time: formatDateTime(chat.invitationExpiresAtUtc, i18n.language) })}
            </p>
          ) : (
            <p className="lead">{t('private.invitation', { name: other })}</p>
          )}
          <PrivateRules initiatorName={initiatorName} isInitiator={chat.isInitiator} />
          {!chat.isInitiator && (
            <div className="d-flex gap-2 flex-wrap">
              <button type="button" className="btn cx-private-primary" onClick={() => void act(async () => setChat(await api.acceptPrivateChat(chatId)))}>
                {t('private.accept')}
              </button>
              <button
                type="button"
                className="btn cx-private-ghost"
                onClick={() =>
                  void act(async () => {
                    await api.endPrivateChat(chatId);
                    finish('declined');
                  })
                }
              >
                {t('private.decline')}
              </button>
            </div>
          )}
        </div>
      </PrivateFrame>
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
    </PrivateFrame>
  );
}
