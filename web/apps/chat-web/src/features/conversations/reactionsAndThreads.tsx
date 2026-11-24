import { useCallback, useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { ConversationEntryResponse, ReactionResponse } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { formatDateTime } from '../../shared/format';
import { AttachmentList } from '../attachments/Attachments';
import * as files from '../attachments/attachmentsApi';
import { errorMessage } from '../auth/errors';
import type { RealtimeClient } from '../realtime/realtimeClient';
import * as api from './conversationsApi';
import { useMentionAutocomplete, type MentionCandidate } from './mentions';
import { useFormatting } from './formatting';
import * as planning from '../planning/planningApi';
import { RichText } from '../../shared/richText';
import { ToolButton } from '../../shared/components/ToolButton';

/** Phase 16: emoji reactions and threads (ADR-030). */

export const quickEmoji = ['👍', '❤️', '😂', '😮', '😢', '🎉'];

const moreEmoji = [
  '👏', '🙏', '🔥', '✅', '❌', '👀', '💯', '🤔', '😅', '😍', '🥳', '😎',
  '🙌', '💪', '🚀', '⭐', '💡', '📌', '👌', '🤝', '😊', '😉', '😬', '😴',
  '🤯', '😡', '🥲', '🙈', '☕', '🍕', '🎂', '⚠️',
];

const maxReactionLength = 32;

/** Upserts by id keeping the newest version (shared with the main timeline). */
export function upsertEntry(entries: ConversationEntryResponse[], entry: ConversationEntryResponse): ConversationEntryResponse[] {
  const existing = entries.find((e) => e.id === entry.id);
  if (existing && (existing.version ?? 1) >= (entry.version ?? 1)) return entries;
  // Reactions and the thread summary change without a new version: keep what is known locally.
  const merged: ConversationEntryResponse = existing
    ? { ...entry, reactions: entry.deletedAtUtc ? null : entry.reactions ?? existing.reactions ?? null, thread: entry.thread ?? existing.thread ?? null }
    : entry;
  return [...entries.filter((e) => e.id !== entry.id), merged].sort((a, b) => a.sequence - b.sequence);
}

export function withReactions(entries: ConversationEntryResponse[], messageId: string, reactions: ReactionResponse[]): ConversationEntryResponse[] {
  return entries.map((e) => (e.id === messageId ? { ...e, reactions } : e));
}

/** The reaction chips under a message plus the picker. */
export function ReactionBar({
  entry,
  me,
  names,
  canReact,
  onToggle,
}: {
  entry: ConversationEntryResponse;
  me: string;
  names: Map<string, string>;
  canReact: boolean;
  onToggle: (emoji: string, reacted: boolean) => void;
}) {
  const { t } = useTranslation();
  const reactions = entry.reactions ?? [];
  if (reactions.length === 0) return null;
  return (
    <div className="cx-reactions" role="group" aria-label={t('messages.react')}>
      {reactions.map((r) => {
        const mine = r.userIds.includes(me);
        const who = r.userIds.slice(0, 10).map((id) => (id === me ? t('messages.you') : names.get(id) ?? t('conversations.someone'))).join(', ');
        const label = t('messages.reactionSummary', { emoji: r.emoji, count: r.count, names: who });
        return (
          <button
            key={r.emoji}
            type="button"
            className={`cx-reaction${mine ? ' is-mine' : ''}`}
            aria-pressed={mine}
            aria-label={label}
            title={label}
            disabled={!canReact}
            onClick={() => onToggle(r.emoji, !mine)}
          >
            <span aria-hidden="true">{r.emoji}</span> <span className="cx-reaction-count">{r.count}</span>
          </button>
        );
      })}
    </div>
  );
}

/** The "React" button: six quick emoji, a larger grid and a field for any other emoji. */
export function ReactionPicker({ onPick, compact = false }: { onPick: (emoji: string) => void; compact?: boolean }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [more, setMore] = useState(false);
  const [custom, setCustom] = useState('');
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  const pick = (emoji: string) => {
    onPick(emoji);
    setOpen(false);
    setMore(false);
    setCustom('');
  };

  const onKey = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') setOpen(false);
  };

  return (
    <span className="cx-reaction-picker" ref={box}>
      {compact ? (
        <ToolButton icon="smile" label={t('messages.react')} aria-expanded={open} aria-haspopup="true" onClick={() => setOpen((o) => !o)} />
      ) : (
        <button type="button" className="btn btn-sm btn-link p-0" aria-expanded={open} aria-haspopup="true" onClick={() => setOpen((o) => !o)}>
          {t('messages.react')}
        </button>
      )}
      {open && (
        <div className="cx-reaction-menu" role="dialog" aria-label={t('messages.react')} onKeyDown={onKey}>
          <div className="cx-emoji-row">
            {quickEmoji.map((emoji) => (
              <button key={emoji} type="button" className="cx-emoji" aria-label={t('messages.reactWith', { emoji })} onClick={() => pick(emoji)}>
                {emoji}
              </button>
            ))}
            <button type="button" className="cx-emoji cx-emoji-more" aria-expanded={more} aria-label={t('messages.moreEmoji')} onClick={() => setMore((m) => !m)}>
              +
            </button>
          </div>
          {more && (
            <>
              <div className="cx-emoji-grid">
                {moreEmoji.map((emoji) => (
                  <button key={emoji} type="button" className="cx-emoji" aria-label={t('messages.reactWith', { emoji })} onClick={() => pick(emoji)}>
                    {emoji}
                  </button>
                ))}
              </div>
              <form
                className="cx-emoji-custom"
                onSubmit={(e) => {
                  e.preventDefault();
                  if (custom.trim()) pick(custom.trim());
                }}
              >
                <label className="visually-hidden" htmlFor="cx-emoji-custom">
                  {t('messages.otherEmoji')}
                </label>
                <input
                  id="cx-emoji-custom"
                  className="form-control form-control-sm"
                  maxLength={maxReactionLength}
                  placeholder={t('messages.otherEmoji')}
                  value={custom}
                  onChange={(e) => setCustom(e.target.value)}
                />
                <button type="submit" className="btn btn-sm btn-outline-primary" disabled={!custom.trim()}>
                  {t('messages.addEmoji')}
                </button>
              </form>
            </>
          )}
        </div>
      )}
    </span>
  );
}

