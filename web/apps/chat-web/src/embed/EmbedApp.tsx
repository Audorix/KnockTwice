import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { ConversationEntryResponse, MessageAttachmentResponse, UserSummary, WidgetConfigResponse } from '../shared/api/contracts';
import { ApiError } from '../shared/api/httpClient';
import { RichText } from '../shared/richText';
import { HostBridge, type HostCommand } from './hostBridge';
import { loadSession, saveSession, widgetApi, type StoredSession } from './widgetApi';
import { VisitorRealtime } from './visitorRealtime';

export interface EmbedAppProps {
  widgetId: string;
  parentOrigin: string;
  language: string;
  /** The host page chose the language; otherwise the widget's default (unless "auto") applies. */
  languageExplicit?: boolean;
  bridge: HostBridge;
  /** Test seam for the realtime connection. */
  realtimeFactory?: (token: () => string, handlers: ConstructorParameters<typeof VisitorRealtime>[1]) => VisitorRealtime;
}

interface Thread {
  conversationId: string | null;
  visitorUserId: string;
  items: ConversationEntryResponse[];
  responders: UserSummary[];
  lastReadSequence: number;
  nextCursor: string | null;
  hasMore: boolean;
}

/** The `sub` claim of an identity token, read without verification, only to notice a different person. */
export function tokenSubject(token: string | null | undefined): string | null {
  const payload = token?.split('.')[1];
  if (!payload) return null;
  try {
    const json = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as { sub?: unknown };
    return typeof json.sub === 'string' ? json.sub : null;
  } catch {
    return null;
  }
}

/** Readable text colour on the widget's accent colour (WCAG relative luminance). */
export function textOn(hex: string): string {
  const channels = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  const luminance = 0.2126 * channels[0]! + 0.7152 * channels[1]! + 0.0722 * channels[2]!;
  return luminance > 0.45 ? '#111827' : '#ffffff';
}

function mergeEntries(current: ConversationEntryResponse[], incoming: ConversationEntryResponse[]): ConversationEntryResponse[] {
  const byId = new Map(current.map((e) => [e.id, e]));
  for (const entry of incoming) {
    const existing = byId.get(entry.id);
    if (!existing || (entry.version ?? 0) >= (existing.version ?? 0)) byId.set(entry.id, entry);
  }
  return [...byId.values()].sort((a, b) => a.sequence - b.sequence);
}

type StoredWithSubject = StoredSession & { subject?: string | null };

/**
 * The embedded chat (Phase 10): a launcher and a panel inside the frame the loader creates. A
 * visitor session is only started when the visitor opens the chat (or the host identifies them),
 * so page views alone never create sessions or count against the anonymous limit.
 */
