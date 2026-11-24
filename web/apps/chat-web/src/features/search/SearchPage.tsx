import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type {
  ConversationDetailResponse, ConversationEntryResponse, ConversationSummaryResponse, MessageContextResponse, SearchCountsResponse,
  SearchResultResponse, UserSummary,
} from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon } from '../../shared/components/NavIcon';
import { formatDateTime } from '../../shared/format';
import { toPlainText } from '../../shared/richText';
import { formatBytes } from '../attachments/Attachments';
import { attachmentUrl } from '../attachments/attachmentsApi';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { listContacts } from '../contacts/contactsApi';
import { getConversation, listConversations, setSaved } from '../conversations/conversationsApi';
import { messageHref } from '../conversations/conversationParts';
import { getMessageContext, highlight, normalizeWord, queryTerms, searchMessages, type SearchFilters, type SearchOrder } from './searchApi';

/**
 * Message search (Phase 14; redesigned to the owner's mockup 2026-11-24): a search card with filters
 * and removable filter chips, result tabs with totals (all, messages, files), result cards with
 * highlighted words, and a "Message context" panel with the messages around the selected result.
 */
export function SearchPage() {
  return (
    <RequireUser>
      <Search />
    </RequireUser>
  );
}

type Tab = 'all' | 'messages' | 'files';
type Filters = Omit<SearchFilters, 'hasFiles'>;

const wideScreen = () => typeof window.matchMedia === 'function' && window.matchMedia('(min-width: 1200px)').matches;

