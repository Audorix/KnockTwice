import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { ConversationEntryResponse, ConversationSummaryResponse, PinnedMessageResponse } from '../../shared/api/contracts';
import { FormAlert } from '../../shared/components/Field';
import { formatDateTime } from '../../shared/format';
import { errorMessage } from '../auth/errors';
import * as api from './conversationsApi';
import { toPlainText } from '../../shared/richText';
import { localInputValue, presetTimes } from '../planning/TimeMenu';

/** Phase 18 (ADR-032): pins, saved messages and forwarding. */

export interface MessageTools {
  canPin: boolean;
  canForward: boolean;
  onTogglePin: () => void;
  onToggleSaved: () => void;
  onForward: () => void;
  /** Phase 20: remind me about this message at a time. */
  onRemind: (at: Date) => void;
}

/** The "More" menu of a message: save, pin, forward, remind me. */
export function MessageMenu({ entry, tools }: { entry: ConversationEntryResponse; tools: MessageTools }) {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const [remind, setRemind] = useState(false);
  const [custom, setCustom] = useState('');
  const box = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (!open) setRemind(false);
    else setCustom(localInputValue(new Date(Date.now() + 60 * 60_000)));
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  const run = (action: () => void) => {
    setOpen(false);
    action();
  };

  return (
    <span className="cx-reaction-picker" ref={box}>
      <button type="button" className="btn btn-sm btn-link p-0" aria-expanded={open} aria-haspopup="menu" onClick={() => setOpen((o) => !o)}>
        {t('tools.more')}
      </button>
      {open && remind && (
        <span className="cx-reaction-menu cx-tools-menu cx-time-menu" role="menu" aria-label={t('planning.remindMe')} onKeyDown={(e) => e.key === 'Escape' && setOpen(false)}>
          {presetTimes().map((choice) => (
            <button key={choice.key} type="button" role="menuitem" className="cx-tools-item" onClick={() => run(() => tools.onRemind(choice.at))}>
              {t(`planning.presets.${choice.key}`)}{' '}
              <span className="cx-meta">{choice.at.toLocaleString(i18n.language, { weekday: 'short', hour: '2-digit', minute: '2-digit' })}</span>
            </button>
          ))}
          <form
            className="cx-time-custom"
            onSubmit={(e) => {
              e.preventDefault();
              const at = new Date(custom);
              if (!Number.isNaN(at.getTime())) run(() => tools.onRemind(at));
            }}
          >
            <label className="visually-hidden" htmlFor={`remind-${entry.id}`}>
              {t('planning.customTime')}
            </label>
            <input id={`remind-${entry.id}`} type="datetime-local" className="form-control form-control-sm" value={custom} onChange={(e) => setCustom(e.target.value)} />
            <button type="submit" className="btn btn-sm btn-outline-primary">
              {t('planning.set')}
            </button>
          </form>
        </span>
      )}
      {open && !remind && (
        <span className="cx-reaction-menu cx-tools-menu" role="menu" onKeyDown={(e) => e.key === 'Escape' && setOpen(false)}>
          <button type="button" role="menuitem" className="cx-tools-item" onClick={() => setRemind(true)}>
            {t('planning.remindMe')}
          </button>
          <button type="button" role="menuitem" className="cx-tools-item" onClick={() => run(tools.onToggleSaved)}>
            {t(entry.saved ? 'tools.unsave' : 'tools.save')}
          </button>
          {tools.canPin && (
            <button type="button" role="menuitem" className="cx-tools-item" onClick={() => run(tools.onTogglePin)}>
              {t(entry.pinned ? 'tools.unpin' : 'tools.pin')}
            </button>
          )}
          {tools.canForward && (
            <button type="button" role="menuitem" className="cx-tools-item" onClick={() => run(tools.onForward)}>
              {t('tools.forward')}
            </button>
          )}
        </span>
      )}
    </span>
  );
}