export function EmbedApp({ widgetId, parentOrigin, language: initialLanguage, languageExplicit = false, bridge, realtimeFactory }: EmbedAppProps) {
  const { t, i18n } = useTranslation();
  const [language, setLanguage] = useState(initialLanguage);
  const [config, setConfig] = useState<WidgetConfigResponse | null | 'unavailable'>(null);
  const [open, setOpen] = useState(false);
  const [session, setSession] = useState<StoredWithSubject | null>(() => loadSession(widgetId));
  const [thread, setThread] = useState<Thread | null>(null);
  const [draft, setDraft] = useState('');
  const [files, setFiles] = useState<MessageAttachmentResponse[]>([]);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [typing, setTyping] = useState<Set<string>>(new Set());
  const hostToken = useRef<string | null>(null);
  const sessionRef = useRef(session);
  const realtime = useRef<VisitorRealtime | null>(null);
  const lastTyping = useRef(0);
  const listRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const launcherRef = useRef<HTMLButtonElement>(null);
  sessionRef.current = session;

  const remember = useCallback((next: StoredWithSubject | null) => {
    sessionRef.current = next;
    setSession(next);
    saveSession(widgetId, next);
  }, [widgetId]);

  const errorText = useCallback((error: unknown): string => {
    if (error instanceof ApiError) {
      if (error.status === 429) return t('embed.errors.tooMany');
      if (error.code === 'widget_origin_not_allowed') return t('embed.errors.origin');
      if (error.code && t(`embed.errors.${error.code}`, { defaultValue: '' })) return t(`embed.errors.${error.code}`);
      if (error.kind !== 'http') return t('embed.errors.network');
    }
    return t('embed.errors.generic');
  }, [t]);

  // ---- Configuration ------------------------------------------------------------------------
  useEffect(() => {
    let cancelled = false;
    widgetApi.config(widgetId, initialLanguage).then(
      (value) => !cancelled && setConfig(value),
      () => !cancelled && setConfig('unavailable'),
    );
    return () => {
      cancelled = true;
    };
  }, [widgetId, initialLanguage]);

  const ready = config && config !== 'unavailable' ? config : null;

  useEffect(() => {
    if (!ready || languageExplicit || ready.defaultLanguage === 'auto' || ready.defaultLanguage === language) return;
    setLanguage(ready.defaultLanguage);
    void i18n.changeLanguage(ready.defaultLanguage);
  }, [ready, languageExplicit, language, i18n]);

  // ---- Session --------------------------------------------------------------------------------
  const startSession = useCallback(async (): Promise<StoredWithSubject> => {
    const identityToken = hostToken.current;
    const started = await widgetApi.startSession(widgetId, parentOrigin, language, identityToken);
    const next: StoredWithSubject = {
      token: started.token,
      identified: started.identified,
      displayName: started.displayName,
      expiresAtUtc: started.expiresAtUtc,
      subject: tokenSubject(identityToken),
    };
    remember(next);
    return next;
  }, [widgetId, parentOrigin, language, remember]);

  const ensureSession = useCallback(async (): Promise<StoredWithSubject> => sessionRef.current ?? startSession(), [startSession]);

  /** Runs a visitor call; an ended session is replaced once (anonymous visitors start over). */
  const withSession = useCallback(async <T,>(action: (token: string) => Promise<T>): Promise<T> => {
    const current = await ensureSession();
    try {
      return await action(current.token);
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        remember(null);
        setThread(null);
        const fresh = await startSession();
        return action(fresh.token);
      }
      throw error;
    }
  }, [ensureSession, remember, startSession]);

  const loadThread = useCallback(async () => {
    const view = await withSession((token) => widgetApi.conversation(token));
    setThread((current) => ({
      conversationId: view.conversationId,
      visitorUserId: view.visitorUserId,
      items: mergeEntries(current?.conversationId === view.conversationId ? current.items : [], view.items),
      responders: view.responders,
      lastReadSequence: view.lastReadSequence,
      nextCursor: view.nextCursor,
      hasMore: view.hasMore,
    }));
    return view;
  }, [withSession]);

  // ---- Realtime -------------------------------------------------------------------------------
  const connect = useCallback(async () => {
    if (realtime.current || !sessionRef.current) return;
    const handlers = {
      onMessage: (_type: string, event: { conversationId: string; entry: ConversationEntryResponse }) =>
        setThread((current) => (current ? { ...current, conversationId: current.conversationId ?? event.conversationId, items: mergeEntries(current.items, [event.entry]) } : current)),
      onTyping: (event: { userId: string; isTyping: boolean }) =>
        setTyping((current) => {
          const next = new Set(current);
          if (event.isTyping) next.add(event.userId);
          else next.delete(event.userId);
          return next;
        }),
      onRead: () => undefined,
      onReconnected: () => void loadThread().catch(() => undefined),
    };
    const client = realtimeFactory
      ? realtimeFactory(() => sessionRef.current?.token ?? '', handlers)
      : new VisitorRealtime(() => sessionRef.current?.token ?? '', handlers);
    realtime.current = client;
    await client.start();
  }, [loadThread, realtimeFactory]);

  const disconnect = useCallback(async () => {
    const client = realtime.current;
    realtime.current = null;
    await client?.stop();
  }, []);

  useEffect(() => () => void disconnect(), [disconnect]);

  // A returning visitor: load the conversation and listen, so replies show as unread on the launcher.
  useEffect(() => {
    if (!ready || !session || thread) return;
    void loadThread().then(() => connect()).catch(() => undefined);
  }, [ready, session, thread, loadThread, connect]);

  // ---- Host commands ----------------------------------------------------------------------------
  const logout = useCallback(async () => {
    const current = sessionRef.current;
    hostToken.current = null;
    remember(null);
    setThread(null);
    setFiles([]);
    await disconnect();
    if (current) await widgetApi.endSession(current.token).catch(() => undefined);
  }, [disconnect, remember]);

  const identify = useCallback(async (token: string | null) => {
    hostToken.current = token || null;
    const current = sessionRef.current;
    const subject = tokenSubject(token);
    if (!token) return;
    // Keep a session that already belongs to this person; anything else is replaced.
    if (current?.identified && current.subject === subject) return;
    await disconnect();
    setThread(null);
    remember(null);
    // The replaced session (anonymous, or another person's) must not stay usable.
    if (current) await widgetApi.endSession(current.token).catch(() => undefined);
    try {
      await startSession();
    } catch (error) {
      setProblem(errorText(error));
    }
  }, [disconnect, remember, startSession, errorText]);

  useEffect(() => {
    const off = bridge.onCommand((command: HostCommand) => {
      switch (command.type) {
        case 'init':
          if (command.identityToken) void identify(command.identityToken);
          if (command.open) setOpen(true);
          break;
        case 'identify':
          void identify(command.identityToken);
          break;
        case 'logout':
          void logout();
          break;
        case 'open':
          setOpen(true);
          break;
        case 'close':
          setOpen(false);
          break;
      }
    });
    bridge.send({ type: 'ready' });
    return off;
  }, [bridge, identify, logout]);

  // Tell the loader how big to be.
  useEffect(() => {
    bridge.send({ type: 'state', open: open && !!ready, position: ready?.position ?? 'right', hidden: !ready });
  }, [bridge, open, ready]);

  // Opening starts (or resumes) the session and the conversation.
  useEffect(() => {
    if (!open || !ready) return;
    setProblem(null);
    void loadThread()
      .then(() => connect())
      .catch((error: unknown) => setProblem(errorText(error)));
    requestAnimationFrame(() => inputRef.current?.focus());
  }, [open, ready]); // eslint-disable-line react-hooks/exhaustive-deps -- run on open only

  // ---- Unread and read position ------------------------------------------------------------------
  const messages = useMemo(() => thread?.items.filter((e) => e.kind === 'message') ?? [], [thread]);
  const lastSequence = messages.length ? messages[messages.length - 1]!.sequence : 0;
  const unread = useMemo(
    () => (thread ? messages.filter((m) => m.sequence > thread.lastReadSequence && m.actor?.id !== thread.visitorUserId).length : 0),
    [messages, thread],
  );

  useEffect(() => {
    bridge.send({ type: 'unread', count: open ? 0 : unread });
  }, [bridge, open, unread]);

  useEffect(() => {
    if (!open || !thread?.conversationId || lastSequence <= thread.lastReadSequence || document.visibilityState === 'hidden') return;
    const sequence = lastSequence;
    setThread((current) => (current ? { ...current, lastReadSequence: sequence } : current));
    void withSession((token) => widgetApi.read(token, sequence)).catch(() => undefined);
  }, [open, thread?.conversationId, thread?.lastReadSequence, lastSequence, withSession]);

  useEffect(() => {
    const list = listRef.current;
    if (list && open) list.scrollTop = list.scrollHeight;
  }, [messages.length, open, typing.size]);

  // ---- Sending ------------------------------------------------------------------------------------
  const send = async (event?: FormEvent) => {
    event?.preventDefault();
    const body = draft.trim();
    if ((!body && files.length === 0) || busy) return;
    setBusy(true);
    setProblem(null);
    try {
      const clientMessageId = crypto.randomUUID();
      const sent = await withSession((token) => widgetApi.send(token, clientMessageId, body, files.map((f) => f.id)));
      setDraft('');
      setFiles([]);
      void realtime.current?.typing(false);
      setThread((current) => (current ? { ...current, items: mergeEntries(current.items, [sent]) } : current));
      if (!thread?.conversationId) {
        await loadThread();
        await connect();
        await realtime.current?.join();
      }
    } catch (error) {
      setProblem(errorText(error));
    } finally {
      setBusy(false);
      inputRef.current?.focus();
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void send();
    }
  };

  const onDraft = (value: string) => {
    setDraft(value);
    const now = Date.now();
    if (value && now - lastTyping.current > 3000 && thread?.conversationId) {
      lastTyping.current = now;
      void realtime.current?.typing(true);
    }
  };

  const attach = async (list: FileList | null) => {
    const file = list?.[0];
    if (!file || !ready) return;
    if (file.size > ready.maxFileSize) {
      setProblem(t('embed.errors.attachment_too_large'));
      return;
    }
    setBusy(true);
    setProblem(null);
    try {
      const uploaded = await withSession((token) => widgetApi.upload(token, file));
      setFiles((current) => [...current, uploaded]);
      if (!thread?.conversationId) await loadThread();
    } catch (error) {
      setProblem(errorText(error));
    } finally {
      setBusy(false);
    }
  };

  // ---- Rendering -------------------------------------------------------------------------------------
  if (!ready) return null;

  const style = { '--chatx-accent': ready.accentColor, '--chatx-on-accent': textOn(ready.accentColor) } as CSSProperties;
  const canAttach = !!session?.identified && ready.maxFileSize > 0;
  const typingNames = thread?.responders.filter((r) => typing.has(r.id)).map((r) => r.displayName) ?? [];

  if (!open) {
    return (
      <div className={`chatx-embed-root chatx-launcher-only chatx-${ready.position}`} style={style}>
        <button ref={launcherRef} type="button" className="chatx-launcher" onClick={() => setOpen(true)}
          aria-label={unread ? t('embed.openWithUnread', { count: unread }) : t('embed.open')}>
          <ChatGlyph />
          {unread > 0 && <span className="chatx-badge" aria-hidden="true">{unread > 9 ? '9+' : unread}</span>}
        </button>
      </div>
    );
  }

  return (
    <div className={`chatx-embed-root chatx-${ready.position}`} style={style}>
      <section className="chatx-panel" aria-label={ready.title} onKeyDown={(e) => e.key === 'Escape' && setOpen(false)}>
        <header className="chatx-header">
          <div className="chatx-header-text">
            <h1 className="chatx-title">{ready.title}</h1>
            <p className="chatx-subtitle">
              {thread?.responders.length ? t('embed.answeredBy', { names: thread.responders.map((r) => r.displayName).join(', ') }) : t('embed.subtitle')}
            </p>
          </div>
          <div className="chatx-avatars" aria-hidden="true">
            {thread?.responders.slice(0, 3).map((r) => <span key={r.id} className="chatx-avatar">{r.avatar.initials}</span>)}
          </div>
          <button type="button" className="chatx-icon-button" onClick={() => setOpen(false)} aria-label={t('embed.close')}>
            <CloseGlyph />
          </button>
        </header>

        <div className="chatx-messages" ref={listRef} role="log" aria-live="polite" aria-label={t('embed.messages')}>
          {ready.welcomeMessage && (
            <div className="chatx-row chatx-theirs">
              <div className="chatx-bubble chatx-welcome">{ready.welcomeMessage}</div>
            </div>
          )}
          {thread?.hasMore && thread.nextCursor && (
            <button type="button" className="chatx-older" onClick={() => void withSession((token) => widgetApi.conversation(token, thread.nextCursor!)).then((page) =>
              setThread((current) => (current ? { ...current, items: mergeEntries(current.items, page.items), nextCursor: page.nextCursor, hasMore: page.hasMore } : current)))}>
              {t('embed.older')}
            </button>
          )}
          {messages.map((m) => {
            const mine = m.actor?.id === thread?.visitorUserId;
            return (
              <div key={m.id} className={`chatx-row ${mine ? 'chatx-mine' : 'chatx-theirs'}`}>
                {!mine && <span className="chatx-author">{m.actor?.displayName}</span>}
                <div className="chatx-bubble">
                  {m.deletedAtUtc ? <em className="chatx-deleted">{t('embed.deleted')}</em> : m.body ? <RichText className="chatx-text" text={m.body} /> : null}
                  {m.attachments?.map((a) => <AttachmentChip key={a.id} attachment={a} token={session?.token ?? ''} />)}
                </div>
                <time className="chatx-time" dateTime={m.createdAtUtc}>
                  {new Date(m.createdAtUtc).toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' })}
                </time>
              </div>
            );
          })}
          {typingNames.length > 0 && <p className="chatx-typing">{t('embed.typing', { names: typingNames.join(', ') })}</p>}
        </div>

        {problem && <p className="chatx-problem" role="alert">{problem}</p>}

        <form className="chatx-composer" onSubmit={(e) => void send(e)}>
          {files.length > 0 && (
            <ul className="chatx-files" aria-label={t('embed.attached')}>
              {files.map((f) => (
                <li key={f.id}>
                  <span>{f.fileName}</span>
                  <button type="button" onClick={() => setFiles((current) => current.filter((x) => x.id !== f.id))} aria-label={t('embed.removeFile', { name: f.fileName })}>×</button>
                </li>
              ))}
            </ul>
          )}
          <div className="chatx-input-row">
            {canAttach && (
              <label className="chatx-icon-button chatx-attach" title={t('embed.attach')}>
                <input type="file" className="chatx-file-input" aria-label={t('embed.attach')} disabled={busy}
                  onChange={(e) => { void attach(e.target.files); e.target.value = ''; }} />
                <ClipGlyph />
              </label>
            )}
            <textarea ref={inputRef} className="chatx-input" rows={1} maxLength={4000} value={draft} placeholder={t('embed.placeholder')}
              aria-label={t('embed.placeholder')} onChange={(e) => onDraft(e.target.value)} onKeyDown={onKeyDown}
              onBlur={() => void realtime.current?.typing(false)} />
            <button type="submit" className="chatx-send" disabled={busy || (!draft.trim() && files.length === 0)} aria-label={t('embed.send')}>
              <SendGlyph />
            </button>
          </div>
          <p className="chatx-notice">{session?.identified ? t('embed.noticeIdentified', { name: session.displayName }) : t('embed.notice')}</p>
        </form>
      </section>
    </div>
  );
}