function Search() {
  const { t, i18n } = useTranslation();
  const { query: params } = useLocation();
  const submitted = params.get('q') ?? '';
  const [text, setText] = useState(submitted);
  const [filters, setFilters] = useState<Filters>(() => {
    const conversationId = params.get('conversation');
    return conversationId ? { conversationId } : {};
  });
  const [tab, setTab] = useState<Tab>('all');
  const [order, setOrder] = useState<SearchOrder>('newest');
  const [conversations, setConversations] = useState<ConversationSummaryResponse[]>([]);
  const [people, setPeople] = useState<UserSummary[]>([]);
  const [results, setResults] = useState<SearchResultResponse[] | null>(null);
  const [counts, setCounts] = useState<SearchCountsResponse | null>(null);
  const [cursor, setCursor] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [selected, setSelected] = useState<SearchResultResponse | null>(null);
  const [tips, setTips] = useState(false);
  const terms = useMemo(() => queryTerms(submitted), [submitted]);
  const searching = submitted.trim().length >= 2;

  useEffect(() => {
    listConversations().then(setConversations, () => undefined);
  }, []);

  // "From" offers the people of the chosen conversation, or your contacts.
  useEffect(() => {
    let current = true;
    const load = filters.conversationId
      ? getConversation(filters.conversationId).then((detail) => detail.participants.map((p) => p.user))
      : listContacts().then((contacts) => contacts.map((c) => c.user));
    load.then((list) => current && setPeople([...list].sort((a, b) => a.displayName.localeCompare(b.displayName))), () => undefined);
    return () => {
      current = false;
    };
  }, [filters.conversationId]);

  useEffect(() => {
    setText(submitted);
    setSelected(null);
    if (!searching) {
      setResults(null);
      setCounts(null);
      return;
    }
    const controller = new AbortController();
    setBusy(true);
    setError(null);
    searchMessages(submitted, withTab(filters, tab), null, controller.signal, order)
      .then((page) => {
        setResults(page.items);
        setCursor(page.nextCursor);
        setCounts(page.counts ?? null);
        if (page.items.length > 0 && wideScreen()) setSelected(page.items[0]!);
      }, (err: unknown) => {
        if (!controller.signal.aborted) setError(err);
      })
      .finally(() => !controller.signal.aborted && setBusy(false));
    return () => controller.abort();
  }, [submitted, searching, filters, tab, order]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    navigate(`/search?q=${encodeURIComponent(text.trim())}`, { replace: true });
  };

  const clearText = () => {
    setText('');
    navigate('/search', { replace: true });
  };

  const more = async () => {
    if (!cursor) return;
    setBusy(true);
    try {
      const page = await searchMessages(submitted, withTab(filters, tab), cursor, undefined, order);
      setResults((current) => [...(current ?? []), ...page.items]);
      setCursor(page.nextCursor);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const toggleSaved = async (item: SearchResultResponse) => {
    const saved = !item.message.saved;
    try {
      await setSaved(item.conversation.id, item.message.id, saved);
      const update = (r: SearchResultResponse) => (r.message.id === item.message.id ? { ...r, message: { ...r.message, saved } } : r);
      setResults((current) => current?.map(update) ?? current);
      setSelected((current) => (current ? update(current) : current));
    } catch (err) {
      setError(err);
    }
  };

  // Quick jump: conversations whose name matches every word.
  const jump = terms.length === 0 ? [] : conversations
    .filter((c) => {
      const words = (c.title.match(/[\p{L}\p{N}]+/gu) ?? []).map(normalizeWord);
      return terms.every((term) => words.some((word) => word.startsWith(term)));
    })
    .slice(0, 6);

  // Empty values remove the filter.
  const setFilter = (changes: { [K in keyof Filters]?: Filters[K] | undefined }) => setFilters((current) => {
    const next: Record<string, unknown> = { ...current, ...changes };
    for (const key of Object.keys(next)) if (next[key] === undefined || next[key] === '') delete next[key];
    return next as Filters;
  });

  const chosen = conversations.find((c) => c.id === filters.conversationId);
  const sender = people.find((p) => p.id === filters.senderId);
  const dates = dateRange(filters.fromDate, filters.toDate, i18n.language, t);
  const hasFilters = Object.keys(filters).length > 0;
  const tabCount: Record<Tab, number | undefined> = counts
    ? { all: counts.total, messages: counts.total - counts.withFiles, files: counts.withFiles }
    : { all: undefined, messages: undefined, files: undefined };
  const total = tabCount[tab];

  return (
    <div className="cx-search-page">
      <header className="cx-page-header">
        <div>
          <nav aria-label={t('nav.label')} className="cx-breadcrumb">
            <span>{t('nav.workspace')}</span>
            <span aria-hidden="true">/</span>
            <span aria-current="page">{t('search.title')}</span>
          </nav>
          <h1 className="cx-page-title mb-1">{t('search.title')}</h1>
          <p className="cx-muted mb-0">{t('search.subtitle')}</p>
        </div>
      </header>

      <section className="cx-card cx-search-card" aria-label={t('search.label')}>
        <form className="cx-search-bar" role="search" onSubmit={submit}>
          <label className="cx-search-field cx-search-big">
            <NavIcon name="search" size={22} />
            <span className="visually-hidden">{t('search.label')}</span>
            <input
              className="form-control"
              type="text"
              inputMode="search"
              autoFocus
              maxLength={200}
              placeholder={t('search.placeholder')}
              value={text}
              onChange={(e) => setText(e.target.value)}
            />
            {text && (
              <button type="button" className="cx-search-clear" onClick={clearText} title={t('search.clear')}>
                <NavIcon name="close" size={18} />
                <span className="visually-hidden">{t('search.clear')}</span>
              </button>
            )}
          </label>
          <button type="submit" className="btn btn-primary cx-search-submit" disabled={text.trim().length < 2}>{t('search.submit')}</button>
        </form>

        <fieldset className="cx-search-filters">
          <legend className="visually-hidden">{t('search.filters')}</legend>
          <label className="cx-search-filter">
            <span>{t('search.conversation')}</span>
            <select className="form-select" value={filters.conversationId ?? ''}
              onChange={(e) => setFilter({ conversationId: e.target.value || undefined, senderId: undefined })}>
              <option value="">{t('search.everywhere')}</option>
              {conversations.map((c) => (
                <option key={c.id} value={c.id}>{placeOf(c, t)}{c.organizationName && !c.title.includes(c.organizationName) ? ` · ${c.organizationName}` : ''}</option>
              ))}
            </select>
          </label>
          <label className="cx-search-filter">
            <span>{t('search.from')}</span>
            <select className="form-select" value={filters.senderId ?? ''} onChange={(e) => setFilter({ senderId: e.target.value || undefined })}>
              <option value="">{t('search.anyone')}</option>
              {people.map((p) => <option key={p.id} value={p.id}>{p.displayName}</option>)}
            </select>
          </label>
          <DateFilter label={t('search.after')} value={filters.fromDate} onChange={(fromDate) => setFilter({ fromDate })} />
          <DateFilter label={t('search.before')} value={filters.toDate} onChange={(toDate) => setFilter({ toDate })} />
          <div className="form-check cx-search-check">
            <input id="search-files" className="form-check-input" type="checkbox" checked={tab === 'files'} onChange={(e) => setTab(e.target.checked ? 'files' : 'all')} />
            <label className="form-check-label" htmlFor="search-files">{t('search.hasFiles')}</label>
          </div>
        </fieldset>

        <div className="cx-search-chips">
          {chosen && <FilterChip label={placeOf(chosen, t)} onRemove={() => setFilter({ conversationId: undefined, senderId: undefined })} />}
          {sender && <FilterChip label={t('search.fromPerson', { name: sender.displayName })} onRemove={() => setFilter({ senderId: undefined })} />}
          {dates && <FilterChip label={dates} onRemove={() => setFilter({ fromDate: undefined, toDate: undefined })} />}
          {hasFilters && (
            <button type="button" className="btn btn-link btn-sm cx-search-clear-filters" onClick={() => setFilters({})}>{t('search.clearFilters')}</button>
          )}
          <button type="button" className="cx-tool cx-search-tips-button" aria-expanded={tips} onClick={() => setTips((v) => !v)} title={t('search.tips')}>
            <NavIcon name="info" size={19} />
            <span className="visually-hidden">{t('search.tips')}</span>
          </button>
        </div>
        {tips && <p className="cx-search-tips">{t('search.hint')}</p>}
      </section>

      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      {!searching ? (
        <p className="cx-muted mt-3">{t('search.hint')}</p>
      ) : (
        <>
          <div className="cx-search-tabs-row">
            <div className="cx-tabs" role="tablist" aria-label={t('search.resultTabs')}>
              {(['all', 'messages', 'files'] as const).map((key) => (
                <button key={key} type="button" role="tab" aria-selected={tab === key} className={`cx-tab${tab === key ? ' is-active' : ''}`} onClick={() => setTab(key)}>
                  {t(`search.tabs.${key}`)}
                  {tabCount[key] !== undefined && <> <span className="cx-tab-count">{tabCount[key]}</span></>}
                </button>
              ))}
            </div>
            <label className="visually-hidden" htmlFor="search-order">{t('search.orderLabel')}</label>
            <select id="search-order" className="form-select cx-search-order" value={order} onChange={(e) => setOrder(e.target.value as SearchOrder)}>
              <option value="newest">{t('activity.order.newest')}</option>
              <option value="oldest">{t('activity.order.oldest')}</option>
            </select>
          </div>

          {tab === 'all' && jump.length > 0 && (
            <div className="cx-search-jump">
              <span className="cx-muted">{t('search.conversations')}</span>
              {jump.map((c) => (
                <Link key={c.id} to={`/conversations?id=${encodeURIComponent(c.id)}`} className="cx-chip">{placeOf(c, t)}</Link>
              ))}
            </div>
          )}

          <div className={`cx-search-layout${selected ? ' has-context' : ''}`}>
            <div aria-live="polite" aria-busy={busy}>
              {results === null ? (
                error === null && <p className="cx-muted">{t('auth.working')}</p>
              ) : results.length === 0 ? (
                <p className="cx-muted">{t('search.none', { query: submitted })}</p>
              ) : (
                <>
                  <p className="cx-search-summary">
                    {total !== undefined ? t('search.summary', { count: total, query: submitted }) : t('search.summaryLoaded', { query: submitted })}
                    {chosen && <> {t('search.summaryIn', { name: placeOf(chosen, t) })}</>}
                  </p>
                  <ol className="list-unstyled cx-result-list">
                    {results.map((item) => (
                      <ResultCard
                        key={item.message.id}
                        item={item}
                        terms={terms}
                        asFile={tab === 'files' || (!item.message.body && (item.message.attachments ?? []).length > 0)}
                        selected={selected?.message.id === item.message.id}
                        onSelect={() => setSelected(item)}
                        onToggleSaved={() => void toggleSaved(item)}
                      />
                    ))}
                  </ol>
                  <div className="cx-search-footer">
                    <span className="cx-muted">
                      {total !== undefined ? t('search.showing', { shown: results.length, total }) : t('search.showingLoaded', { shown: results.length })}
                    </span>
                    {cursor && (
                      <button type="button" className="btn btn-outline-primary btn-sm" disabled={busy} onClick={() => void more()}>{t('search.more')}</button>
                    )}
                  </div>
                </>
              )}
            </div>
            {selected && <MessageContext item={selected} terms={terms} onClose={() => setSelected(null)} />}
          </div>
        </>
      )}
    </div>
  );
}

/** The files filter of the chosen result tab. */
function withTab(filters: Filters, tab: Tab): SearchFilters {
  return tab === 'all' ? filters : { ...filters, hasFiles: tab === 'files' };
}

/** "# customers" for a channel, the title otherwise. */
const placeOf = (c: Pick<ConversationSummaryResponse, 'type' | 'title'>, t: TFunction) =>
  c.type === 'channel' ? `# ${c.title}` : c.title || t('conversations.someone');

/** Where a result was said: like placeOf, but "Direct message" instead of repeating the other person's name. */
const resultPlace = (c: Pick<ConversationSummaryResponse, 'type' | 'title'>, t: TFunction) =>
  c.type === 'direct' ? t('mentions.direct') : placeOf(c, t);

/** "1–24 Nov 2026", "From 1 Nov 2026" or "Until 24 Nov 2026". */
function dateRange(from: string | undefined, to: string | undefined, language: string, t: TFunction): string | null {
  const format = new Intl.DateTimeFormat(language, { day: 'numeric', month: 'short', year: 'numeric' });
  const day = (value: string) => new Date(`${value}T00:00:00`);
  if (from && to) return format.formatRange(day(from), day(to));
  if (from) return t('search.sinceDate', { date: format.format(day(from)) });
  if (to) return t('search.untilDate', { date: format.format(day(to)) });
  return null;
}

/** "Today, 09:15", "Yesterday, 16:20" or "Nov 20, 14:32". */
function resultTime(value: string, language: string, t: TFunction): string {
  const date = new Date(value);
  const today = new Date();
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);
  const time = date.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' });
  if (date.toDateString() === today.toDateString()) return t('search.todayAt', { time });
  if (date.toDateString() === yesterday.toDateString()) return t('search.yesterdayAt', { time });
  const day = date.toLocaleDateString(language, { month: 'short', day: 'numeric', ...(date.getFullYear() !== today.getFullYear() ? { year: 'numeric' } : {}) });
  return `${day}, ${time}`;
}

function Marked({ text, terms }: { text: string; terms: string[] }) {
  return <>{highlight(text, terms).map((part, i) => (part.match ? <mark key={i}>{part.text}</mark> : <span key={i}>{part.text}</span>))}</>;
}

/**
 * A date field that applies its value only once the year is complete: while a year is typed the
 * browser reports 0002, 0020, … which must not start searches (or a "start after end" error).
 */
function DateFilter({ label, value, onChange }: { label: string; value: string | undefined; onChange: (value: string | undefined) => void }) {
  const [draft, setDraft] = useState(value ?? '');
  useEffect(() => setDraft(value ?? ''), [value]);
  return (
    <label className="cx-search-filter">
      <span>{label}</span>
      <input
        className="form-control"
        type="date"
        value={draft}
        onChange={(e) => {
          const next = e.target.value;
          setDraft(next);
          if (next === '') onChange(undefined);
          else if (Number(next.slice(0, 4)) >= 1900) onChange(next);
        }}
      />
    </label>
  );
}

function FilterChip({ label, onRemove }: { label: string; onRemove: () => void }) {
  const { t } = useTranslation();
  return (
    <span className="cx-filter-chip">
      {label}
      <button type="button" onClick={onRemove} title={t('search.removeFilter', { name: label })}>
        <NavIcon name="close" size={14} />
        <span className="visually-hidden">{t('search.removeFilter', { name: label })}</span>
      </button>
    </span>
  );
}

function ResultCard({ item, terms, asFile, selected, onSelect, onToggleSaved }: {
  item: SearchResultResponse;
  terms: string[];
  asFile: boolean;
  selected: boolean;
  onSelect: () => void;
  onToggleSaved: () => void;
}) {
  const { t, i18n } = useTranslation();
  const { conversation, message } = item;
  const files = message.attachments ?? [];
  const author = message.actor?.displayName ?? t('conversations.someone');
  const place = (
    <span className="cx-result-place">
      {resultPlace(conversation, t)}
      {conversation.organizationName && <> · {conversation.organizationName}</>}
    </span>
  );
  const body = toPlainText(message.body ?? '');
  const saved = !!message.saved;

  return (
    <li className={`cx-result-card${selected ? ' is-selected' : ''}`}>
      <button type="button" className="cx-result-main" aria-pressed={selected} onClick={onSelect}>
        {asFile && files.length > 0 ? (
          <span className="cx-result-file-icon" aria-hidden="true"><NavIcon name="file" size={24} /></span>
        ) : (
          message.actor && <Avatar user={message.actor} size="sm" />
        )}
        <span className="cx-result-content">
          <span className="cx-result-head">
            {asFile && files.length > 0 ? (
              <strong><Marked text={files.map((f) => f.fileName).join(', ')} terms={terms} /></strong>
            ) : (
              <strong>{author}</strong>
            )}
            <time dateTime={message.createdAtUtc} title={formatDateTime(message.createdAtUtc, i18n.language)}>{resultTime(message.createdAtUtc, i18n.language, t)}</time>
          </span>
          {asFile && files.length > 0 && <span className="cx-result-place">{t('search.sharedBy', { name: author })}</span>}
          {place}
          {body && <span className="cx-result-text"><Marked text={body} terms={terms} /></span>}
        </span>
      </button>
      <div className="cx-result-actions">
        {!asFile && files.length > 0 && (
          <span className="cx-result-files">
            {files.map((f) => (
              <span key={f.id} className="cx-file-chip">
                <NavIcon name="file" size={14} />
                <Marked text={f.fileName} terms={terms} />
                <span className="cx-muted">· {formatBytes(f.size, i18n.language)}</span>
              </span>
            ))}
          </span>
        )}
        <Link to={messageHref(conversation.id, message)} className="cx-result-link">
          {t('search.viewInConversation')}
          <NavIcon name="external" size={14} />
        </Link>
        <button type="button" className={`cx-tool${saved ? ' is-on' : ''}`} aria-pressed={saved} title={t(saved ? 'tools.unsave' : 'tools.save')} onClick={onToggleSaved}>
          <NavIcon name="bookmark" size={18} />
          <span className="visually-hidden">{t(saved ? 'tools.unsave' : 'tools.save')}</span>
        </button>
      </div>
    </li>
  );
}

/** The selected result with the messages around it (a side column; a sliding panel on smaller screens). */
function MessageContext({ item, terms, onClose }: { item: SearchResultResponse; terms: string[]; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const [context, setContext] = useState<MessageContextResponse | null>(null);
  const [detail, setDetail] = useState<ConversationDetailResponse | null>(null);
  const [error, setError] = useState<unknown>(null);
  const conversationId = item.conversation.id;
  const messageId = item.message.id;
  const panel = useRef<HTMLElement>(null);

  // As an overlay (narrow screens) the panel takes the focus, so Escape and screen readers work in it.
  useEffect(() => {
    if (!wideScreen()) panel.current?.focus();
  }, [messageId]);

  useEffect(() => {
    const controller = new AbortController();
    setContext(null);
    setError(null);
    getMessageContext(conversationId, messageId, 3, controller.signal).then(setContext, (err: unknown) => !controller.signal.aborted && setError(err));
    return () => controller.abort();
  }, [conversationId, messageId]);

  useEffect(() => {
    let current = true;
    getConversation(conversationId).then((d) => current && setDetail(d), () => undefined);
    return () => {
      current = false;
    };
  }, [conversationId]);

  const entries = (context?.items ?? []).filter((e) => e.kind === 'message');
  let lastDay = '';

  return (
    <>
      <div className="cx-search-context-backdrop" onClick={onClose} aria-hidden="true" />
      <aside ref={panel} tabIndex={-1} className="cx-card cx-search-context" aria-labelledby="search-context-title" onKeyDown={(e) => e.key === 'Escape' && onClose()}>
        <div className="cx-search-context-head">
          <h2 id="search-context-title">{t('search.context')}</h2>
          <button type="button" className="cx-tool" onClick={onClose} title={t('search.closeContext')}>
            <NavIcon name="close" size={18} />
            <span className="visually-hidden">{t('search.closeContext')}</span>
          </button>
        </div>
        <div className="cx-search-context-place">
          <strong>{item.conversation.type === 'direct' ? `${t('mentions.direct')} · ${item.conversation.title}` : placeOf(item.conversation, t)}</strong>
          <span className="cx-muted">
            {[item.conversation.organizationName, detail ? t('search.participants', { count: detail.participants.length }) : null].filter(Boolean).join(' · ')}
          </span>
        </div>
        {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        {context === null ? (
          error === null && <p className="cx-muted">{t('auth.working')}</p>
        ) : (
          <ol className="list-unstyled cx-context-list">
            {context.hasOlder && <li className="cx-context-more" aria-hidden="true">⋯</li>}
            {entries.map((entry) => {
              const day = new Date(entry.createdAtUtc).toDateString();
              const separator = day !== lastDay ? contextDay(entry.createdAtUtc, i18n.language, t) : null;
              lastDay = day;
              return (
                <ContextEntry key={entry.id} entry={entry} separator={separator} target={entry.id === messageId} terms={terms} />
              );
            })}
            {context.hasNewer && <li className="cx-context-more" aria-hidden="true">⋯</li>}
          </ol>
        )}
        <Link to={messageHref(conversationId, item.message)} className="btn btn-outline-primary cx-icon-button cx-search-context-open">
          {t('search.openConversation')}
          <NavIcon name="external" size={16} />
        </Link>
      </aside>
    </>
  );
}

function ContextEntry({ entry, separator, target, terms }: { entry: ConversationEntryResponse; separator: string | null; target: boolean; terms: string[] }) {
  const { t, i18n } = useTranslation();
  const text = toPlainText(entry.body ?? '');
  return (
    <>
      {separator && <li className="cx-day-separator" role="separator"><span>{separator}</span></li>}
      <li className={`cx-context-entry${target ? ' is-target' : ''}`} aria-current={target ? 'true' : undefined}>
        {entry.actor && <Avatar user={entry.actor} size="sm" />}
        <div className="cx-context-content">
          <div className="cx-context-head">
            <strong>{entry.actor?.displayName ?? t('conversations.someone')}</strong>
            <time dateTime={entry.createdAtUtc}>{new Date(entry.createdAtUtc).toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' })}</time>
          </div>
          {entry.deletedAtUtc ? (
            <p className="cx-muted fst-italic mb-0">{t('messages.deleted')}</p>
          ) : (
            text && <p className="cx-context-text">{target ? <Marked text={text} terms={terms} /> : text}</p>
          )}
          {!entry.deletedAtUtc && (entry.attachments ?? []).map((f) => (
            <a key={f.id} className="cx-context-file" href={attachmentUrl(f.id, true)} download={f.fileName}>
              <NavIcon name="file" size={18} />
              <span className="cx-context-file-name">{f.fileName}</span>
              <span className="cx-muted">· {formatBytes(f.size, i18n.language)}</span>
              <NavIcon name="download" size={18} />
              <span className="visually-hidden">{t('search.download', { name: f.fileName })}</span>
            </a>
          ))}
        </div>
      </li>
    </>
  );
}

/** "Today", "Yesterday" or "Friday, November 20". */
function contextDay(value: string, language: string, t: TFunction): string {
  const date = new Date(value);
  const today = new Date();
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);
  if (date.toDateString() === today.toDateString()) return t('search.today');
  if (date.toDateString() === yesterday.toDateString()) return t('search.yesterday');
  return date.toLocaleDateString(language, { weekday: 'long', month: 'long', day: 'numeric', ...(date.getFullYear() !== today.getFullYear() ? { year: 'numeric' } : {}) });
}
