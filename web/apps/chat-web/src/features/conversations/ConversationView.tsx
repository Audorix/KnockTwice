import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { navigate, useLocation } from '../../app/router';
import type {
  AttachmentLimitsResponse,
  ConversationDetailResponse,
  ConversationEntryResponse,
  MessageAttachmentResponse,
  NotificationLevel,
  PinnedMessageResponse,
} from '../../shared/api/contracts';
import { mentionCandidates, useMentionAutocomplete } from './mentions';
import { ForwardDialog, MessageMenu, PinsPanel, type MessageTools } from './messageTools';
import { useFormatting } from './formatting';
import * as planning from '../planning/planningApi';
import { TimeMenu } from '../planning/TimeMenu';
import { RichText, toPlainText, type RichTextMentions } from '../../shared/richText';

/** Who to highlight in a message: the people it mentions (by their current names), and you. */
export function mentionsOf(entry: ConversationEntryResponse, names: Map<string, string>, me: string): RichTextMentions | undefined {
  const m = entry.mentions;
  if (!m) return undefined;
  return {
    names: m.userIds.map((id) => names.get(id)).filter((n): n is string => Boolean(n)),
    everyone: m.everyone,
    here: m.here,
    ...(names.get(me) ? { self: names.get(me)! } : {}),
  };
}
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { LockGlyph } from '../../shared/components/LockGlyph';
import { NavIcon } from '../../shared/components/NavIcon';
import { ToolButton } from '../../shared/components/ToolButton';
import { formatDateTime } from '../../shared/format';
import { acceptFor, AttachButton, AttachmentList, DraftFileList, formatBytes, useDraftFiles } from '../attachments/Attachments';
import * as files from '../attachments/attachmentsApi';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../auth/errors';
import { useRealtime } from '../realtime/RealtimeContext';
import * as api from './conversationsApi';
import { describeEntry, GroupDetails } from './conversationParts';
import { ReactionBar, ReactionPicker, ThreadPanel, ThreadSummary, upsertEntry, withReactions } from './reactionsAndThreads';
import * as business from '../business/businessApi';
import { ChannelMark } from '../business/ChannelMark';

/** Matches the server default (Messaging:EditWindow); the server enforces the real value. */
const editWindowMs = 15 * 60 * 1000;
const maxLength = 4000;
const typingRenewMs = 3000;

interface PendingMessage {
  clientMessageId: string;
  body: string;
  replyTo: ConversationEntryResponse | null;
  attachmentIds: string[];
  attachmentNames: string[];
  mentionUserIds: string[];
  failed: boolean;
}

/** Keeps the newest version of each entry, in sequence order (events arrive at least once, possibly out of order). */
const upsert = upsertEntry;

/** Thread replies not echoed to the conversation live only in the thread panel. */
const inMainTimeline = (entry: ConversationEntryResponse) => !entry.threadRootId || entry.shownInConversation === true;

const attachmentLink = (item: Pick<MessageAttachmentResponse, 'id'>, download: boolean) => files.attachmentUrl(item.id, download);

const formatLimit = (bytes: number, language: string) => formatBytes(bytes, language);

