import { useCallback, useEffect, useMemo, useState } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type { ConversationSummaryResponse, MentionItemResponse, SavedItemResponse } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon, type NavIconName } from '../../shared/components/NavIcon';
import { formatDateTime } from '../../shared/format';
import { errorMessage } from '../auth/errors';
import { useOptionalAuth } from '../auth/AuthContext';
import { countUnreadMentions, listConversations, listMentions, listSaved, markRead, setSaved } from '../conversations/conversationsApi';
import { messageHref } from '../conversations/conversationParts';
import { RichText, toPlainText } from '../../shared/richText';
import { ReminderList, ScheduledList } from '../planning/PlanningLists';
import { listReminders, listScheduled, type ReminderResponse, type ScheduledMessageResponse } from '../planning/planningApi';
import { useRealtime } from '../realtime/RealtimeContext';

type Tab = 'mentions' | 'saved' | 'scheduled' | 'reminders';

/**
 * Activity (Phase 17 mentions, Phase 18 saved messages, Phase 20 scheduled messages and reminders;
 * redesigned to the owner's mockup 2026-11-24): a page header, tabs with counts, the mention cards
 * grouped by day with filters, and a "Coming up" column with the next reminder and scheduled message.
 */
export function MentionsPage() {
  const { t } = useTranslation();
  const { query } = useLocation();
  const requested = query.get('tab');
  const tab: Tab = requested === 'saved' || requested === 'scheduled' || requested === 'reminders' ? requested : 'mentions';
  const [counts, setCounts] = useState<Partial<Record<Tab, string | undefined>>>({});
  const [reminders, setReminders] = useState<ReminderResponse[]>([]);
  const [scheduled, setScheduled] = useState<ScheduledMessageResponse[]>([]);
  const [markAll, setMarkAll] = useState<(() => Promise<void>) | null>(null);
  // Stable, so the mentions list does not re-run its effect on every render.
  const onMarkAll = useCallback((action: (() => Promise<void>) | null) => setMarkAll(() => action), []);

  // Counts and the "Coming up" column; each part may fail on its own without hiding the page.
  const refresh = useCallback(() => {
    countUnreadMentions().then((n) => setCounts((c) => ({ ...c, mentions: n > 0 ? String(n) : undefined })), () => undefined);
    listSaved().then((p) => setCounts((c) => ({ ...c, saved: p.items.length > 0 ? `${p.items.length}${p.hasMore ? '+' : ''}` : undefined })), () => undefined);
    listScheduled().then((s) => {
      setScheduled(s);
      setCounts((c) => ({ ...c, scheduled: s.length > 0 ? String(s.length) : undefined }));
    }, () => undefined);
    listReminders().then((r) => {
      setReminders(r);
      setCounts((c) => ({ ...c, reminders: r.length > 0 ? String(r.length) : undefined }));
    }, () => undefined);
  }, []);
  useEffect(refresh, [refresh, tab]);

  const tabs: { key: Tab; label: string; icon: NavIconName; href: string }[] = [
    { key: 'mentions', label: t('mentions.title'), icon: 'at', href: '/mentions' },
    { key: 'saved', label: t('tools.savedTitle'), icon: 'bookmark', href: '/mentions?tab=saved' },
    { key: 'scheduled', label: t('planning.scheduledTitle'), icon: 'clock', href: '/mentions?tab=scheduled' },
    { key: 'reminders', label: t('planning.remindersTitle'), icon: 'bell', href: '/mentions?tab=reminders' },
  ];

  return (
    <div className="cx-activity">
      <header className="cx-page-header">
        <div>
          <nav aria-label={t('nav.label')} className="cx-breadcrumb">
            <span>{t('nav.workspace')}</span>
            <span aria-hidden="true">/</span>
            <span aria-current="page">{t('activity.title')}</span>
          </nav>
          <h1 className="cx-page-title mb-1">{t('activity.title')}</h1>
          <p className="cx-muted mb-0">{t('activity.subtitle')}</p>
        </div>
        {tab === 'mentions' && markAll && (
          <button type="button" className="btn btn-outline-secondary cx-icon-button" onClick={() => void markAll().then(refresh)}>
            <NavIcon name="check" size={18} />
            {t('activity.markAllRead')}
          </button>
        )}
      </header>

      <nav className="cx-tabs cx-activity-tabs" aria-label={t('mentions.tabs')}>
        {tabs.map((x) => (
          <Link key={x.key} to={x.href} className={`cx-tab${tab === x.key ? ' is-active' : ''}`} aria-current={tab === x.key ? 'page' : undefined}>
            <NavIcon name={x.icon} size={18} />
            {x.label}
            {counts[x.key] && <> <span className="cx-tab-count">{counts[x.key]}</span></>}
          </Link>
        ))}
      </nav>

      <div className="cx-activity-layout">
        <section className="cx-card cx-activity-main" aria-labelledby="activity-panel-title">
          <h2 id="activity-panel-title" className="cx-section-title">{tabs.find((x) => x.key === tab)!.label}</h2>
          {tab === 'saved' ? <SavedList /> : tab === 'scheduled' ? <ScheduledList /> : tab === 'reminders' ? <ReminderList /> : (
            <MentionList onMarkAll={onMarkAll} onChanged={refresh} />
          )}
        </section>
        <ComingUp reminders={reminders} scheduled={scheduled} />
      </div>
    </div>
  );
}

