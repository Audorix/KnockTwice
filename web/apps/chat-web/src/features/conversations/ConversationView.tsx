import { Fragment, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { navigate, useLocation } from '../../app/router';
import type {
  AttachmentLimitsResponse,
  ConversationDetailResponse,
  ConversationEntryResponse,
  MessageAttachmentResponse,
  NotificationLevel,
  PinnedMessageResponse,
  SharedFileResponse,
  UserSummary,
} from '../../shared/api/contracts';
import { mentionCandidates, useMentionAutocomplete } from './mentions';
import { ForwardDialog, MessageMenu, PinsPanel, type MessageTools } from './messageTools';
import { useFormatting } from './formatting';
import * as planning from '../planning/planningApi';
import { TimeMenu } from '../planning/TimeMenu';
import { RichText, toPlainText, type RichTextMentions } from '../../shared/richText';
import { useSelectionAfterRender } from '../../shared/useSelectionAfterRender';

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
import { useConfirm } from '../../shared/components/ConfirmDialog';
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
import { startPrivateChat } from '../private/privateChatsApi';

/** Matches the server default (Messaging:EditWindow); the server enforces the real value. */
const editWindowMs = 15 * 60 * 1000;
const maxLength = 4000;
const typingRenewMs = 3000;
const translationLanguages = [
  ['en', 'English'], ['es', 'Español'], ['fr', 'Français'], ['de', 'Deutsch'], ['it', 'Italiano'],
  ['ro', 'Română'], ['sv', 'Svenska'], ['pl', 'Polski'],
] as const;

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

const automaticTranslationQueue: (() => Promise<void>)[] = [];
let activeAutomaticTranslations = 0;
function queueAutomaticTranslation(task: () => Promise<void>) {
  automaticTranslationQueue.push(task);
  queueAutomaticTranslationPump();
}

function queueAutomaticTranslationPump() {
  while (activeAutomaticTranslations < 2 && automaticTranslationQueue.length > 0) {
    const next = automaticTranslationQueue.shift()!;
    activeAutomaticTranslations++;
    void Promise.resolve().then(next).catch(() => undefined).finally(() => {
      activeAutomaticTranslations--;
      queueAutomaticTranslationPump();
    });
  }
}

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
  // Redesign 2026-11: Messages / Shared files, and where "new messages" began when this was opened.
  const [view, setView] = useState<'messages' | 'files'>('messages');
  const canUseTranslation = Boolean(user?.permissions.includes('messages.translate'));
  const [translationTarget, setTranslationTarget] = useState(() => {
    try { return localStorage.getItem('chatx.translation.targetLanguage') ?? 'en'; } catch { return 'en'; }
  });
  const [autoTranslateSupport, setAutoTranslateSupport] = useState(() => {
    try { return localStorage.getItem('chatx.translation.autoSupport') === 'true'; } catch { return false; }
  });
  const firstUnread = useRef<number | null>(null);
  if (detail && firstUnread.current === null) {
    firstUnread.current = detail.conversation.lastSequence > detail.conversation.lastReadSequence ? detail.conversation.lastReadSequence + 1 : Number.POSITIVE_INFINITY;
  }
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
  const selectInComposer = useSelectionAfterRender(composer);
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

  const [confirm, confirmDialog] = useConfirm();
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

  /** Emoji from the composer's picker go where the cursor is. */
  const insertAtCursor = (text: string) => {
    const box = composer.current;
    const start = box?.selectionStart ?? draft.length;
    const end = box?.selectionEnd ?? draft.length;
    onDraftChange(draft.slice(0, start) + text + draft.slice(end));
    selectInComposer(start + text.length);
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

  // The "N new messages" divider sits before the first unread message from someone else.
  const isNew = (e: ConversationEntryResponse) => e.kind === 'message' && e.sequence >= (firstUnread.current ?? Number.POSITIVE_INFINITY) && e.actor?.id !== me;
  const firstNew = entries.find(isNew);
  const newCount = entries.filter(isNew).length;

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
      {confirmDialog}
      <div className="cx-card-header cx-chat-header">
        <button type="button" className="btn btn-sm cx-back" onClick={() => navigate('/conversations')}>
          <NavIcon name="back" size={18} />
          <span className="visually-hidden">{t('conversations.backToList')}</span>
        </button>
        {isChannel ? (
          <span className="cx-chat-mark" aria-hidden="true">#</span>
        ) : c.counterpart ? (
          <Avatar user={c.counterpart} size="sm" />
        ) : (
          <Avatar user={{ id: c.id, avatar: { initials: initialsOf(c.title) } }} size="sm" />
        )}
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
        <div className="d-flex gap-2 flex-wrap justify-content-end align-items-center">
          {(isGroup || isChannel || isWebsite) && <ParticipantStack participants={detail.participants.map((p) => p.user)} />}
          <ToolButton icon="search" label={t('conversations.searchIn')} onClick={() => navigate(`/search?conversation=${encodeURIComponent(c.id)}`)} />
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
          <ConversationMenu
            pinned={!!c.pinned}
            privateWith={
              isWebsite
                ? []
                : !isGroup && !isChannel
                  ? c.counterpart && !c.counterpart.deleted ? [c.counterpart] : []
                  : detail.participants.map((p) => p.user).filter((u) => u.id !== me && !u.deleted)
            }
            onStartPrivate={(person) =>
              void act(async () => {
                // Sends the invitation and opens the chat, which waits for the person to accept.
                const chat = await startPrivateChat(person.id);
                navigate(`/private?id=${encodeURIComponent(chat.id)}`);
              })
            }
            onTogglePin={() =>
              void act(async () => {
                const summary = await api.setConversationPinned(c.id, !c.pinned);
                setDetail((d) => (d ? { ...d, conversation: { ...d.conversation, pinned: summary.pinned ?? false } } : d));
                onChanged();
              })
            }
          />
        </div>
      </div>
      <div className="cx-chat-tabs">
        <div role="tablist" aria-label={t('conversations.viewTabs')} className="cx-chat-tablist">
          {(['messages', 'files'] as const).map((key) => (
            <button key={key} type="button" role="tab" aria-selected={view === key} className={`cx-tab${view === key ? ' is-active' : ''}`} onClick={() => setView(key)}>
              {t(`conversations.views.${key}`)}
            </button>
          ))}
        </div>
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
        {canUseTranslation && view === 'messages' && (
          <div className="cx-translation-preference">
            <label className="cx-translation-target">
              <span className="visually-hidden">{t('messages.translationLanguage')}</span>
              <select
                className="form-select form-select-sm"
                aria-label={t('messages.translationLanguage')}
                value={translationTarget}
                onChange={(event) => {
                  const language = event.target.value;
                  setTranslationTarget(language);
                  try { localStorage.setItem('chatx.translation.targetLanguage', language); } catch { /* preference remains available for this view */ }
                }}
              >
                {translationLanguages.map(([code, name]) => <option key={code} value={code}>{name}</option>)}
              </select>
            </label>
            {isWebsite && (
              <label className="cx-auto-translate-control">
                <input
                  type="checkbox"
                  className="form-check-input"
                  checked={autoTranslateSupport}
                  onChange={(event) => {
                    const enabled = event.target.checked;
                    setAutoTranslateSupport(enabled);
                    try { localStorage.setItem('chatx.translation.autoSupport', String(enabled)); } catch { /* preference remains available for support */ }
                  }}
                />
                <span><strong>{t('messages.autoTranslateSupport')}</strong><small>{t('messages.autoTranslateSupportHint')}</small></span>
              </label>
            )}
          </div>
        )}
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

      {view === 'files' ? (
        <SharedFiles conversationId={c.id} language={i18n.language} />
      ) : (
      <>
      <div className="cx-timeline" ref={timeline} onScroll={onTimelineScroll} role="log" aria-label={t('conversations.history')} aria-live="polite">
        {olderCursor && (
          <div ref={topSentinel} className="text-center">
            <button type="button" className="btn btn-sm btn-link" onClick={() => void loadOlder()}>
              {t('conversations.loadOlder')}
            </button>
          </div>
        )}
        <ol className="list-unstyled mb-0">
          {entries.map((entry, index) => (
            <Fragment key={entry.id}>
              {(index === 0 || dayKey(entries[index - 1]!.createdAtUtc) !== dayKey(entry.createdAtUtc)) && (
                <li className="cx-day-separator" role="separator">
                  <span>{dayLabel(entry.createdAtUtc, i18n.language, t)}</span>
                </li>
              )}
              {entry === firstNew && (
                <li className="cx-new-divider" role="separator">
                  <span>{t('messages.newCount', { count: newCount })}</span>
                </li>
              )}
            {entry.kind === 'message' ? (
              <MessageItem
                conversationId={c.id}
                entry={entry}
                mine={entry.actor?.id === me}
                showAuthor={isGroup || isWebsite || isChannel}
                receipt={entry.actor?.id === me && !entry.deletedAtUtc ? receipt(entry) : null}
                canEdit={entry.actor?.id === me && !entry.deletedAtUtc && Date.now() - Date.parse(entry.createdAtUtc) < editWindowMs}
                canDelete={!entry.deletedAtUtc && (entry.actor?.id === me || canModerate)}
                canTranslate={!entry.deletedAtUtc && entry.actor?.id !== me && canUseTranslation}
                autoTranslate={isWebsite && autoTranslateSupport && canUseTranslation && entry.actor?.id !== me && !entry.deletedAtUtc}
                translationTarget={translationTarget}
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
                onDelete={() => void confirm({
                  title: t('confirm.deleteMessage.title'),
                  text: t('confirm.deleteMessage.text'),
                  confirmLabel: t('confirm.deleteMessage.action'),
                }).then((ok) => { if (ok) void (act(() => api.deleteMessage(c.id, entry.id))); })}
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
            )}
            </Fragment>
          ))}
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
        {typingNames.length > 0 && <span className="cx-typing-dots" aria-hidden="true"><span /><span /><span /></span>}
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
      ) : c.archivedAtUtc ? (
        <p className="cx-composer-closed cx-muted" role="status">
          {t('channels.archivedNotice')}
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
        <label htmlFor="composer" className="visually-hidden">
          {t('messages.compose')}
        </label>
        <div className="cx-composer-box">
          <div className="cx-mention-anchor">
            {mention.menu}
            <textarea
              id="composer"
              ref={composer}
              className="form-control"
              rows={1}
              maxLength={maxLength}
              placeholder={isChannel ? t('messages.placeholderIn', { name: `#${c.title}` }) : c.title ? t('messages.placeholderIn', { name: c.title }) : t('messages.placeholder')}
              value={draft}
              {...(editing ? {} : mention.inputProps)}
              onChange={(e) => {
                onDraftChange(e.target.value);
                if (!editing) mention.onInput(e.target.value, e.target.selectionStart);
              }}
              onKeyDown={onKeyDown}
            />
          </div>
          <div className="cx-composer-bar">
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
            <ReactionPicker compact label={t('messages.insertEmoji')} onPick={insertAtCursor} />
            {formatting.richTextButton}
            {formatting.toolbar}
            <span className="cx-composer-spacer" />
            <div className="cx-send-group">
              <button
                type="submit"
                className="btn btn-primary cx-icon-button"
                disabled={editing ? !draft.trim() : drafts.busy || (!draft.trim() && drafts.readyIds.length === 0)}
              >
                {!editing && <NavIcon name="send" size={17} />}
                {t(editing ? 'messages.save' : 'messages.send')}
              </button>
              {!editing && (
                <TimeMenu
                  label={t('planning.sendLater')}
                  buttonClassName="btn btn-primary cx-send-more"
                  buttonContent={<NavIcon name="chevron-down" size={16} />}
                  disabled={drafts.busy || (!draft.trim() && drafts.readyIds.length === 0)}
                  onPick={(at) => void sendLater(at)}
                />
              )}
            </div>
          </div>
        </div>
        {formatting.codeDialog}
        {formatting.richTextDialog}
        <div className="form-text cx-composer-hint">{t('messages.hintShort')}</div>
      </form>
      )}
      </>
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

// ---- Redesign 2026-11 helpers ---------------------------------------------------------------

const dayKey = (value: string) => new Date(value).toDateString();

/** "Today, November 24", "Yesterday, November 23" or "Friday, November 20". */
function dayLabel(value: string, language: string, t: TFunction): string {
  const date = new Date(value);
  const today = new Date();
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);
  const monthDay = date.toLocaleDateString(language, { month: 'long', day: 'numeric', ...(date.getFullYear() !== today.getFullYear() ? { year: 'numeric' } : {}) });
  if (date.toDateString() === today.toDateString()) return t('messages.todayOn', { date: monthDay });
  if (date.toDateString() === yesterday.toDateString()) return t('messages.yesterdayOn', { date: monthDay });
  return `${date.toLocaleDateString(language, { weekday: 'long' })}, ${monthDay}`;
}

const initialsOf = (title: string) => title.split(' ').filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '#';

/** Up to three participants' avatars and "+N". */
function ParticipantStack({ participants }: { participants: UserSummary[] }) {
  const { t } = useTranslation();
  const shown = participants.slice(0, 3);
  const more = participants.length - shown.length;
  return (
    <span className="cx-avatar-stack" title={participants.map((p) => p.displayName).join(', ')} aria-label={t('conversations.participants', { count: participants.length })} role="img">
      {shown.map((p) => <Avatar key={p.id} user={p} size="sm" />)}
      {more > 0 && <span className="cx-avatar cx-avatar-sm cx-avatar-more">+{more}</span>}
    </span>
  );
}

/**
 * The conversation's "…" menu: pin or unpin it in your list, and start a private chat. A private
 * chat is always between two people, so in a group or channel it first asks with whom.
 */
function ConversationMenu({ pinned, onTogglePin, privateWith, onStartPrivate }: {
  pinned: boolean;
  onTogglePin: () => void;
  privateWith: UserSummary[];
  onStartPrivate: (person: UserSummary) => void;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [choosing, setChoosing] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) setChoosing(false);
  }, [open]);
  const startPrivate = (person: UserSummary) => {
    setOpen(false);
    onStartPrivate(person);
  };
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  return (
    <div className="cx-row-menu" ref={box} onKeyDown={(e) => e.key === 'Escape' && setOpen(false)}>
      <ToolButton icon="dots" label={t('conversations.moreActions')} aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen((o) => !o)} />
      {open && (
        <div className="cx-menu" role="menu">
          <button type="button" role="menuitem" className="cx-menu-item" onClick={() => { setOpen(false); onTogglePin(); }}>
            <NavIcon name="pin" size={18} />
            {t(pinned ? 'conversations.unpin' : 'conversations.pin')}
          </button>
          {privateWith.length > 0 && (
            <button
              type="button"
              role="menuitem"
              className="cx-menu-item"
              aria-expanded={privateWith.length > 1 ? choosing : undefined}
              onClick={() => (privateWith.length === 1 ? startPrivate(privateWith[0]!) : setChoosing((c) => !c))}
            >
              <LockGlyph />
              {t('private.menuItem')}
            </button>
          )}
          {choosing && (
            <div role="group" aria-label={t('private.chooseWith')} className="cx-menu-group">
              <p className="cx-menu-heading">{t('private.chooseWith')}</p>
              {privateWith.map((person) => (
                <button key={person.id} type="button" role="menuitem" className="cx-menu-item" onClick={() => startPrivate(person)}>
                  <Avatar user={person} size="sm" />
                  <span className="text-truncate">{person.displayName}</span>
                </button>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

/** The Shared files tab: newest first, with who shared each file and when. */
function SharedFiles({ conversationId, language }: { conversationId: string; language: string }) {
  const { t } = useTranslation();
  const [items, setItems] = useState<SharedFileResponse[] | null>(null);
  const [cursor, setCursor] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    api.listSharedFiles(conversationId).then((page) => {
      setItems(page.items);
      setCursor(page.hasMore ? page.nextCursor : null);
    }, setError);
  }, [conversationId]);

  const more = () =>
    void api.listSharedFiles(conversationId, cursor).then((page) => {
      setItems((current) => [...(current ?? []), ...page.items]);
      setCursor(page.hasMore ? page.nextCursor : null);
    }, setError);

  return (
    <div className="cx-shared-files" role="tabpanel">
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {items === null ? (
        <p className="cx-muted">{t('auth.working')}</p>
      ) : items.length === 0 ? (
        <p className="cx-muted">{t('conversations.noFiles')}</p>
      ) : (
        <ul className="list-unstyled mb-0">
          {items.map((f) => (
            <li key={f.file.id} className="cx-attachment is-file">
              <svg className="cx-attachment-glyph" viewBox="0 0 24 24" width="28" height="28" aria-hidden="true" focusable="false">
                <path d="M6 2h8l5 5v13a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z" fill="none" stroke="currentColor" strokeWidth="1.6" />
                <path d="M14 2v5h5" fill="none" stroke="currentColor" strokeWidth="1.6" />
              </svg>
              <span className="cx-attachment-text">
                <span className="cx-attachment-name">{f.file.fileName}</span>
                <span className="cx-meta">
                  {fileTypeLabel(f.file.fileName)} · {formatLimit(f.file.size, language)} · {f.author?.displayName ?? t('conversations.someone')} · {formatDateTime(f.sharedAtUtc, language)}
                </span>
              </span>
              <a className="cx-tool" href={attachmentLink(f.file, true)} download={f.file.fileName} title={t('attachments.download')}>
                <NavIcon name="download" size={18} />
                <span className="visually-hidden">{`${t('attachments.download')} ${f.file.fileName}`}</span>
              </a>
            </li>
          ))}
        </ul>
      )}
      {cursor && (
        <button type="button" className="btn btn-sm btn-outline-secondary mt-2" onClick={more}>
          {t('conversations.olderFiles')}
        </button>
      )}
    </div>
  );
}

/** "PDF", "PNG"… from the file name. */
const fileTypeLabel = (name: string) => (name.includes('.') ? name.slice(name.lastIndexOf('.') + 1).toUpperCase() : '');

function MessageItem({
  conversationId,
  entry,
  mine,
  showAuthor,
  receipt,
  canEdit,
  canDelete,
  canTranslate,
  autoTranslate,
  translationTarget,
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
  conversationId: string;
  tools: MessageTools;
  highlighted: boolean;
  entry: ConversationEntryResponse;
  mine: boolean;
  showAuthor: boolean;
  receipt: string | null;
  canEdit: boolean;
  canDelete: boolean;
  canTranslate: boolean;
  autoTranslate: boolean;
  translationTarget: string;
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
  const [translation, setTranslation] = useState<{ language: string; text: string; sourceBody: string } | null>(null);
  const [translationBusy, setTranslationBusy] = useState(false);
  const [translationError, setTranslationError] = useState(false);
  const automaticRequest = useRef<string | null>(null);
  const desiredAutomaticRequest = useRef<string | null>(null);
  const automaticEnabled = useRef(autoTranslate);
  automaticEnabled.current = autoTranslate;
  const messageElement = useRef<HTMLLIElement>(null);
  const [autoVisible, setAutoVisible] = useState(false);
  useEffect(() => {
    automaticEnabled.current = autoTranslate;
    return () => {
      automaticEnabled.current = false;
      desiredAutomaticRequest.current = null;
    };
  }, [autoTranslate]);
  const translate = useCallback(async () => {
    setTranslationBusy(true);
    setTranslationError(false);
    try {
      const result = await api.translateMessage(conversationId, entry.id, translationTarget);
      setTranslation({ language: result.targetLanguage, text: result.translation, sourceBody: entry.body ?? '' });
    } catch {
      setTranslationError(true);
    } finally {
      setTranslationBusy(false);
    }
  }, [conversationId, entry.body, entry.id, translationTarget]);
  useEffect(() => {
    if (!autoTranslate) {
      setAutoVisible(false);
      automaticRequest.current = null;
      desiredAutomaticRequest.current = null;
      return;
    }
    const element = messageElement.current;
    if (!element) return;
    if (typeof IntersectionObserver === 'undefined') {
      setAutoVisible(true);
      return;
    }
    const observer = new IntersectionObserver((observed) => {
      if (observed.some(item => item.isIntersecting)) {
        setAutoVisible(true);
        observer.disconnect();
      }
    }, { root: element.closest('.cx-timeline'), threshold: 0.1 });
    observer.observe(element);
    return () => observer.disconnect();
  }, [autoTranslate, entry.id]);
  useEffect(() => {
    if (!autoTranslate || !autoVisible || !canTranslate || entry.deletedAtUtc || !entry.body) {
      if (!autoTranslate) {
        automaticRequest.current = null;
        desiredAutomaticRequest.current = null;
      }
      return;
    }
    const requestKey = `${entry.id}:${entry.editedAtUtc ?? ''}:${translationTarget}:${entry.body}`;
    if (automaticRequest.current === requestKey || (translation?.language === translationTarget && translation.sourceBody === entry.body)) return;
    automaticRequest.current = requestKey;
    desiredAutomaticRequest.current = requestKey;
    setTranslationError(false);
    queueAutomaticTranslation(() => desiredAutomaticRequest.current === requestKey && automaticEnabled.current ? translate() : Promise.resolve());
  }, [autoTranslate, autoVisible, canTranslate, entry.body, entry.deletedAtUtc, entry.editedAtUtc, entry.id, translate, translation, translationTarget]);
  const visibleTranslation = translation?.language === translationTarget && translation.sourceBody === (entry.body ?? '') ? translation.text : null;
  const deleted = Boolean(entry.deletedAtUtc);
  return (
    <li ref={messageElement} id={`msg-${entry.id}`} className={`cx-message${mine ? ' is-mine' : ''}${deleted ? ' is-deleted' : ''}${highlighted ? ' is-highlighted' : ''}`}>
      {!mine && entry.actor && <Avatar user={entry.actor} size="sm" />}
      <div className="cx-message-stack">
        <div className="cx-message-head">
          {(showAuthor || !mine) && !mine && <span className="cx-message-author">{entry.actor?.displayName ?? t('conversations.someone')}</span>}
          <time dateTime={entry.createdAtUtc} title={formatDateTime(entry.createdAtUtc, language)}>
            {new Date(entry.createdAtUtc).toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' })}
          </time>
          {entry.editedAtUtc && !deleted && <span>· {t('messages.edited')}</span>}
          {entry.pinned && !deleted && <span>· {t('tools.pinnedLabel')}</span>}
          {entry.saved && !deleted && <span>· {t('tools.savedLabel')}</span>}
        </div>
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
              {visibleTranslation && (
                <div className="cx-message-translation">
                  <span className="cx-message-translation-label"><NavIcon name="globe" size={14} />{t('messages.translatedTo', { language: translationTarget })}</span>
                  <RichText className="cx-message-body" text={visibleTranslation} />
                  <details><summary>{t('messages.showOriginal')}</summary><RichText className="cx-message-body" text={entry.body ?? ''} mentions={mentionsOf(entry, names, me)} /></details>
                </div>
              )}
              <AttachmentList items={entry.attachments ?? []} urlFor={attachmentLink} />
            </>
          )}
        </div>
        {receipt && (
          <div className="cx-message-receipt">
            <NavIcon name="check-double" size={15} />
            {receipt}
          </div>
        )}
        {!deleted && <ReactionBar entry={entry} me={me} names={names} canReact={canReact} onToggle={onReact} />}
        {!entry.threadRootId && onOpenThread && <ThreadSummary entry={entry} onOpen={onOpenThread} />}
      </div>
      {!deleted && (
        // Shown on hover and keyboard focus (always on touch screens); the buttons keep their words as names.
        <div className="cx-message-tools" role="toolbar" aria-label={t('messages.actions')}>
          <ToolButton icon="reply" label={t('messages.reply')} onClick={onReply} />
          {canTranslate && <ToolButton icon="globe" label={visibleTranslation ? t('messages.translateAgain') : t('messages.translate')} disabled={translationBusy} onClick={() => void translate()} />}
          {canReact && <ReactionPicker compact onPick={(emoji) => onReact(emoji, true)} />}
          {onOpenThread && !entry.threadRootId && <ToolButton icon="thread" label={t('messages.replyInThread')} onClick={onOpenThread} />}
          {canEdit && <ToolButton icon="edit" label={t('messages.edit')} onClick={onEdit} />}
          {canDelete && <ToolButton icon="trash" label={t('messages.delete')} danger onClick={onDelete} />}
          <MessageMenu compact entry={entry} tools={tools} />
        </div>
      )}
      {canTranslate && (
        <div className="cx-message-translation-controls">
          <span className="cx-meta">{t('messages.translationDisclosure')}</span>
          {translationBusy && <span className="cx-meta">{t('messages.translating')}</span>}
          {translationError && <span className="cx-meta text-danger">{t('messages.translationFailed')}</span>}
        </div>
      )}
    </li>
  );
}
