import { useCallback, useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import type { ConversationDetailResponse, UserSummary } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';

/** Phase 17: @mentions in the composer (ADR-031). */

/** Matches the server default (Messaging:EveryoneMentionLimit); the server enforces the real value. */
export const everyoneMentionLimit = 20;

export interface MentionCandidate {
  /** User id; "everyone" / "here" for the broadcasts. */
  id: string;
  label: string;
  kind: 'user' | 'everyone' | 'here';
  user?: UserSummary;
}

const fold = (text: string) => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();

/** People one may mention in the conversation, plus @everyone / @here where allowed. */
export function mentionCandidates(detail: ConversationDetailResponse | null, me: string): MentionCandidate[] {
  if (!detail || detail.conversation.type === 'website') return [];
  const c = detail.conversation;
  const people: MentionCandidate[] = detail.participants
    .filter((p) => p.user.id !== me && !p.user.deleted)
    .map((p) => ({ id: p.user.id, label: p.user.displayName, kind: 'user', user: p.user }));
  const room = c.type === 'group' || c.type === 'channel';
  const manager = c.myRole === 'owner' || c.myRole === 'administrator';
  const broadcasts: MentionCandidate[] =
    room && (detail.participants.length <= everyoneMentionLimit || manager)
      ? [
          { id: 'everyone', label: 'everyone', kind: 'everyone' },
          { id: 'here', label: 'here', kind: 'here' },
        ]
      : [];
  return [...broadcasts, ...people];
}

interface Query {
  text: string;
  start: number;
  caret: number;
}

/**
 * Suggests people after "@" in a textarea. Picking one inserts "@Name " and remembers who it was,
 * so {@link MentionAutocomplete.mentionIds} can send their ids with the message (only for names
 * still in the text).
 */
export interface MentionAutocomplete {
  /** Call from the textarea's change handler with the new value and caret. */
  onInput: (value: string, caret: number) => void;
  /** Call first in the textarea's key handler; true when the key was used by the suggestions. */
  onKeyDown: (event: KeyboardEvent<HTMLTextAreaElement>) => boolean;
  inputProps: Record<string, string | boolean | undefined>;
  menu: ReactNode;
  mentionIds: (body: string) => string[];
  reset: () => void;
}

export function useMentionAutocomplete(
  candidates: MentionCandidate[],
  value: string,
  setValue: (value: string) => void,
  textarea: RefObject<HTMLTextAreaElement | null>,
): MentionAutocomplete {
  const { t } = useTranslation();
  const listId = useId();
  const [query, setQuery] = useState<Query | null>(null);
  const [active, setActive] = useState(0);
  const chosen = useRef(new Map<string, string>());

  const matches = useMemo(() => {
    if (!query) return [];
    const q = fold(query.text);
    return candidates.filter((c) => fold(c.label).includes(q)).sort((a, b) => Number(!fold(a.label).startsWith(q)) - Number(!fold(b.label).startsWith(q))).slice(0, 8);
  }, [candidates, query]);

  const onInput = useCallback((text: string, caret: number) => {
    const match = /(^|[\s([{"'])@([^\s@]{0,40})$/u.exec(text.slice(0, caret));
    setQuery(match ? { text: match[2]!, start: caret - match[2]!.length - 1, caret } : null);
    setActive(0);
  }, []);

  const pick = useCallback(
    (candidate: MentionCandidate) => {
      if (!query) return;
      const insert = `@${candidate.label} `;
      const next = value.slice(0, query.start) + insert + value.slice(query.caret);
      if (candidate.kind === 'user') chosen.current.set(candidate.label, candidate.id);
      setValue(next);
      setQuery(null);
      const caret = query.start + insert.length;
      requestAnimationFrame(() => {
        const box = textarea.current;
        if (!box) return;
        box.focus();
        box.setSelectionRange(caret, caret);
      });
    },
    [query, value, setValue, textarea],
  );

  const open = matches.length > 0;

  const onKeyDown = useCallback(
    (event: KeyboardEvent<HTMLTextAreaElement>) => {
      if (!open) return false;
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        setActive((i) => (i + (event.key === 'ArrowDown' ? 1 : matches.length - 1)) % matches.length);
        return true;
      }
      if ((event.key === 'Enter' || event.key === 'Tab') && !event.shiftKey) {
        event.preventDefault();
        pick(matches[Math.min(active, matches.length - 1)]!);
        return true;
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        setQuery(null);
        return true;
      }
      return false;
    },
    [open, matches, active, pick],
  );

  const mentionIds = useCallback(
    (body: string) => [...new Set([...chosen.current].filter(([label]) => body.includes(`@${label}`)).map(([, id]) => id))],
    [],
  );

  const reset = useCallback(() => {
    chosen.current.clear();
    setQuery(null);
  }, []);

  const optionId = (i: number) => `${listId}-${i}`;
  const menu = open ? (
    <ul id={listId} className="cx-mention-menu list-unstyled" role="listbox" aria-label={t('mentions.suggestions')}>
      {matches.map((c, i) => (
        <li
          key={c.id}
          id={optionId(i)}
          role="option"
          aria-selected={i === active}
          className={`cx-mention-option${i === active ? ' is-active' : ''}`}
          onMouseDown={(e) => {
            e.preventDefault(); // keep the focus in the textarea
            pick(c);
          }}
          onMouseEnter={() => setActive(i)}
        >
          {c.user ? <Avatar user={c.user} size="sm" /> : <span className="cx-mention-at" aria-hidden="true">@</span>}
          <span className="text-truncate">
            {c.kind === 'user' ? c.label : `@${c.label}`}
            {c.kind !== 'user' && <span className="cx-meta"> — {t(`mentions.${c.kind}Hint`)}</span>}
          </span>
        </li>
      ))}
    </ul>
  ) : null;

  return {
    onInput,
    onKeyDown,
    inputProps: {
      role: 'combobox',
      'aria-autocomplete': 'list',
      'aria-expanded': open,
      'aria-controls': open ? listId : undefined,
      'aria-activedescendant': open ? optionId(Math.min(active, matches.length - 1)) : undefined,
    },
    menu,
    mentionIds,
    reset,
  };
}