function newClientId(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

export function ConversationView({ conversationId, onChanged }: { conversationId: string; onChanged: () => void }) {
  const { t, i18n } = useTranslation();
  const { user } = useAuth();
  const { client } = useRealtime();
  const [detail, setDetail] = useState<ConversationDetailResponse | null>(null);
  const [entries, setEntries] = useState<ConversationEntryResponse[]>([]);
  const [pending, setPending] = useState<PendingMessage[]>([]);
  const [olderCursor, setOlderCursor] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [showManage, setShowManage] = useState(false);
  const [draft, setDraft] = useState('');
  const [replyTo, setReplyTo] = useState<ConversationEntryResponse | null>(null);
  const [editing, setEditing] = useState<ConversationEntryResponse | null>(null);
  const [typing, setTyping] = useState<Record<string, number>>({});
  const [limits, setLimits] = useState<AttachmentLimitsResponse | null>(null);
  const [fileNotice, setFileNotice] = useState<string | null>(null);
  const [threadRoot, setThreadRoot] = useState<string | null>(null);
  const [pins, setPins] = useState<PinnedMessageResponse[]>([]);
  const [showPins, setShowPins] = useState(false);
  const [forwarding, setForwarding] = useState<ConversationEntryResponse | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [highlight, setHighlight] = useState<string | null>(null);
  const { query } = useLocation();
  const jumpTo = useRef<{ message: string; tries: number } | null>(null);
  const targetMessage = query.get('message');
  const targetThread = query.get('thread');
  const threadRootRef = useRef<string | null>(null);
  threadRootRef.current = threadRoot;
  /** Thread replies already counted on their first message, so a reply seen twice counts once. */
  const countedReplies = useRef(new Set<string>());
  const upload = useCallback((file: File) => files.uploadAttachment(conversationId, file), [conversationId]);
  const drafts = useDraftFiles(upload, files.deleteAttachment, files.getAttachment);
  const readUpTo = useRef(0);
  const lastTypingSent = useRef(0);
  const logEnd = useRef<HTMLDivElement>(null);
  const timeline = useRef<HTMLDivElement>(null);
  const topSentinel = useRef<HTMLDivElement>(null);
  const atBottom = useRef(true);
  const restoreScroll = useRef<{ height: number; top: number } | null>(null);
  const [showJump, setShowJump] = useState(false);
  const me = user?.id ?? '';

  const markRead = useCallback(
    async (sequence: number) => {
      if (sequence <= readUpTo.current) return;
      readUpTo.current = sequence;
      try {
        await api.markRead(conversationId, sequence);
        onChanged();
      } catch {
        // Not critical: the next view or message retries.
      }
    },
    [conversationId, onChanged],
  );

  /** Keeps a thread's summary on its first message current as replies arrive or are deleted. */
  const countReply = useCallback(
    (change: 'added' | 'removed' | 'none', reply: ConversationEntryResponse) => {
      if (change === 'none' || !reply.threadRootId) return;
      const key = `${change}:${reply.id}`;
      if (countedReplies.current.has(key)) return;
      countedReplies.current.add(key);
      const rootId = reply.threadRootId;
      setEntries((current) =>
        current.map((e) => {
          if (e.id !== rootId) return e;
          const thread = e.thread ?? { replyCount: 0, lastReplyAtUtc: null, following: null, unread: null };
          if (change === 'removed') return { ...e, thread: { ...thread, replyCount: Math.max(0, thread.replyCount - 1) } };
          const unseen = reply.actor?.id !== me && threadRootRef.current !== rootId;
          return {
            ...e,
            thread: { ...thread, replyCount: thread.replyCount + 1, lastReplyAtUtc: reply.createdAtUtc, unread: (thread.unread ?? 0) + (unseen ? 1 : 0) },
          };
        }),
      );
    },
    [me],
  );

  const threadRead = useCallback(() => {
    const rootId = threadRootRef.current;
    setEntries((current) => current.map((e) => (e.id === rootId && e.thread ? { ...e, thread: { ...e.thread, unread: 0 } } : e)));
  }, []);

  const toggleReaction = useCallback(
    async (messageId: string, emoji: string, reacted: boolean) => {
      const reactions = await api.setReaction(conversationId, messageId, emoji, reacted);
      setEntries((current) => withReactions(current, messageId, reactions));
    },
    [conversationId],
  );

  const load = useCallback(async () => {
    try {
      const [d, history] = await Promise.all([api.getConversation(conversationId), api.getHistory(conversationId)]);
      setDetail(d);
      setEntries((current) => history.items.reduce(upsert, current.filter((e) => e.sequence < (history.items[0]?.sequence ?? Infinity))));
      setOlderCursor(history.hasMore ? history.nextCursor : null);
      if (d.conversation.type !== 'website') api.listPins(conversationId).then(setPins, () => undefined);
      if (d.conversation.lastSequence > d.conversation.lastReadSequence) await markRead(d.conversation.lastSequence);
    } catch (err) {
      setError(err);
    }
  }, [conversationId, markRead]);

  // Phase 22: a cursor taken after loading; after a reconnect only what changed is fetched.
  const changeCursor = useRef<{ cursor: string; at: number } | null>(null);
  useEffect(() => {
    if (!detail || changeCursor.current) return;
    api.getChangeCursor().then((cursor) => {
      changeCursor.current ??= { cursor, at: Date.now() };
    }, () => undefined);
  }, [detail]);

  const catchUp = useCallback(async () => {
    const saved = changeCursor.current;
    // Changes are kept 24 hours on the server; older cursors (or none) reload.
    if (!saved || Date.now() - saved.at > 24 * 60 * 60 * 1000) {
      changeCursor.current = null;
      await load();
      return;
    }
    try {
      const set = await api.getChanges(saved.cursor);
      changeCursor.current = { cursor: set.cursor, at: Date.now() };
      if (set.reset) {
        await load();
        return;
      }
      const mine = set.entries.filter((e) => e.conversationId === conversationId).map((e) => e.entry);
      if (mine.length > 0) {
        // Thread replies kept in their thread stay out; their first message arrives with its new count.
        setEntries((current) => mine.filter(inMainTimeline).reduce(upsert, current));
      }
      if (set.conversationIds.includes(conversationId)) {
        const d = await api.getConversation(conversationId);
        setDetail(d);
        if (d.conversation.lastSequence > d.conversation.lastReadSequence && document.visibilityState === 'visible') {
          await markRead(d.conversation.lastSequence);
        }
      }
    } catch {
      await load();
    }
  }, [conversationId, load, markRead]);

  // Phase 18: open at a message (from mentions, search, saved messages or pins).
  useEffect(() => {
    if (targetThread) setThreadRoot(targetThread);
    jumpTo.current = targetMessage && !targetThread ? { message: targetMessage, tries: 0 } : null;
  }, [targetMessage, targetThread]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    files.getLimits().then(setLimits, () => setLimits(null));
  }, []);

  // Live updates: messages, receipts, typing and conversation changes for this conversation.
  useEffect(() => {
    if (!client) return;
    void client.joinConversation(conversationId).catch(() => undefined);
    const offMessage = client.onMessage((type, event) => {
      if (event.conversationId !== conversationId) return;
      const entry = event.entry;
      if (entry.threadRootId) countReply(type === 'MessageDeleted' ? 'removed' : type === 'MessageCreated' ? 'added' : 'none', entry);
      if (!inMainTimeline(entry)) return;
      setEntries((current) => upsert(current, entry));
      if (entry.actor?.id === me && entry.clientMessageId) {
        setPending((current) => current.filter((p) => p.clientMessageId !== entry.clientMessageId));
      }
      if (entry.actor?.id !== me && document.visibilityState === 'visible') void markRead(entry.sequence);
    });
    const offReaction = client.onReaction((event) => {
      if (event.conversationId === conversationId) setEntries((current) => withReactions(current, event.messageId, event.reactions));
    });
    const offReceipt = client.onReceipt((type, event) => {
      if (event.conversationId !== conversationId) return;
      setDetail((current) =>
        current && {
          ...current,
          participants: current.participants.map((p) =>
            p.user.id !== event.userId
              ? p
              : type === 'MessageRead'
                ? { ...p, lastReadSequence: Math.max(p.lastReadSequence, event.sequence), lastDeliveredSequence: Math.max(p.lastDeliveredSequence, event.sequence) }
                : { ...p, lastDeliveredSequence: Math.max(p.lastDeliveredSequence, event.sequence) },
          ),
        },
      );
    });
    const offTyping = client.onTyping((event) => {
      if (event.conversationId !== conversationId || event.userId === me) return;
      setTyping((current) => {
        const next = { ...current };
        if (event.isTyping && event.expiresAtUtc) next[event.userId] = Date.parse(event.expiresAtUtc);
        else delete next[event.userId];
        return next;
      });
    });
    const offUpdate = client.onConversationUpdated((event) => {
      if (event.conversationId === conversationId && event.change !== 'removed' && event.change !== 'read') void load();
    });
    const offReconnect = client.onReconnected(() => void catchUp());
    return () => {
      offMessage();
      offReaction();
      offReceipt();
      offTyping();
      offUpdate();
      offReconnect();
      void client.sendTyping(conversationId, false).catch(() => undefined);
      void client.leaveConversation(conversationId).catch(() => undefined);
    };
  }, [client, conversationId, load, markRead, me, countReply, catchUp]);

  // Typing indicators expire on their own; drop stale ones locally too.
  useEffect(() => {
    if (Object.keys(typing).length === 0) return;
    const timer = setInterval(() => {
      const now = Date.now();
      setTyping((current) => Object.fromEntries(Object.entries(current).filter(([, expires]) => expires > now)));
    }, 1000);
    return () => clearInterval(timer);
  }, [typing]);

  // Follow new messages only when the reader is already at the end (or sent them); otherwise offer a jump.
  const newest = entries.at(-1);
  useEffect(() => {
    if (restoreScroll.current) return;
    const mineLast = newest?.actor?.id === me || pending.length > 0;
    if (atBottom.current || mineLast) {
      logEnd.current?.scrollIntoView?.({ block: 'end' });
      setShowJump(false);
    } else {
      setShowJump(true);
    }
  }, [newest?.id, newest?.actor?.id, pending.length, me]);

  // After older entries are prepended, keep the reader where they were.
  useLayoutEffect(() => {
    const box = timeline.current;
    const saved = restoreScroll.current;
    if (!box || !saved) return;
    box.scrollTop = box.scrollHeight - saved.height + saved.top;
    restoreScroll.current = null;
  }, [entries]);

  const names = useMemo(() => new Map(detail?.participants.map((p) => [p.user.id, p.user.displayName]) ?? []), [detail]);
  const candidates = useMemo(() => mentionCandidates(detail, me), [detail, me]);
  const composer = useRef<HTMLTextAreaElement>(null);
  const mention = useMentionAutocomplete(candidates, draft, setDraft, composer);
  const formatting = useFormatting(composer, draft, setDraft);

  // Phase 20: the draft (text and reply target) is kept on the server, so every device shows it.
  const draftReady = useRef(false);
  const lastSavedDraft = useRef('|');
  const entriesRef = useRef(entries);
  entriesRef.current = entries;
  const historyLoaded = detail !== null;
  useEffect(() => {
    if (!historyLoaded || draftReady.current) return;
    let current = true;
    planning.listDrafts().then(
      (all) => {
        if (!current) return;
        const saved = all.find((d) => d.conversationId === conversationId && !d.threadRootId);
        if (saved) {
          lastSavedDraft.current = `${saved.body}|${saved.replyToId ?? ''}`;
          setDraft((typed) => typed || saved.body);
          const target = saved.replyToId ? entriesRef.current.find((e) => e.id === saved.replyToId) : undefined;
          if (target) setReplyTo((chosen) => chosen ?? target);
        }
        draftReady.current = true;
      },
      () => {
        draftReady.current = true;
      },
    );
    return () => {
      current = false;
    };
  }, [historyLoaded, conversationId]);

  useEffect(() => {
    if (!draftReady.current || editing) return;
    const key = `${draft}|${replyTo?.id ?? ''}`;
    if (key === lastSavedDraft.current) return;
    const timer = setTimeout(() => {
      lastSavedDraft.current = key;
      void planning.saveDraft(conversationId, { body: draft, replyToId: replyTo?.id ?? null }).catch(() => undefined);
    }, 800);
    return () => clearTimeout(timer);
  }, [draft, replyTo?.id, editing, conversationId]);

  const loadingOlder = useRef(false);
  const loadOlder = useCallback(async () => {
    if (!olderCursor || loadingOlder.current) return;
    loadingOlder.current = true;
    try {
      const page = await api.getHistory(conversationId, olderCursor);
      const box = timeline.current;
      if (box) restoreScroll.current = { height: box.scrollHeight, top: box.scrollTop };
      setEntries((current) => page.items.reduce(upsert, current));
      setOlderCursor(page.hasMore ? page.nextCursor : null);
    } catch (err) {
      setError(err);
    } finally {
      loadingOlder.current = false;
    }
  }, [conversationId, olderCursor]);

  // Older pages load on their own when the reader scrolls near the top.
  useEffect(() => {
    const target = topSentinel.current;
    if (!target || !olderCursor || typeof IntersectionObserver === 'undefined') return;
    const observer = new IntersectionObserver((items) => {
      if (items.some((i) => i.isIntersecting)) void loadOlder();
    }, { root: timeline.current, rootMargin: '200px 0px 0px 0px' });
    observer.observe(target);
    return () => observer.disconnect();
  }, [olderCursor, loadOlder]);

  // Loads older pages until the target message is there (at most 30 pages), then shows it highlighted.
  useEffect(() => {
    const jump = jumpTo.current;
    if (!jump || entries.length === 0) return;
    if (entries.some((e) => e.id === jump.message)) {
      jumpTo.current = null;
      setHighlight(jump.message);
      requestAnimationFrame(() => document.getElementById(`msg-${jump.message}`)?.scrollIntoView?.({ block: 'center' }));
      const timer = setTimeout(() => setHighlight(null), 4000);
      return () => clearTimeout(timer);
    }
    if (olderCursor && jump.tries < 30) {
      jump.tries++;
      void loadOlder();
    } else if (!olderCursor) {
      jumpTo.current = null;
      setNotice(t('tools.messageGone'));
    }
    return undefined;
  }, [entries, olderCursor, loadOlder, t]);

  const showMessage = (entry: ConversationEntryResponse) => {
    setShowPins(false);
    jumpTo.current = { message: entry.id, tries: 0 };
    setEntries((current) => [...current]); // re-run the jump effect
  };

  const onTimelineScroll = () => {
    const box = timeline.current;
    if (!box) return;
    atBottom.current = box.scrollHeight - box.scrollTop - box.clientHeight < 80;
    if (atBottom.current) setShowJump(false);
  };

  const act = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      await action();
      await load();
      onChanged();
    } catch (err) {
      setError(err);
    }
  };

  const deliver = async (message: PendingMessage) => {
    setPending((current) => current.map((p) => (p.clientMessageId === message.clientMessageId ? { ...p, failed: false } : p)));
    try {
      // The same clientMessageId on retry: the server never stores the message twice.
      const saved = await api.sendMessage(conversationId, {
        clientMessageId: message.clientMessageId,
        body: message.body,
        replyToId: message.replyTo?.id ?? null,
        attachmentIds: message.attachmentIds,
        mentionUserIds: message.mentionUserIds,
      });
      setEntries((current) => upsert(current, saved));
      setPending((current) => current.filter((p) => p.clientMessageId !== message.clientMessageId));
      readUpTo.current = Math.max(readUpTo.current, saved.sequence);
      onChanged();
    } catch (err) {
      setError(err);
      setPending((current) => current.map((p) => (p.clientMessageId === message.clientMessageId ? { ...p, failed: true } : p)));
    }
  };

  const submit = async (event?: FormEvent) => {
    event?.preventDefault();
    const body = draft.trim();
    if (editing ? !body : (!body && drafts.readyIds.length === 0) || drafts.busy) return;
    setError(null);
    if (editing) {
      try {
        const saved = await api.editMessage(conversationId, editing.id, body);
        setEntries((current) => upsert(current, saved));
        setEditing(null);
        setDraft('');
      } catch (err) {
        setError(err);
      }
      return;
    }

    const ready = drafts.files.filter((f) => f.status === 'ready' && f.id);
    const message: PendingMessage = {
      clientMessageId: newClientId(),
      body,
      replyTo,
      attachmentIds: ready.map((f) => f.id!),
      attachmentNames: ready.map((f) => f.file.name),
      mentionUserIds: mention.mentionIds(body),
      failed: false,
    };
    setPending((current) => [...current, message]);
    setDraft('');
    mention.reset();
    setReplyTo(null);
    drafts.clear();
    lastTypingSent.current = 0;
    void client?.sendTyping(conversationId, false).catch(() => undefined);
    await deliver(message);
  };

  /** Phase 20: sends the composed message at a later time instead of now. */
  const sendLater = async (at: Date) => {
    const body = draft.trim();
    if ((!body && drafts.readyIds.length === 0) || drafts.busy) return;
    setError(null);
    try {
      await planning.scheduleMessage(conversationId, {
        body,
        sendAtUtc: at.toISOString(),
        replyToId: replyTo?.id ?? null,
        attachmentIds: drafts.readyIds,
        mentionUserIds: mention.mentionIds(body),
      });
      setDraft('');
      mention.reset();
      setReplyTo(null);
      drafts.clear();
      setNotice(t('planning.scheduledFor', { time: at.toLocaleString(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }) }));
    } catch (err) {
      setError(err);
    }
  };

  const onDraftChange = (value: string) => {
    setDraft(value);
    if (!client || editing) return;
    const now = Date.now();
    if (value.trim() && now - lastTypingSent.current > typingRenewMs) {
      lastTypingSent.current = now;
      void client.sendTyping(conversationId, true).catch(() => undefined);
    } else if (!value.trim() && lastTypingSent.current) {
      lastTypingSent.current = 0;
      void client.sendTyping(conversationId, false).catch(() => undefined);
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (mention.onKeyDown(event)) return;
    if (formatting.onKeyDown(event)) return;
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void submit();
    } else if (event.key === 'Escape') {
      setEditing(null);
      setReplyTo(null);
    }
  };

  if (!detail) {
    return (
      <section className="cx-card">
        {error !== null ? <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert> : <p className="cx-muted mb-0">{t('auth.working')}</p>}
      </section>
    );
  }

  const c = detail.conversation;
  const isGroup = c.type === 'group';
  const isWebsite = c.type === 'website';
  const isChannel = c.type === 'channel';
  const canModerate = (isGroup || c.type === 'channel') && (c.myRole === 'owner' || c.myRole === 'administrator');
  // Phase 16: reactions and threads everywhere except website chats.
  const canReact = !isWebsite;
  // Phase 18 (owner decision): either person pins in direct conversations, managers in groups and channels.
  const canPin = !isWebsite && (c.type === 'direct' || canModerate);
  const toolsFor = (entry: ConversationEntryResponse) => ({
    canPin,
    canForward: !isWebsite,
    onTogglePin: () => void act(() => api.setPinned(c.id, entry.id, !entry.pinned)),
    onToggleSaved: () =>
      void api.setSaved(c.id, entry.id, !entry.saved).then(
        () => {
          setEntries((current) => current.map((e) => (e.id === entry.id ? { ...e, saved: !entry.saved } : e)));
          setNotice(t(entry.saved ? 'tools.unsaved' : 'tools.saved'));
        },
        setError,
      ),
    onForward: () => setForwarding(entry),
    onRemind: (at: Date) =>
      void planning.setReminder(c.id, entry.id, at.toISOString()).then(
        () => setNotice(t('planning.reminderSet', { time: at.toLocaleString(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }) })),
        setError,
      ),
  });
  const others = detail.participants.filter((p) => p.user.id !== me);
  const typingNames = Object.keys(typing).map((id) => names.get(id)).filter(Boolean) as string[];

  const receipt = (entry: ConversationEntryResponse): string => {
    if (others.length === 0) return t('messages.sent');
    if (!isGroup && others.length === 1) {
      const other = others[0]!;
      if (other.lastReadSequence >= entry.sequence) return t('messages.read');
      if (other.lastDeliveredSequence >= entry.sequence) return t('messages.delivered');
      return t('messages.sent');
    }
    const read = others.filter((p) => p.lastReadSequence >= entry.sequence).length;
    if (read > 0) return t('messages.readBy', { count: read, total: others.length });
    const delivered = others.filter((p) => p.lastDeliveredSequence >= entry.sequence).length;
    return delivered > 0 ? t('messages.deliveredTo', { count: delivered, total: others.length }) : t('messages.sent');
  };

  return (
    <section className="cx-card cx-conversation-view" aria-labelledby="conversation-title">
      <div className="cx-card-header">
        <button type="button" className="btn btn-sm cx-back" onClick={() => navigate('/conversations')}>
          <NavIcon name="back" size={18} />
          <span className="visually-hidden">{t('conversations.backToList')}</span>
        </button>
        <div className="min-w-0 flex-grow-1">
          <h2 id="conversation-title" className="cx-card-title text-truncate">
            {isChannel && <ChannelMark visibility={c.visibility} />} {c.title || t('conversations.unknownPerson')}
          </h2>
          <p className="cx-meta mb-0">
            {isWebsite && <span className="badge cx-website-badge me-1">{t('conversations.types.website')}</span>}
            {isWebsite && c.supportStatus && (
              <span className={`badge me-1 ${c.supportStatus === 'open' ? 'text-bg-success' : 'text-bg-light border'}`}>{t(`support.statuses.${c.supportStatus}`)}</span>
            )}
            {isChannel && c.myRole === 'guest' && <span className="badge text-bg-warning me-1">{t('channels.roles.guest')}</span>}
            {c.scope === 'organization' ? c.organizationName : t('conversations.personal')} ·{' '}
            {isGroup || isWebsite || isChannel ? t('conversations.participants', { count: detail.participants.length }) : t('conversations.types.direct')}
          </p>
          {isWebsite && <p className="cx-meta mb-0">{t('conversations.websiteHint')}</p>}
          {isChannel && c.myRole === 'guest' && <p className="cx-meta mb-0">{t('channels.guestNotice')}</p>}
        </div>
        <div className="d-flex gap-2 flex-wrap justify-content-end">
          {isChannel && (
            <button type="button" className="btn btn-sm btn-outline-primary" onClick={() => navigate(`/channel?id=${encodeURIComponent(c.id)}`)}>
              {t('conversations.details')}
            </button>
          )}
          {isWebsite && (
            <>
              <button type="button" className="btn btn-sm btn-outline-primary" onClick={() => act(() => business.claimSupportConversation(c.id))}>
                {t('support.claim')}
              </button>
              <button type="button" className="btn btn-sm btn-outline-secondary"
                onClick={() => act(() => business.setSupportStatus(c.id, c.supportStatus === 'closed' ? 'open' : 'closed'))}>
                {c.supportStatus === 'closed' ? t('support.reopen') : t('support.close')}
              </button>
            </>
          )}
          {!isGroup && c.counterpart && !c.counterpart.deleted && (
            <button
              type="button"
              className="btn btn-sm cx-private-start"
              onClick={() => navigate(`/private?with=${encodeURIComponent(c.counterpart!.id)}&name=${encodeURIComponent(c.counterpart!.displayName)}`)}
              title={t('private.startHint')}
            >
              <LockGlyph /> {t('private.start')}
            </button>
          )}
          <label className="visually-hidden" htmlFor={`level-${c.id}`}>
            {t('conversations.levels.label')}
          </label>
          <select
            id={`level-${c.id}`}
            className="form-select form-select-sm cx-level-select"
            title={t('conversations.levels.label')}
            value={c.notificationLevel ?? (c.muted ? 'mentions' : 'all')}
            onChange={(e) => void act(() => api.setNotificationLevel(c.id, e.target.value as NotificationLevel))}
          >
            {(['all', 'mentions', 'nothing'] as const).map((level) => (
              <option key={level} value={level}>
                {t(`conversations.levels.${level}`)}
              </option>
            ))}
          </select>
          {isGroup && (
            <button type="button" className="btn btn-sm btn-outline-primary" aria-expanded={showManage} onClick={() => setShowManage((s) => !s)}>
              {t('conversations.details')}
            </button>
          )}
          {pins.length > 0 && (
            <button type="button" className="btn btn-sm btn-outline-secondary" aria-expanded={showPins} onClick={() => setShowPins((s) => !s)}>
              {t('tools.pinnedButton', { count: pins.length })}
            </button>
          )}
        </div>
      </div>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {fileNotice && <FormAlert tone="info">{fileNotice}</FormAlert>}
      {notice && (
        <div role="status" className="cx-notice">
          <span>{notice}</span>
          <button type="button" className="btn-close btn-sm" aria-label={t('notifications.dismiss')} onClick={() => setNotice(null)} />
        </div>
      )}
      {showPins && pins.length > 0 && (
        <PinsPanel
          pins={pins}
          language={i18n.language}
          canUnpin={canPin}
          onJump={showMessage}
          onUnpin={(entry) => void act(() => api.setPinned(c.id, entry.id, false))}
          onClose={() => setShowPins(false)}
        />
      )}
      {detail.description && <p className="cx-muted">{detail.description}</p>}
      {showManage && isGroup && <GroupDetails detail={detail} canManage={canModerate} currentUserId={me} act={act} />}

      <div className="cx-timeline" ref={timeline} onScroll={onTimelineScroll} role="log" aria-label={t('conversations.history')} aria-live="polite">
        {olderCursor && (
          <div ref={topSentinel} className="text-center">
            <button type="button" className="btn btn-sm btn-link" onClick={() => void loadOlder()}>
              {t('conversations.loadOlder')}
            </button>
          </div>
        )}
        <ol className="list-unstyled mb-0">
          {entries.map((entry) =>
            entry.kind === 'message' ? (
              <MessageItem
                key={entry.id}
                entry={entry}
                mine={entry.actor?.id === me}
                showAuthor={isGroup || isWebsite || isChannel}
                receipt={entry.actor?.id === me && !entry.deletedAtUtc ? receipt(entry) : null}
                canEdit={entry.actor?.id === me && !entry.deletedAtUtc && Date.now() - Date.parse(entry.createdAtUtc) < editWindowMs}
                canDelete={!entry.deletedAtUtc && (entry.actor?.id === me || canModerate)}
                language={i18n.language}
                me={me}
                names={names}
                canReact={canReact}
                onReact={(emoji, reacted) => void toggleReaction(entry.id, emoji, reacted).catch(setError)}
                onOpenThread={canReact ? () => setThreadRoot(entry.threadRootId ?? entry.id) : null}
                tools={toolsFor(entry)}
                highlighted={highlight === entry.id}
                onReply={() => {
                  setEditing(null);
                  setReplyTo(entry);
                }}
                onEdit={() => {
                  setReplyTo(null);
                  setEditing(entry);
                  setDraft(entry.body ?? '');
                }}
                onDelete={() => {
                  if (window.confirm(t('messages.deleteConfirm'))) void act(() => api.deleteMessage(c.id, entry.id));
                }}
              />
            ) : (
              <li key={entry.id} className="cx-history-entry">
                {entry.kind === 'pinned' && entry.replyTo && !entry.replyTo.deleted ? (
                  <button type="button" className="btn btn-link btn-sm p-0 cx-history-link" onClick={() => showMessage({ ...entry, id: entry.replyTo!.id })}>
                    {describeEntry(entry, c.type, t)}
                  </button>
                ) : (
                  <span>{describeEntry(entry, c.type, t)}</span>
                )}
                <time className="cx-meta" dateTime={entry.createdAtUtc}>
                  {formatDateTime(entry.createdAtUtc, i18n.language)}
                </time>
              </li>
            ),
          )}
          {pending.map((p) => (
            <li key={p.clientMessageId} className="cx-message is-mine is-pending">
              <div className="cx-bubble">
                {p.body && <RichText className="cx-message-body" text={p.body} />}
                {p.attachmentNames.length > 0 && <p className="cx-meta mb-1">{t('attachments.count', { count: p.attachmentNames.length })}</p>}
                <div className="cx-message-meta">
                  {p.failed ? (
                    <>
                      <span className="text-danger">{t('messages.failed')}</span>
                      <button type="button" className="btn btn-sm btn-link p-0" onClick={() => void deliver(p)}>
                        {t('messages.retry')}
                      </button>
                    </>
                  ) : (
                    <span>{t('messages.sending')}</span>
                  )}
                </div>
              </div>
            </li>
          ))}
        </ol>
        <div ref={logEnd} />
        {showJump && (
          <button
            type="button"
            className="btn btn-sm btn-primary cx-jump"
            onClick={() => {
              logEnd.current?.scrollIntoView?.({ block: 'end', behavior: 'smooth' });
              setShowJump(false);
            }}
          >
            {t('messages.jumpToLatest')}
          </button>
        )}
      </div>

      <p className="cx-typing" aria-live="polite">
        {typingNames.length === 1
          ? t('messages.typingOne', { name: typingNames[0] })
          : typingNames.length > 1
            ? t('messages.typingMany', { count: typingNames.length })
            : ' '}
      </p>

      {c.type === 'direct' && c.counterpart?.deleted ? (
        <p className="cx-composer-closed cx-muted" role="status">
          {t('conversations.counterpartDeleted')}
        </p>
      ) : (
      <form className="cx-composer" onSubmit={(e) => void submit(e)}>
        {(replyTo || editing) && (
          <div className="cx-composer-context">
            <span className="text-truncate">
              {editing
                ? t('messages.editing')
                : t('messages.replyingTo', { name: replyTo?.actor?.displayName ?? t('conversations.someone') })}
              {replyTo?.body && <span className="cx-meta"> — {toPlainText(replyTo.body).slice(0, 80)}</span>}
            </span>
            <button
              type="button"
              className="btn btn-sm btn-link"
              onClick={() => {
                setReplyTo(null);
                setEditing(null);
                setDraft('');
              }}
            >
              {t('conversations.cancel')}
            </button>
          </div>
        )}
        <DraftFileList files={drafts.files} onRemove={drafts.remove} />
        {formatting.toolbar}
        <label htmlFor="composer" className="visually-hidden">
          {t('messages.compose')}
        </label>
        <div className="cx-composer-row">
          {!editing && (
            <AttachButton
              id={`attach-${conversationId}`}
              accept={acceptFor(limits?.allowedExtensions)}
              onFiles={(chosen) => {
                setFileNotice(null);
                drafts.add(chosen, limits, setFileNotice, {
                  tooLarge: t('attachments.tooLarge', { size: limits ? formatLimit(limits.maxFileSize, i18n.language) : '' }),
                  type: t('attachments.typeNotAllowed'),
                  tooMany: t('attachments.tooMany', { count: limits?.maxPerMessage ?? 0 }),
                });
              }}
            />
          )}
          <div className="cx-mention-anchor">
            {mention.menu}
            <textarea
              id="composer"
              ref={composer}
              className="form-control"
              rows={2}
              maxLength={maxLength}
              placeholder={t('messages.placeholder')}
              value={draft}
              {...(editing ? {} : mention.inputProps)}
              onChange={(e) => {
                onDraftChange(e.target.value);
                if (!editing) mention.onInput(e.target.value, e.target.selectionStart);
              }}
              onKeyDown={onKeyDown}
            />
          </div>
          <button
            type="submit"
            className={`btn btn-primary${editing ? '' : ' cx-send'}`}
            title={editing ? undefined : t('messages.send')}
            disabled={editing ? !draft.trim() : drafts.busy || (!draft.trim() && drafts.readyIds.length === 0)}
          >
            {editing ? (
              t('messages.save')
            ) : (
              <>
                <NavIcon name="send" size={18} />
                <span className="visually-hidden">{t('messages.send')}</span>
              </>
            )}
          </button>
        </div>
        <div className="d-flex justify-content-between align-items-start gap-2">
          <div className="form-text">{t('messages.hint')}</div>
          {!editing && (
            <TimeMenu
              label={t('planning.sendLater')}
              buttonClassName="btn btn-sm btn-link p-0 mt-1 text-nowrap"
              disabled={drafts.busy || (!draft.trim() && drafts.readyIds.length === 0)}
              onPick={(at) => void sendLater(at)}
            />
          )}
        </div>
      </form>
      )}
      {threadRoot && (
        <ThreadPanel
          key={threadRoot}
          conversationId={c.id}
          rootId={threadRoot}
          me={me}
          names={names}
          candidates={candidates}
          client={client}
          canReact={canReact}
          showAuthor={isGroup || isChannel}
          language={i18n.language}
          onClose={() => setThreadRoot(null)}
          onReaction={toggleReaction}
          onRead={threadRead}
        />
      )}
      {forwarding && (
        <ForwardDialog
          conversationId={c.id}
          entry={forwarding}
          onClose={() => setForwarding(null)}
          onDone={(target) => {
            setForwarding(null);
            if (target === c.id) void load();
            setNotice(t('tools.forwarded'));
          }}
        />
      )}
    </section>
  );
}