/** The conversation's pinned messages; picking one jumps to it. */
export function PinsPanel({
  pins,
  language,
  canUnpin,
  onJump,
  onUnpin,
  onClose,
}: {
  pins: PinnedMessageResponse[];
  language: string;
  canUnpin: boolean;
  onJump: (entry: ConversationEntryResponse) => void;
  onUnpin: (entry: ConversationEntryResponse) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  return (
    <section className="cx-pins" aria-labelledby="pins-title">
      <div className="d-flex justify-content-between align-items-center mb-1">
        <h3 id="pins-title" className="h6 mb-0">
          {t('tools.pinnedTitle', { count: pins.length })}
        </h3>
        <button type="button" className="btn-close btn-sm" aria-label={t('tools.closePins')} onClick={onClose} />
      </div>
      <ul className="list-unstyled mb-0">
        {pins.map((pin) => (
          <li key={pin.entry.id} className="cx-pin">
            <button type="button" className="cx-pin-open" onClick={() => onJump(pin.entry)}>
              <strong>{pin.entry.actor?.displayName ?? t('conversations.someone')}</strong>{' '}
              <span className="cx-pin-text">{toPlainText(pin.entry.body) || t('attachments.count', { count: pin.entry.attachments?.length ?? 0 })}</span>
              <span className="cx-meta d-block">
                {t('tools.pinnedBy', { name: pin.pinnedBy?.displayName ?? t('conversations.someone'), date: formatDateTime(pin.pinnedAtUtc, language) })}
              </span>
            </button>
            {canUnpin && (
              <button type="button" className="btn btn-sm btn-link p-0" onClick={() => onUnpin(pin.entry)}>
                {t('tools.unpin')}
              </button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

function newClientId(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

/** Picks a conversation and an optional comment, then forwards a copy of the message. */
export function ForwardDialog({
  conversationId,
  entry,
  onDone,
  onClose,
}: {
  conversationId: string;
  entry: ConversationEntryResponse;
  onDone: (targetConversationId: string) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [conversations, setConversations] = useState<ConversationSummaryResponse[] | null>(null);
  const [filter, setFilter] = useState('');
  const [target, setTarget] = useState('');
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  // One idempotency key per dialog: a retry after a network error never forwards twice.
  const clientMessageId = useMemo(() => newClientId(), []);
  const dialog = useRef<HTMLDivElement>(null);

  useEffect(() => {
    api.listConversations().then((list) => setConversations(list.filter((c) => c.type !== 'website')), setError);
    dialog.current?.focus();
  }, []);

  const shown = (conversations ?? []).filter((c) => c.title.toLowerCase().includes(filter.trim().toLowerCase())).slice(0, 50);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!target || busy) return;
    setBusy(true);
    setError(null);
    try {
      await api.forwardMessage(conversationId, entry.id, { targetConversationId: target, clientMessageId, ...(comment.trim() ? { comment: comment.trim() } : {}) });
      onDone(target);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="cx-modal-backdrop" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        className="cx-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="forward-title"
        tabIndex={-1}
        ref={dialog}
        onKeyDown={(e) => e.key === 'Escape' && onClose()}
      >
        <form onSubmit={(e) => void submit(e)}>
          <div className="d-flex justify-content-between align-items-center mb-2">
            <h3 id="forward-title" className="h5 mb-0">
              {t('tools.forwardTitle')}
            </h3>
            <button type="button" className="btn-close" aria-label={t('conversations.cancel')} onClick={onClose} />
          </div>
          <blockquote className="cx-quote">
            <strong>{entry.actor?.displayName ?? t('conversations.someone')}</strong> {toPlainText(entry.body).slice(0, 140)}
          </blockquote>
          {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
          <label className="form-label" htmlFor="forward-filter">
            {t('tools.forwardTo')}
          </label>
          <input id="forward-filter" className="form-control form-control-sm mb-2" value={filter} placeholder={t('tools.forwardFilter')} onChange={(e) => setFilter(e.target.value)} />
          <div className="cx-forward-list" role="radiogroup" aria-label={t('tools.forwardTo')}>
            {conversations === null && error === null && <p className="cx-muted small mb-0">{t('auth.working')}</p>}
            {shown.map((c) => (
              <label key={c.id} className="form-check cx-forward-option">
                <input type="radio" className="form-check-input" name="forward-target" value={c.id} checked={target === c.id} onChange={() => setTarget(c.id)} />
                <span className="form-check-label">
                  {c.type === 'channel' ? `# ${c.title}` : c.title}
                  {c.id === conversationId && <span className="cx-meta"> — {t('tools.thisConversation')}</span>}
                </span>
              </label>
            ))}
          </div>
          <label className="form-label mt-2" htmlFor="forward-comment">
            {t('tools.forwardComment')}
          </label>
          <textarea id="forward-comment" className="form-control" rows={2} maxLength={4000} value={comment} onChange={(e) => setComment(e.target.value)} />
          <div className="d-flex justify-content-end gap-2 mt-3">
            <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
              {t('conversations.cancel')}
            </button>
            <button type="submit" className="btn btn-primary" disabled={!target || busy}>
              {t('tools.forward')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