/** The thread link under a message that has replies. */
export function ThreadSummary({ entry, onOpen }: { entry: ConversationEntryResponse; onOpen: () => void }) {
  const { t } = useTranslation();
  const thread = entry.thread;
  if (!thread || thread.replyCount === 0) return null;
  return (
    <button type="button" className="btn btn-sm btn-link p-0 cx-thread-link" onClick={onOpen}>
      {t('messages.threadReplies', { count: thread.replyCount })}
      {thread.unread ? <span className="badge rounded-pill text-bg-primary ms-1">{t('messages.threadUnread', { count: thread.unread })}</span> : null}
    </button>
  );
}

function newClientId(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

const attachmentLink = (item: { id: string }, download: boolean) => files.attachmentUrl(item.id, download);

/**
 * The side panel of one thread: its first message, the replies, a composer with
 * "Also send to the conversation", and follow / stop following.
 */
export function ThreadPanel({
  conversationId,
  rootId,
  me,
  names,
  candidates,
  client,
  canReact,
  showAuthor,
  language,
  onClose,
  onReaction,
  onRead,
}: {
  conversationId: string;
  rootId: string;
  me: string;
  names: Map<string, string>;
  candidates: MentionCandidate[];
  client: RealtimeClient | null;
  canReact: boolean;
  showAuthor: boolean;
  language: string;
  onClose: () => void;
  onReaction: (messageId: string, emoji: string, reacted: boolean) => Promise<void>;
  onRead: () => void;
}) {
  const { t } = useTranslation();
  const [root, setRoot] = useState<ConversationEntryResponse | null>(null);
  const [replies, setReplies] = useState<ConversationEntryResponse[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [following, setFollowing] = useState(false);
  const [draft, setDraft] = useState('');
  const [alsoInConversation, setAlsoInConversation] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const readUpTo = useRef(0);
  const end = useRef<HTMLDivElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);
  const composer = useRef<HTMLTextAreaElement>(null);
  const mention = useMentionAutocomplete(candidates, draft, setDraft, composer);
  const formatting = useFormatting(composer, draft, setDraft);

  // Phase 20: the thread's draft is kept on the server too.
  const draftReady = useRef(false);
  const lastSavedDraft = useRef('');
  useEffect(() => {
    let current = true;
    planning.listDrafts().then(
      (all) => {
        if (!current) return;
        const saved = all.find((d) => d.conversationId === conversationId && d.threadRootId === rootId);
        if (saved) {
          lastSavedDraft.current = saved.body;
          setDraft((typed) => typed || saved.body);
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
  }, [conversationId, rootId]);
  useEffect(() => {
    if (!draftReady.current || draft === lastSavedDraft.current) return;
    const timer = setTimeout(() => {
      lastSavedDraft.current = draft;
      void planning.saveDraft(conversationId, { body: draft, threadRootId: rootId }).catch(() => undefined);
    }, 800);
    return () => clearTimeout(timer);
  }, [draft, conversationId, rootId]);

  const markRead = useCallback(
    async (sequence: number) => {
      if (sequence <= readUpTo.current) return;
      readUpTo.current = sequence;
      try {
        await api.markThreadRead(conversationId, rootId, sequence);
        onRead();
      } catch {
        // Not critical: the next view retries.
      }
    },
    [conversationId, rootId, onRead],
  );

  const load = useCallback(async () => {
    try {
      const page = await api.getThread(conversationId, rootId);
      setRoot(page.root);
      setReplies(page.items);
      setCursor(page.hasMore ? page.nextCursor : null);
      setFollowing(page.following);
      readUpTo.current = page.lastReadSequence;
      const last = page.items.at(-1);
      if (last) await markRead(last.sequence);
    } catch (err) {
      setError(err);
    }
  }, [conversationId, rootId, markRead]);

  useEffect(() => {
    readUpTo.current = 0;
    setRoot(null);
    setReplies([]);
    void load();
  }, [load]);

  useEffect(() => {
    closeButton.current?.focus();
  }, [rootId]);

  useEffect(() => {
    end.current?.scrollIntoView?.({ block: 'end' });
  }, [replies.length]);

  useEffect(() => {
    if (!client) return;
    const offMessage = client.onMessage((_type, event) => {
      if (event.conversationId !== conversationId) return;
      if (event.entry.id === rootId) setRoot((current) => (current && (current.version ?? 1) > (event.entry.version ?? 1) ? current : { ...event.entry, thread: event.entry.thread ?? current?.thread ?? null }));
      if (event.entry.threadRootId !== rootId) return;
      setReplies((current) => upsertEntry(current, event.entry));
      if (event.entry.actor?.id !== me && document.visibilityState === 'visible') void markRead(event.entry.sequence);
    });
    const offReaction = client.onReaction((event) => {
      if (event.conversationId !== conversationId) return;
      setRoot((current) => (current && current.id === event.messageId ? { ...current, reactions: event.reactions } : current));
      setReplies((current) => withReactions(current, event.messageId, event.reactions));
    });
    const offReconnect = client.onReconnected(() => void load());
    return () => {
      offMessage();
      offReaction();
      offReconnect();
    };
  }, [client, conversationId, rootId, me, markRead, load]);

  const loadOlder = async () => {
    if (!cursor) return;
    try {
      const page = await api.getThread(conversationId, rootId, cursor);
      setReplies((current) => page.items.reduce(upsertEntry, current));
      setCursor(page.hasMore ? page.nextCursor : null);
    } catch (err) {
      setError(err);
    }
  };

  const submit = async (event?: FormEvent) => {
    event?.preventDefault();
    const body = draft.trim();
    if (!body || sending) return;
    setSending(true);
    setError(null);
    try {
      const saved = await api.sendMessage(conversationId, {
        clientMessageId: newClientId(),
        body,
        threadRootId: rootId,
        alsoInConversation,
        mentionUserIds: mention.mentionIds(body),
      });
      setReplies((current) => upsertEntry(current, saved));
      readUpTo.current = Math.max(readUpTo.current, saved.sequence);
      setDraft('');
      mention.reset();
      setFollowing(true);
    } catch (err) {
      setError(err);
    } finally {
      setSending(false);
    }
  };

  const toggleFollow = async () => {
    try {
      await api.setThreadFollowing(conversationId, rootId, !following);
      setFollowing((f) => !f);
    } catch (err) {
      setError(err);
    }
  };

  const react = async (messageId: string, emoji: string, reacted: boolean) => {
    setError(null);
    try {
      await onReaction(messageId, emoji, reacted);
    } catch (err) {
      setError(err);
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (mention.onKeyDown(event)) return;
    if (formatting.onKeyDown(event)) return;
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void submit();
    } else if (event.key === 'Escape') {
      onClose();
    }
  };

  const renderEntry = (entry: ConversationEntryResponse, isRoot: boolean) => {
    const deleted = Boolean(entry.deletedAtUtc);
    const mine = entry.actor?.id === me;
    return (
      <li key={entry.id} className={`cx-thread-entry${isRoot ? ' is-root' : ''}${deleted ? ' is-deleted' : ''}`}>
        {entry.actor && <Avatar user={entry.actor} size="sm" />}
        <div className="min-w-0 flex-grow-1">
          <div className="cx-message-author">
            {(showAuthor || !mine) && (entry.actor?.displayName ?? t('conversations.someone'))}{' '}
            <time className="cx-meta" dateTime={entry.createdAtUtc}>
              {formatDateTime(entry.createdAtUtc, language)}
            </time>
          </div>
          {deleted ? (
            <p className="cx-message-body">
              <em>{t('messages.deleted')}</em>
            </p>
          ) : (
            <>
              {entry.body && (
                <RichText
                  className="cx-message-body"
                  text={entry.body}
                  {...(entry.mentions
                    ? {
                        mentions: {
                          names: entry.mentions.userIds.map((id) => names.get(id)).filter((n): n is string => Boolean(n)),
                          everyone: entry.mentions.everyone,
                          here: entry.mentions.here,
                        },
                      }
                    : {})}
                />
              )}
              <AttachmentList items={entry.attachments ?? []} urlFor={attachmentLink} />
              <ReactionBar entry={entry} me={me} names={names} canReact={canReact} onToggle={(emoji, reacted) => void react(entry.id, emoji, reacted)} />
              {canReact && (
                <div className="cx-message-meta">
                  <span className="cx-message-actions">
                    <ReactionPicker onPick={(emoji) => void react(entry.id, emoji, true)} />
                  </span>
                </div>
              )}
            </>
          )}
        </div>
      </li>
    );
  };

  return (
    <aside className="cx-thread-panel" aria-labelledby="thread-title" role="complementary">
      <div className="cx-thread-header">
        <h3 id="thread-title" className="cx-card-title mb-0">
          {t('threads.title')}
        </h3>
        <div className="d-flex gap-2 align-items-center">
          <button type="button" className="btn btn-sm btn-outline-secondary" aria-pressed={following} title={t('threads.followHint')} onClick={() => void toggleFollow()}>
            {t(following ? 'threads.unfollow' : 'threads.follow')}
          </button>
          <button type="button" ref={closeButton} className="btn-close" aria-label={t('threads.close')} onClick={onClose} />
        </div>
      </div>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <div className="cx-thread-body">
        {root ? (
          <ol className="list-unstyled mb-0">
            {renderEntry(root, true)}
            <li className="cx-thread-divider" aria-hidden="true">
              {t('threads.replies')}
            </li>
            {cursor && (
              <li className="text-center">
                <button type="button" className="btn btn-sm btn-link" onClick={() => void loadOlder()}>
                  {t('threads.loadOlder')}
                </button>
              </li>
            )}
            {replies.length === 0 && <li className="cx-muted small">{t('threads.empty')}</li>}
            {replies.map((r) => renderEntry(r, false))}
          </ol>
        ) : (
          error === null && <p className="cx-muted">{t('auth.working')}</p>
        )}
        <div ref={end} />
      </div>
      <form className="cx-composer" onSubmit={(e) => void submit(e)}>
        {formatting.toolbar}
        <label htmlFor="thread-composer" className="visually-hidden">
          {t('threads.placeholder')}
        </label>
        <div className="cx-composer-row">
          <div className="cx-mention-anchor">
            {mention.menu}
            <textarea
              id="thread-composer"
              ref={composer}
              className="form-control"
              rows={2}
              maxLength={4000}
              placeholder={t('threads.placeholder')}
              value={draft}
              {...mention.inputProps}
              onChange={(e) => {
                setDraft(e.target.value);
                mention.onInput(e.target.value, e.target.selectionStart);
              }}
              onKeyDown={onKeyDown}
            />
          </div>
          <button type="submit" className="btn btn-primary" disabled={!draft.trim() || sending}>
            {t('messages.send')}
          </button>
        </div>
        <div className="form-check mt-1">
          <input
            id="thread-also"
            type="checkbox"
            className="form-check-input"
            checked={alsoInConversation}
            onChange={(e) => setAlsoInConversation(e.target.checked)}
          />
          <label className="form-check-label small" htmlFor="thread-also">
            {t('threads.alsoInConversation')}
          </label>
        </div>
      </form>
    </aside>
  );
}