function AttachmentChip({ attachment, token }: { attachment: MessageAttachmentResponse; token: string }) {
  const { t } = useTranslation();
  const [url, setUrl] = useState<string | null>(null);
  const image = attachment.kind === 'image';

  useEffect(() => {
    if (!image || !token) return;
    let revoked = false;
    let objectUrl: string | null = null;
    widgetApi.download(token, attachment.id).then((blob) => {
      if (revoked) return;
      objectUrl = URL.createObjectURL(blob);
      setUrl(objectUrl);
    }, () => undefined);
    return () => {
      revoked = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [attachment.id, image, token]);

  const save = async () => {
    const blob = await widgetApi.download(token, attachment.id);
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = attachment.fileName;
    link.click();
    setTimeout(() => URL.revokeObjectURL(link.href), 1000);
  };

  return (
    <span className="chatx-attachment">
      {image && url && <img src={url} alt={attachment.fileName} className="chatx-image" />}
      <button type="button" className="chatx-file" onClick={() => void save()} aria-label={t('embed.download', { name: attachment.fileName })}>
        {attachment.fileName}
      </button>
    </span>
  );
}

const ChatGlyph = () => (
  <svg viewBox="0 0 24 24" width="28" height="28" aria-hidden="true" focusable="false">
    <path fill="currentColor" d="M4 4h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H9l-5 4v-4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Zm3 6.5a1.5 1.5 0 1 0 0 .01Zm5 0a1.5 1.5 0 1 0 0 .01Zm5 0a1.5 1.5 0 1 0 0 .01Z" />
  </svg>
);
const CloseGlyph = () => (
  <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" focusable="false">
    <path fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" d="M6 6l12 12M18 6 6 18" />
  </svg>
);
const SendGlyph = () => (
  <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" focusable="false">
    <path fill="currentColor" d="M3.4 20.4 21 12 3.4 3.6 3.3 10l12.4 2-12.4 2z" />
  </svg>
);
const ClipGlyph = () => (
  <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" focusable="false">
    <path fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" d="M20 11.5 12 19.5a5 5 0 0 1-7-7l8.5-8.5a3.3 3.3 0 0 1 4.7 4.7L9.7 17.2a1.7 1.7 0 0 1-2.4-2.4L15 7" />
  </svg>
);
