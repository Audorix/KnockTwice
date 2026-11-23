import { useCallback, useEffect, useState } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type { MentionItemResponse, SavedItemResponse } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { formatDateTime } from '../../shared/format';
import { errorMessage } from '../auth/errors';
import { listMentions, listSaved, setSaved } from '../conversations/conversationsApi';
import { messageHref } from '../conversations/conversationParts';
import { toPlainText } from '../../shared/richText';
import { ReminderList, ScheduledList } from '../planning/PlanningLists';
import { useRealtime } from '../realtime/RealtimeContext';

/** Phase 17 (ADR-031): mentions; Phase 18 (ADR-032): saved messages in a second tab. */
export function MentionsPage() {
  const { t } = useTranslation();
  const { query } = useLocation();
  const requested = query.get('tab');
  const tab = requested === 'saved' || requested === 'scheduled' || requested === 'reminders' ? requested : 'mentions';
  const tabs = [
    { key: 'mentions', label: t('mentions.title'), href: '/mentions' },
    { key: 'saved', label: t('tools.savedTitle'), href: '/mentions?tab=saved' },
    { key: 'scheduled', label: t('planning.scheduledTitle'), href: '/mentions?tab=scheduled' },
    { key: 'reminders', label: t('planning.remindersTitle'), href: '/mentions?tab=reminders' },
  ];
  return (
    <section className="cx-card" aria-labelledby="mentions-title">
      <div className="cx-card-header">
        <h2 id="mentions-title" className="cx-card-title">
          {tabs.find((x) => x.key === tab)!.label}
        </h2>
      </div>
      <nav className="nav nav-pills cx-tabs mb-3" aria-label={t('mentions.tabs')}>
        {tabs.map((x) => (
          <Link key={x.key} to={x.href} className={`nav-link${tab === x.key ? ' active' : ''}`} aria-current={tab === x.key ? 'page' : undefined}>
            {x.label}
          </Link>
        ))}
      </nav>
      {tab === 'saved' ? <SavedList /> : tab === 'scheduled' ? <ScheduledList /> : tab === 'reminders' ? <ReminderList /> : <MentionList />}
    </section>
  );
}