function MessageItem({
  entry,
  mine,
  showAuthor,
  receipt,
  canEdit,
  canDelete,
  language,
  me,
  names,
  canReact,
  onReact,
  onOpenThread,
  tools,
  highlighted,
  onReply,
  onEdit,
  onDelete,
}: {
  tools: MessageTools;
  highlighted: boolean;
  entry: ConversationEntryResponse;
  mine: boolean;
  showAuthor: boolean;
  receipt: string | null;
  canEdit: boolean;
  canDelete: boolean;
  language: string;
  me: string;
  names: Map<string, string>;
  canReact: boolean;
  onReact: (emoji: string, reacted: boolean) => void;
  /** Opens the thread panel; null where threads are not available. */
  onOpenThread: (() => void) | null;
  onReply: () => void;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const { t } = useTranslation();
  const deleted = Boolean(entry.deletedAtUtc);
  return (
    <li id={`msg-${entry.id}`} className={`cx-message${mine ? ' is-mine' : ''}${deleted ? ' is-deleted' : ''}${highlighted ? ' is-highlighted' : ''}`}>
      {!mine && entry.actor && <Avatar user={entry.actor} size="sm" />}
      <div className="cx-message-stack">
        {showAuthor && !mine && <div className="cx-message-author">{entry.actor?.displayName ?? t('conversations.someone')}</div>}
        <div className="cx-bubble">
          {entry.forwardedFrom && !deleted && (
            <div className="cx-meta cx-forwarded">{t('tools.forwardedFrom', { name: entry.forwardedFrom.displayName })}</div>
          )}
          {entry.threadRootId && onOpenThread && (
            <button type="button" className="btn btn-sm btn-link p-0 cx-meta cx-thread-echo" onClick={onOpenThread}>
              {t('messages.threadReplyShown')} · {t('messages.openThread')}
            </button>
          )}
          {entry.replyTo && (
            <blockquote className="cx-quote">
              <strong>{entry.replyTo.author?.displayName ?? t('conversations.someone')}</strong>{' '}
              {entry.replyTo.deleted ? <em>{t('messages.deleted')}</em> : entry.replyTo.excerpt}
            </blockquote>
          )}
          {deleted ? (
            <p className="cx-message-body">
              <em>{t('messages.deleted')}</em>
            </p>
          ) : (
            <>
              {entry.body && <RichText className="cx-message-body" text={entry.body} mentions={mentionsOf(entry, names, me)} />}
              <AttachmentList items={entry.attachments ?? []} urlFor={attachmentLink} />
            </>
          )}
          <div className="cx-message-meta">
            <time dateTime={entry.createdAtUtc}>{formatDateTime(entry.createdAtUtc, language)}</time>
            {entry.editedAtUtc && !deleted && <span>· {t('messages.edited')}</span>}
            {entry.pinned && !deleted && <span>· {t('tools.pinnedLabel')}</span>}
            {entry.saved && !deleted && <span>· {t('tools.savedLabel')}</span>}
            {receipt && <span>· {receipt}</span>}
          </div>
        </div>
        {!deleted && <ReactionBar entry={entry} me={me} names={names} canReact={canReact} onToggle={onReact} />}
        {!entry.threadRootId && onOpenThread && <ThreadSummary entry={entry} onOpen={onOpenThread} />}
      </div>
      {!deleted && (
        // Shown on hover and keyboard focus (always on touch screens); the buttons keep their words as names.
        <div className="cx-message-tools" role="toolbar" aria-label={t('messages.actions')}>
          <ToolButton icon="reply" label={t('messages.reply')} onClick={onReply} />
          {canReact && <ReactionPicker compact onPick={(emoji) => onReact(emoji, true)} />}
          {onOpenThread && !entry.threadRootId && <ToolButton icon="thread" label={t('messages.replyInThread')} onClick={onOpenThread} />}
          {canEdit && <ToolButton icon="edit" label={t('messages.edit')} onClick={onEdit} />}
          {canDelete && <ToolButton icon="trash" label={t('messages.delete')} danger onClick={onDelete} />}
          <MessageMenu compact entry={entry} tools={tools} />
        </div>
      )}
    </li>
  );
}
