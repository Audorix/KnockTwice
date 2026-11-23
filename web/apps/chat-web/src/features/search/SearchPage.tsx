import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type { ConversationSummaryResponse, SearchResultResponse, UserSummary } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { formatDateTime } from '../../shared/format';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { getConversation, listConversations } from '../conversations/conversationsApi';
import { messageHref } from '../conversations/conversationParts';
import { toPlainText } from '../../shared/richText';
import { highlight, normalizeWord, queryTerms, searchMessages, type SearchFilters } from './searchApi';

/** Message search (Phase 14): every word matches as a prefix, accents and case ignored. */
export function SearchPage() {
  return (
    <RequireUser>
      <Search />
    </RequireUser>
  );
}

function Search() {
  const { t, i18n } = useTranslation();
  const { query: params } = useLocation();
  const submitted = params.get('q') ?? '';
  const [text, setText] = useState(submitted);
  const [filters, setFilters] = useState<SearchFilters>({});
  const [conversations, setConversations] = useState<ConversationSummaryResponse[]>([]);
  const [people, setPeople] = useState<UserSummary[]>([]);
  const [results, setResults] = useState<SearchResultResponse[] | null>(null);
  const [cursor, setCursor] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const terms = useMemo(() => queryTerms(submitted), [submitted]);

  useEffect(() => {
    listConversations().then(setConversations, () => undefined);
  }, []);

  // The sender filter offers the people of the chosen conversation.
  useEffect(() => {
    setPeople([]);
    if (!filters.conversationId) return;
    getConversation(filters.conversationId).then((detail) => setPeople(detail.participants.map((p) => p.user)), () => undefined);
  }, [filters.conversationId]);

  useEffect(() => {
    setText(submitted);
    if (submitted.trim().length < 2) {
      setResults(null);
      return;
    }
    const controller = new AbortController();
    setBusy(true);
    setError(null);
    searchMessages(submitted, filters, null, controller.signal)
      .then((page) => {
        setResults(page.items);
        setCursor(page.nextCursor);
      }, (err: unknown) => {
        if (!controller.signal.aborted) setError(err);
      })
      .finally(() => !controller.signal.aborted && setBusy(false));
    return () => controller.abort();
  }, [submitted, filters]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    navigate(`/search?q=${encodeURIComponent(text.trim())}`, { replace: true });
  };

  const more = async () => {
    if (!cursor) return;
    setBusy(true);
    try {
      const page = await searchMessages(submitted, filters, cursor);
      setResults((current) => [...(current ?? []), ...page.items]);
      setCursor(page.nextCursor);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
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
  const setFilter = (changes: { [K in keyof SearchFilters]?: SearchFilters[K] | undefined }) => setFilters((current) => {
    const next: Record<string, unknown> = { ...current, ...changes };
    for (const key of Object.keys(next)) if (next[key] === undefined || next[key] === '' || next[key] === false) delete next[key];
    return next as SearchFilters;
  });

  return (
    <section className="cx-search" aria-labelledby="search-title">
      <h1 id="search-title" className="cx-page-title">{t('search.title')}</h1>
      <form className="cx-search-form" role="search" onSubmit={submit}>
        <label htmlFor="search-query" className="visually-hidden">{t('search.label')}</label>
        <input
          id="search-query"
          className="form-control form-control-lg"
          type="search"
          autoFocus
          maxLength={200}
          placeholder={t('search.placeholder')}
          value={text}
          onChange={(e) => setText(e.target.value)}
        />
        <button type="submit" className="btn btn-primary" disabled={text.trim().length < 2}>{t('search.submit')}</button>
      </form>

      <fieldset className="cx-search-filters">
        <legend className="visually-hidden">{t('search.filters')}</legend>
        <label className="cx-search-filter">
          <span>{t('search.in')}</span>
          <select className="form-select form-select-sm" value={filters.conversationId ?? ''}
            onChange={(e) => setFilter({ conversationId: e.target.value || undefined, senderId: undefined })}>
            <option value="">{t('search.everywhere')}</option>
            {conversations.map((c) => <option key={c.id} value={c.id}>{c.title}</option>)}
          </select>
        </label>
        <label className="cx-search-filter">
          <span>{t('search.from')}</span>
          <select className="form-select form-select-sm" value={filters.senderId ?? ''} disabled={people.length === 0}
            onChange={(e) => setFilter({ senderId: e.target.value || undefined })}>
            <option value="">{filters.conversationId ? t('search.anyone') : t('search.chooseConversation')}</option>
            {people.map((p) => <option key={p.id} value={p.id}>{p.displayName}</option>)}
          </select>
        </label>
        <label className="cx-search-filter">
          <span>{t('search.after')}</span>
          <input className="form-control form-control-sm" type="date" value={filters.fromDate ?? ''} onChange={(e) => setFilter({ fromDate: e.target.value || undefined })} />
        </label>
        <label className="cx-search-filter">
          <span>{t('search.before')}</span>
          <input className="form-control form-control-sm" type="date" value={filters.toDate ?? ''} onChange={(e) => setFilter({ toDate: e.target.value || undefined })} />
        </label>
        <div className="form-check cx-search-filter">
          <input id="search-files" className="form-check-input" type="checkbox" checked={!!filters.hasFiles} onChange={(e) => setFilter({ hasFiles: e.target.checked })} />
          <label className="form-check-label" htmlFor="search-files">{t('search.hasFiles')}</label>
        </div>
      </fieldset>

      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      {jump.length > 0 && (
        <div className="cx-search-jump">
          <h2 className="cx-section-label">{t('search.conversations')}</h2>
          <ul className="list-unstyled d-flex flex-wrap gap-2 mb-0">
            {jump.map((c) => (
              <li key={c.id}>
                <Link to={`/conversations?id=${encodeURIComponent(c.id)}`} className="btn btn-sm btn-outline-secondary">{c.title}</Link>
              </li>
            ))}
          </ul>
        </div>
      )}

      {submitted.trim().length >= 2 && (
        <div aria-live="polite" aria-busy={busy}>
          <h2 className="cx-section-label">{t('search.messages')}</h2>
          {results === null ? (
            <p className="cx-muted">{t('auth.working')}</p>
          ) : results.length === 0 ? (
            <p className="cx-muted">{t('search.none', { query: submitted })}</p>
          ) : (
            <ol className="list-unstyled cx-search-results">
              {results.map(({ conversation, message }) => (
                <li key={message.id}>
                  <Link to={messageHref(conversation.id, message)} className="cx-search-result">
                    <span className="cx-search-result-head">
                      {message.actor && <Avatar user={message.actor} size="sm" />}
                      <strong>{message.actor?.displayName ?? t('conversations.someone')}</strong>
                      <span className="cx-meta">{t('search.inConversation', { name: conversation.title })}</span>
                      <time className="cx-meta ms-auto" dateTime={message.createdAtUtc}>{formatDateTime(message.createdAtUtc, i18n.language)}</time>
                    </span>
                    <span className="cx-search-result-body">
                      {highlight(toPlainText(message.body), terms).map((part, i) => (part.match ? <mark key={i}>{part.text}</mark> : <span key={i}>{part.text}</span>))}
                    </span>
                    {(message.attachments ?? []).length > 0 && (
                      <span className="cx-meta d-block">
                        {t('search.files')}{' '}
                        {(message.attachments ?? []).map((a, i) => (
                          <span key={a.id}>
                            {i > 0 && ', '}
                            {highlight(a.fileName, terms).map((part, j) => (part.match ? <mark key={j}>{part.text}</mark> : <span key={j}>{part.text}</span>))}
                          </span>
                        ))}
                      </span>
                    )}
                  </Link>
                </li>
              ))}
            </ol>
          )}
          {cursor && (
            <button type="button" className="btn btn-outline-primary" disabled={busy} onClick={() => void more()}>{t('search.more')}</button>
          )}
        </div>
      )}

      {submitted.trim().length < 2 && <p className="cx-muted">{t('search.hint')}</p>}
    </section>
  );
}