function MentionList() {
  const { t, i18n } = useTranslation();
  const { client } = useRealtime();
  const [items, setItems] = useState<MentionItemResponse[] | null>(null);
  const [cursor, setCursor] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);

  const load = useCallback(async () => {
    try {
      const page = await listMentions();
      setItems(page.items);
      setCursor(page.hasMore ? page.nextCursor : null);
    } catch (err) {
      setError(err);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // New mentions and reading conversations change the list.
  useEffect(() => {
    if (!client) return;
    let pending: ReturnType<typeof setTimeout> | undefined;
    const soon = () => {
      clearTimeout(pending);
      pending = setTimeout(() => void load(), 500);
    };
    const offs = [client.onMessage(soon), client.onConversationUpdated(soon), client.onReconnected(soon)];
    return () => {
      clearTimeout(pending);
      offs.forEach((off) => off());
    };
  }, [client, load]);

  const loadOlder = async () => {
    if (!cursor) return;
    try {
      const page = await listMentions(cursor);
      setItems((current) => [...(current ?? []), ...page.items.filter((i) => !current?.some((c) => c.entry.id === i.entry.id))]);
      setCursor(page.hasMore ? page.nextCursor : null);
    } catch (err) {
      setError(err);
    }
  };

  return (
    <>
      <p className="cx-muted">{t('mentions.intro')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {items === null ? (
        error === null && <p className="cx-muted">{t('auth.working')}</p>
      ) : items.length === 0 ? (
        <p className="cx-muted mb-0">{t('mentions.empty')}</p>
      ) : (
        <ul className="list-unstyled cx-mention-list mb-0">
          {items.map((item) => (
            <li key={item.entry.id}>
              <button
                type="button"
                className={`cx-mention-item${item.unread ? ' is-unread' : ''}`}
                onClick={() => navigate(messageHref(item.conversationId, item.entry))}
              >
                {item.entry.actor && <Avatar user={item.entry.actor} size="sm" />}
                <span className="min-w-0 flex-grow-1">
                  <span className="d-flex gap-2 align-items-baseline flex-wrap">
                    <strong>{item.entry.actor?.displayName ?? t('conversations.someone')}</strong>
                    <span className="cx-meta text-truncate">
                      {place(item, t)}
                      {item.entry.threadRootId ? ` · ${t('mentions.inThread')}` : ''}
                    </span>
                    <time className="cx-meta ms-auto" dateTime={item.entry.createdAtUtc}>
                      {formatDateTime(item.entry.createdAtUtc, i18n.language)}
                    </time>
                  </span>
                  <span className="cx-mention-text">{toPlainText(item.entry.body)}</span>
                </span>
                {item.unread && (
                  <span className="cx-unread-dot" role="img" aria-label={t('mentions.unread')} />
                )}
              </button>
            </li>
          ))}
        </ul>
      )}
      {cursor && (
        <div className="text-center mt-2">
          <button type="button" className="btn btn-sm btn-link" onClick={() => void loadOlder()}>
            {t('mentions.loadOlder')}
          </button>
        </div>
      )}
    </>
  );
}

const place = (item: { conversationType: string; conversationTitle: string | null }, t: TFunction) =>
  item.conversationType === 'direct'
    ? t('mentions.direct')
    : item.conversationType === 'channel'
      ? `# ${item.conversationTitle ?? ''}`
      : item.conversationTitle ?? t('conversations.someone');

/** Phase 18: the caller's saved messages they can still read, most recently saved first. */
function SavedList() {
  const { t, i18n } = useTranslation();
  const [items, setItems] = useState<SavedItemResponse[] | null>(null);
  const [cursor, setCursor] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    listSaved().then(
      (page) => {
        setItems(page.items);
        setCursor(page.hasMore ? page.nextCursor : null);
      },
      setError,
    );
  }, []);

  const loadOlder = async () => {
    if (!cursor) return;
    try {
      const page = await listSaved(cursor);
      setItems((current) => [...(current ?? []), ...page.items]);
      setCursor(page.hasMore ? page.nextCursor : null);
    } catch (err) {
      setError(err);
    }
  };

  const unsave = async (item: SavedItemResponse) => {
    try {
      await setSaved(item.conversationId, item.entry.id, false);
      setItems((current) => current?.filter((i) => i.entry.id !== item.entry.id) ?? null);
    } catch (err) {
      setError(err);
    }
  };

  return (
    <>
      <p className="cx-muted">{t('tools.savedIntro')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {items === null ? (
        error === null && <p className="cx-muted">{t('auth.working')}</p>
      ) : items.length === 0 ? (
        <p className="cx-muted mb-0">{t('tools.savedEmpty')}</p>
      ) : (
        <ul className="list-unstyled cx-mention-list mb-0">
          {items.map((item) => (
            <li key={item.entry.id} className="d-flex align-items-start gap-2">
              <button type="button" className="cx-mention-item" onClick={() => navigate(messageHref(item.conversationId, item.entry))}>
                {item.entry.actor && <Avatar user={item.entry.actor} size="sm" />}
                <span className="min-w-0 flex-grow-1">
                  <span className="d-flex gap-2 align-items-baseline flex-wrap">
                    <strong>{item.entry.actor?.displayName ?? t('conversations.someone')}</strong>
                    <span className="cx-meta text-truncate">{place(item, t)}</span>
                    <time className="cx-meta ms-auto" dateTime={item.entry.createdAtUtc}>
                      {formatDateTime(item.entry.createdAtUtc, i18n.language)}
                    </time>
                  </span>
                  <span className="cx-mention-text">{toPlainText(item.entry.body) || t('attachments.count', { count: item.entry.attachments?.length ?? 0 })}</span>
                </span>
              </button>
              <button type="button" className="btn btn-sm btn-link mt-2" onClick={() => void unsave(item)}>
                {t('tools.unsave')}
              </button>
            </li>
          ))}
        </ul>
      )}
      {cursor && (
        <div className="text-center mt-2">
          <button type="button" className="btn btn-sm btn-link" onClick={() => void loadOlder()}>
            {t('tools.savedOlder')}
          </button>
        </div>
      )}
    </>
  );
}