type Show = 'all' | 'unread';
type Order = 'newest' | 'oldest';

function MentionList({ onMarkAll, onChanged }: { onMarkAll: (action: (() => Promise<void>) | null) => void; onChanged: () => void }) {
  const { t, i18n } = useTranslation();
  const { client } = useRealtime();
  const me = useOptionalAuth()?.user ?? null;
  const [items, setItems] = useState<MentionItemResponse[] | null>(null);
  const [cursor, setCursor] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [organizations, setOrganizations] = useState<Map<string, string>>(new Map());
  const [savedIds, setSavedIds] = useState<Set<string>>(new Set());
  const [search, setSearch] = useState('');
  const [show, setShow] = useState<Show>('all');
  const [conversation, setConversation] = useState('');
  const [order, setOrder] = useState<Order>('newest');

  const load = useCallback(async () => {
    try {
      const page = await listMentions();
      setItems(page.items);
      setCursor(page.hasMore ? page.nextCursor : null);
      setSavedIds(new Set(page.items.filter((i) => i.entry.saved).map((i) => i.entry.id)));
    } catch (err) {
      setError(err);
    }
  }, []);

  useEffect(() => {
    void load();
    // Organization names for "in #customers · Iron Motors"; optional.
    listConversations().then(
      (all: ConversationSummaryResponse[]) => setOrganizations(new Map(all.filter((c) => c.organizationName).map((c) => [c.id, c.organizationName!]))),
      () => undefined,
    );
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

  // Reading a mention moves its conversation's read position up to that message.
  const read = async (item: MentionItemResponse) => {
    try {
      await markRead(item.conversationId, item.entry.sequence);
      await load();
      onChanged();
    } catch (err) {
      setError(err);
    }
  };

  const toggleSaved = async (item: MentionItemResponse) => {
    const saved = !savedIds.has(item.entry.id);
    try {
      await setSaved(item.conversationId, item.entry.id, saved);
      setSavedIds((current) => {
        const next = new Set(current);
        if (saved) next.add(item.entry.id);
        else next.delete(item.entry.id);
        return next;
      });
      onChanged();
    } catch (err) {
      setError(err);
    }
  };

  // "Mark all as read": each conversation with unread mentions, up to its newest unread mention.
  const unread = useMemo(() => (items ?? []).filter((i) => i.unread), [items]);
  useEffect(() => {
    if (unread.length === 0) {
      onMarkAll(null);
      return;
    }
    const newest = new Map<string, number>();
    unread.forEach((i) => newest.set(i.conversationId, Math.max(newest.get(i.conversationId) ?? 0, i.entry.sequence)));
    onMarkAll(async () => {
      try {
        await Promise.all([...newest].map(([id, sequence]) => markRead(id, sequence)));
        await load();
      } catch (err) {
        setError(err);
      }
    });
  }, [unread, onMarkAll, load]);

  const places = useMemo(() => {
    const seen = new Map<string, string>();
    (items ?? []).forEach((i) => seen.set(i.conversationId, placeName(i, t)));
    return [...seen];
  }, [items, t]);

  const needle = search.trim().toLocaleLowerCase(i18n.language);
  const visible = (items ?? [])
    .filter((i) => show === 'all' || i.unread)
    .filter((i) => !conversation || i.conversationId === conversation)
    .filter((i) => !needle || [toPlainText(i.entry.body), i.entry.actor?.displayName, i.conversationTitle].some((x) => x?.toLocaleLowerCase(i18n.language).includes(needle)))
    .sort((a, b) => (order === 'newest' ? -1 : 1) * (Date.parse(a.entry.createdAtUtc) - Date.parse(b.entry.createdAtUtc)));

  const groups: { day: string; label: string; items: MentionItemResponse[] }[] = [];
  visible.forEach((i) => {
    const day = new Date(i.entry.createdAtUtc).toDateString();
    const group = groups.find((g) => g.day === day);
    if (group) group.items.push(i);
    else groups.push({ day, label: dayHeading(i.entry.createdAtUtc, i18n.language, t), items: [i] });
  });

  return (
    <>
      <div className="cx-table-tools">
        <label className="cx-search-field">
          <NavIcon name="search" size={18} />
          <span className="visually-hidden">{t('activity.searchLabel')}</span>
          <input type="search" className="form-control" placeholder={t('activity.searchPlaceholder')} value={search} onChange={(e) => setSearch(e.target.value)} />
        </label>
        <div className="cx-table-selects">
          <div className="cx-segmented" role="group" aria-label={t('activity.showLabel')}>
            {(['all', 'unread'] as const).map((s) => (
              <button key={s} type="button" className={show === s ? 'is-active' : ''} aria-pressed={show === s} onClick={() => setShow(s)}>
                {t(`activity.show.${s}`)}
              </button>
            ))}
          </div>
          <label className="visually-hidden" htmlFor="activity-conversation">{t('activity.conversationLabel')}</label>
          <select id="activity-conversation" className="form-select" value={conversation} onChange={(e) => setConversation(e.target.value)}>
            <option value="">{t('activity.allConversations')}</option>
            {places.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
          </select>
          <label className="visually-hidden" htmlFor="activity-order">{t('activity.orderLabel')}</label>
          <select id="activity-order" className="form-select" value={order} onChange={(e) => setOrder(e.target.value as Order)}>
            <option value="newest">{t('activity.order.newest')}</option>
            <option value="oldest">{t('activity.order.oldest')}</option>
          </select>
        </div>
      </div>

      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {items === null ? (
        error === null && <p className="cx-muted">{t('auth.working')}</p>
      ) : items.length === 0 ? (
        <p className="cx-muted mb-0">{t('mentions.empty')}</p>
      ) : visible.length === 0 ? (
        <p className="cx-muted mb-0">{t('activity.noMatches')}</p>
      ) : (
        groups.map((group) => (
          <section key={group.day} className="cx-activity-day" aria-label={group.label}>
            <h3 className="cx-activity-day-title">{group.label}</h3>
            <ul className="list-unstyled cx-mention-list mb-0">
              {group.items.map((item) => (
                <li key={item.entry.id} className={`cx-mention-card${item.unread ? ' is-unread' : ''}`}>
                  {item.unread ? <span className="cx-unread-dot" role="img" aria-label={t('mentions.unread')} /> : <span className="cx-unread-dot is-read" aria-hidden="true" />}
                  {item.entry.actor && <Avatar user={item.entry.actor} size="sm" />}
                  <div className="cx-mention-content">
                    <div className="cx-mention-line">
                      <strong>{item.entry.actor?.displayName ?? t('conversations.someone')}</strong>
                      <span className="cx-muted">{t(item.conversationType === 'direct' ? 'activity.sentYouDirect' : 'activity.mentionedYouIn')}</span>
                      {item.conversationType !== 'direct' && <span className="cx-mention-place">{placeName(item, t)}</span>}
                      {organizations.get(item.conversationId) && <span className="cx-muted">· {organizations.get(item.conversationId)}</span>}
                      {item.entry.threadRootId && <span className="cx-muted">· {t('mentions.inThread')}</span>}
                      <time className="cx-mention-time" dateTime={item.entry.createdAtUtc} title={formatDateTime(item.entry.createdAtUtc, i18n.language)}>
                        {new Date(item.entry.createdAtUtc).toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' })}
                      </time>
                    </div>
                    <RichText
                      className="cx-mention-text"
                      text={item.entry.body ?? ''}
                      mentions={me ? { names: [me.displayName], self: me.displayName, everyone: item.entry.mentions?.everyone ?? false, here: item.entry.mentions?.here ?? false } : undefined}
                    />
                    {(item.entry.attachments ?? []).length > 0 && (
                      <div className="cx-mention-files">
                        {item.entry.attachments!.map((a) => (
                          <span key={a.id} className="cx-file-chip">
                            <NavIcon name="clip" size={14} />
                            {a.fileName}
                          </span>
                        ))}
                      </div>
                    )}
                    <div className="cx-mention-actions">
                      <button type="button" className="btn btn-link btn-sm cx-icon-button" onClick={() => navigate(messageHref(item.conversationId, item.entry))}>
                        {t('activity.openConversation')}
                        <NavIcon name="external" size={14} />
                      </button>
                      <button type="button" className={`cx-tool${savedIds.has(item.entry.id) ? ' is-on' : ''}`} aria-pressed={savedIds.has(item.entry.id)}
                        title={t(savedIds.has(item.entry.id) ? 'tools.unsave' : 'tools.save')} onClick={() => void toggleSaved(item)}>
                        <NavIcon name="bookmark" size={17} />
                        <span className="visually-hidden">{t(savedIds.has(item.entry.id) ? 'tools.unsave' : 'tools.save')}</span>
                      </button>
                      {item.unread && (
                        <button type="button" className="cx-tool" title={t('activity.markRead')} onClick={() => void read(item)}>
                          <NavIcon name="check" size={17} />
                          <span className="visually-hidden">{t('activity.markRead')}</span>
                        </button>
                      )}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          </section>
        ))
      )}
      {cursor ? (
        <div className="text-center mt-2">
          <button type="button" className="btn btn-sm btn-link" onClick={() => void loadOlder()}>
            {t('mentions.loadOlder')}
          </button>
        </div>
      ) : (
        items !== null && items.length > 0 && unread.length === 0 && (
          <p className="cx-activity-done">
            <NavIcon name="check" size={18} />
            {t('activity.upToDate')}
          </p>
        )
      )}
    </>
  );
}

/** "# customers", a group's name, or "Direct message". */
const placeName = (item: { conversationType: string; conversationTitle: string | null }, t: TFunction) =>
  item.conversationType === 'direct'
    ? t('mentions.direct')
    : item.conversationType === 'channel'
      ? `# ${item.conversationTitle ?? ''}`
      : item.conversationTitle ?? t('conversations.someone');

/** "Today · Nov 24", "Yesterday · Nov 23" or "Friday · Nov 20". */
function dayHeading(value: string, language: string, t: TFunction): string {
  const date = new Date(value);
  const today = new Date();
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);
  const short = date.toLocaleDateString(language, { month: 'short', day: 'numeric' });
  const name = date.toDateString() === today.toDateString() ? t('activity.today')
    : date.toDateString() === yesterday.toDateString() ? t('conversations.yesterday')
      : date.toLocaleDateString(language, { weekday: 'long' });
  return `${name} · ${short}`;
}

/** The next reminder and the next scheduled message. */
function ComingUp({ reminders, scheduled }: { reminders: ReminderResponse[]; scheduled: ScheduledMessageResponse[] }) {
  const { t, i18n } = useTranslation();
  const nextReminder = [...reminders].sort((a, b) => Date.parse(a.remindAtUtc) - Date.parse(b.remindAtUtc))[0];
  const nextScheduled = [...scheduled].filter((s) => s.status === 'pending').sort((a, b) => Date.parse(a.sendAtUtc) - Date.parse(b.sendAtUtc))[0];
  const when = (value: string) => {
    const date = new Date(value);
    const day = new Date(date).toDateString() === new Date().toDateString() ? t('activity.today')
      : new Date(date.getTime() - 86_400_000).toDateString() === new Date().toDateString() ? t('activity.tomorrow')
        : date.toLocaleDateString(i18n.language, { weekday: 'short', month: 'short', day: 'numeric' });
    return { day, time: date.toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' }) };
  };
  const placeOf = (type: string | null, title: string | null) => (type === 'channel' ? `# ${title ?? ''}` : title ?? t('mentions.direct'));

  return (
    <aside className="cx-card cx-coming-up" aria-labelledby="coming-up-title">
      <h2 id="coming-up-title" className="cx-coming-up-title">
        <NavIcon name="clock" size={20} />
        {t('activity.comingUp')}
      </h2>
      {!nextReminder && !nextScheduled && <p className="cx-muted">{t('activity.nothingComing')}</p>}
      {nextReminder && (
        <div className="cx-coming-card">
          <span className="cx-tag">{when(nextReminder.remindAtUtc).day}</span>
          <p className="cx-coming-name">{toPlainText(nextReminder.entry.body ?? '').slice(0, 60) || t('planning.remindersTitle')}</p>
          <p className="cx-muted mb-1">{when(nextReminder.remindAtUtc).time} · {placeOf(nextReminder.conversationType, nextReminder.conversationTitle)}</p>
          <button type="button" className="btn btn-link btn-sm p-0 cx-icon-button" onClick={() => navigate(messageHref(nextReminder.conversationId, nextReminder.entry))}>
            {t('activity.openReminder')}
            <NavIcon name="external" size={14} />
          </button>
        </div>
      )}
      {nextScheduled && (
        <div className="cx-coming-card">
          <p className="cx-coming-name cx-icon-button">
            <NavIcon name="calendar" size={18} />
            {t('activity.scheduledMessage')}
          </p>
          <p className="cx-muted mb-1">
            {when(nextScheduled.sendAtUtc).day}, {when(nextScheduled.sendAtUtc).time}
            <br />
            {placeOf(nextScheduled.conversationType, nextScheduled.conversationTitle)}
          </p>
          <Link to="/mentions?tab=scheduled" className="btn btn-link btn-sm p-0 cx-icon-button">
            {t('activity.viewMessage')}
            <NavIcon name="external" size={14} />
          </Link>
        </div>
      )}
      <p className="cx-coming-tip">
        <NavIcon name="at" size={20} />
        <span>
          <strong>{t('activity.tipTitle')}</strong>
          <br />
          {t('activity.tipText')}
        </span>
      </p>
    </aside>
  );
}

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
            <li key={item.entry.id} className="cx-mention-card">
              {item.entry.actor && <Avatar user={item.entry.actor} size="sm" />}
              <div className="cx-mention-content">
                <div className="cx-mention-line">
                  <strong>{item.entry.actor?.displayName ?? t('conversations.someone')}</strong>
                  <span className="cx-mention-place">{placeName(item, t)}</span>
                  <time className="cx-mention-time" dateTime={item.entry.createdAtUtc}>{formatDateTime(item.entry.createdAtUtc, i18n.language)}</time>
                </div>
                <span className="cx-mention-text">{toPlainText(item.entry.body) || t('attachments.count', { count: item.entry.attachments?.length ?? 0 })}</span>
                <div className="cx-mention-actions">
                  <button type="button" className="btn btn-link btn-sm cx-icon-button" onClick={() => navigate(messageHref(item.conversationId, item.entry))}>
                    {t('activity.openConversation')}
                    <NavIcon name="external" size={14} />
                  </button>
                  <button type="button" className="btn btn-sm btn-link" onClick={() => void unsave(item)}>
                    {t('tools.unsave')}
                  </button>
                </div>
              </div>
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
