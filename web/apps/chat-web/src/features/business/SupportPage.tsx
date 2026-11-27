import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { navigate, useLocation } from '../../app/router';
import type {
  AttachmentLimitsResponse,
  ConversationEntryResponse,
  OrganizationMemberResponse,
  OrganizationResponse,
  SupportInboxItemResponse,
  SupportInboxPageResponse,
  SupportNoteResponse,
  SupportStatus,
  UserSummary,
} from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon } from '../../shared/components/NavIcon';
import { RowMenu } from '../../shared/components/RowMenu';
import { RichText } from '../../shared/richText';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { acceptFor, AttachButton, AttachmentList, DraftFileList, formatBytes, useDraftFiles } from '../attachments/Attachments';
import * as files from '../attachments/attachmentsApi';
import { describeEntry } from '../conversations/conversationParts';
import * as conversations from '../conversations/conversationsApi';
import { applyFormat } from '../conversations/formatting';
import { ReactionPicker } from '../conversations/reactionsAndThreads';
import { listMembers, listOrganizations } from '../organizations/organizationsApi';
import { useRealtime } from '../realtime/RealtimeContext';
import * as api from './businessApi';

/** Organizations whose support inbox the user works: support agents and owners/administrators. */
export { supportOrganizations } from '../../app/navigationRules';
import { supportOrganizations } from '../../app/navigationRules';

type Tab = SupportStatus | 'all';
const tabs: Tab[] = ['open', 'pending', 'closed', 'all'];
type Assignment = api.SupportInboxFilters['assignment'];
type Sort = api.SupportInboxFilters['sort'];

const PAGE_SIZE = 20;
const wide = () => typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia('(min-width: 992px)').matches;

/**
 * The support inbox (Phase 11, redesigned 2026-11 from the owner's mockup): status tabs with
 * counts, search and filters, the conversation list beside the selected conversation, internal
 * notes for the team and a composer that answers the customer or adds a note.
 */
export function SupportPage() {
  return (
    <RequireUser>
      <Support />
    </RequireUser>
  );
}

function Support() {
  const { t } = useTranslation();
  const { client } = useRealtime();
  const { query } = useLocation();
  const [organizations, setOrganizations] = useState<OrganizationResponse[] | null>(null);
  const [members, setMembers] = useState<OrganizationMemberResponse[]>([]);
  const [tab, setTab] = useState<Tab>('open');
  const [search, setSearch] = useState('');
  const [applied, setApplied] = useState('');
  const [assignment, setAssignment] = useState<Assignment>('anyone');
  const [mine, setMine] = useState(false);
  const [sort, setSort] = useState<Sort>('oldest');
  const [page, setPage] = useState<SupportInboxPageResponse | null>(null);
  const [shown, setShown] = useState(PAGE_SIZE);
  const [error, setError] = useState<unknown>(null);
  // Without a choice in the address, the first organization whose inbox has anything opens.
  const [probe, setProbe] = useState(0);
  const organizationId = query.get('org') ?? organizations?.[probe]?.id ?? '';
  const selectedId = query.get('id');

  useEffect(() => {
    listOrganizations().then((all) => setOrganizations(supportOrganizations(all)), (err: unknown) => {
      setOrganizations([]);
      setError(err);
    });
  }, []);

  useEffect(() => {
    if (!organizationId) return;
    listMembers(organizationId).then((m) => setMembers(m.members), () => setMembers([]));
  }, [organizationId]);

  const go = useCallback(
    (changes: { org?: string; id?: string | null }) => {
      const next = new URLSearchParams();
      const org = changes.org ?? organizationId;
      if (org) next.set('org', org);
      const id = changes.id === undefined ? selectedId : changes.id;
      if (id) next.set('id', id);
      navigate(`/support?${next.toString()}`, { replace: true });
    },
    [organizationId, selectedId],
  );

  const reload = useCallback(() => {
    if (!organizationId) return;
    api.getSupportInbox(organizationId, { status: tab, mine, assignment, q: applied, sort, skip: 0, take: shown }).then(setPage, (err: unknown) => {
      setPage({ items: [], total: 0, counts: { open: 0, pending: 0, closed: 0, all: 0 } });
      setError(err);
    });
  }, [organizationId, tab, mine, assignment, applied, sort, shown]);

  useEffect(() => {
    reload();
    // Visitors' messages reach only the people taking part: refresh now and then for everyone else.
    const timer = setInterval(reload, 30000);
    return () => clearInterval(timer);
  }, [reload]);

  useEffect(() => {
    if (!client) return;
    const off = [client.onConversationUpdated(() => reload()), client.onReconnected(() => reload())];
    return () => off.forEach((o) => o());
  }, [client, reload]);

  useEffect(() => {
    if (!query.get('org') && page?.counts.all === 0 && organizations && probe < organizations.length - 1) {
      setPage(null);
      setProbe((p) => p + 1);
    }
  }, [query, page, organizations, probe]);

  // A new filter starts from the first page again.
  useEffect(() => setShown(PAGE_SIZE), [organizationId, tab, mine, assignment, applied, sort]);

  // On wide screens the first conversation opens by itself, as in the mockup.
  const items = page?.items ?? [];
  useEffect(() => {
    if (!selectedId && items.length > 0 && wide()) go({ id: items[0]!.conversation.conversationId });
  }, [selectedId, items, go]);

  const [selectedItem, setSelectedItem] = useState<SupportInboxItemResponse | null>(null);
  useEffect(() => {
    const found = items.find((i) => i.conversation.conversationId === selectedId);
    if (found) setSelectedItem(found);
    else if (!selectedId) setSelectedItem(null);
  }, [items, selectedId]);

  const agents = useMemo(
    () => members.filter((m) => m.supportAgent || m.role === 'owner' || m.role === 'administrator').map((m) => m.user),
    [members],
  );
  const memberIds = useMemo(() => new Set(members.map((m) => m.user.id)), [members]);
  const counts = page?.counts;

  return (
    <div className={`cx-support${selectedId ? ' has-selection' : ''}`}>
      <header className="cx-page-header">
        <div>
          <nav aria-label={t('nav.label')} className="cx-breadcrumb">
            <span>{t('nav.workspace')}</span>
            <span aria-hidden="true">/</span>
            <span aria-current="page">{t('support.crumb')}</span>
          </nav>
          <h1 className="cx-page-title mb-1">{t('support.title')}</h1>
          <p className="cx-muted mb-0">{t('support.subtitle')}</p>
        </div>
        {organizations && organizations.length > 0 && (
          <label className="cx-support-org">
            <NavIcon name="building" size={20} />
            <span className="visually-hidden">{t('channels.organization')}</span>
            <select className="form-select" value={organizationId} onChange={(e) => go({ org: e.target.value, id: null })}>
              {organizations.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
            </select>
          </label>
        )}
      </header>

      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      {organizations?.length === 0 ? (
        <section className="cx-card">
          <p className="cx-muted mb-0">{t('support.noAccess')}</p>
        </section>
      ) : (
        <>
          <div className="cx-tabs" role="tablist" aria-label={t('support.tabsLabel')}>
            {tabs.map((x) => (
              <button
                key={x}
                id={`support-tab-${x}`}
                type="button"
                role="tab"
                aria-selected={tab === x}
                aria-controls="support-panel"
                tabIndex={tab === x ? 0 : -1}
                className={`cx-tab${tab === x ? ' is-active' : ''}`}
                onClick={() => setTab(x)}
                onKeyDown={(e) => moveTab(e, x, setTab)}
              >
                {t(`support.tabs.${x}`)}
                {counts && (x === 'open' || x === 'pending') && <> <span className="cx-tab-count">{counts[x]}</span></>}
              </button>
            ))}
          </div>

          <form
            className="cx-support-filters"
            role="search"
            onSubmit={(e) => {
              e.preventDefault();
              setApplied(search);
            }}
          >
            <label className="cx-search-field">
              <NavIcon name="search" size={18} />
              <span className="visually-hidden">{t('support.searchLabel')}</span>
              <input
                type="search"
                className="form-control"
                placeholder={t('support.searchPlaceholder')}
                value={search}
                onChange={(e) => {
                  setSearch(e.target.value);
                  if (!e.target.value) setApplied('');
                }}
              />
            </label>
            <span className="cx-support-divider" aria-hidden="true" />
            <button type="button" className={`cx-support-queue${assignment === 'unassigned' && tab === 'open' ? ' is-active' : ''}`}
              aria-label={`${t('support.queue')} ${counts?.unassignedOpen ?? 0}`}
              aria-pressed={assignment === 'unassigned' && tab === 'open'}
              onClick={() => {
                if (assignment === 'unassigned' && tab === 'open') setAssignment('anyone');
                else {
                  setTab('open');
                  setAssignment('unassigned');
                  setMine(false);
                }
              }}>
              <NavIcon name="headset" size={16} />
              <span>{t('support.queue')}</span>
              <span className="cx-support-queue-count">{counts?.unassignedOpen ?? 0}</span>
            </button>
            <span className="cx-support-divider" aria-hidden="true" />
            <label className="visually-hidden" htmlFor="support-assignment">{t('support.assignment.label')}</label>
            <select id="support-assignment" className="form-select cx-support-select" value={assignment} onChange={(e) => setAssignment(e.target.value as Assignment)}>
              {(['anyone', 'unassigned', 'assigned'] as const).map((a) => <option key={a} value={a}>{t(`support.assignment.${a}`)}</option>)}
            </select>
            <div className="form-check cx-support-mine">
              <input id="support-mine" className="form-check-input" type="checkbox" checked={mine} onChange={(e) => setMine(e.target.checked)} />
              <label className="form-check-label" htmlFor="support-mine">{t('support.mine')}</label>
            </div>
            <span className="cx-support-spacer" />
            <label className="visually-hidden" htmlFor="support-sort">{t('support.sort.label')}</label>
            <select id="support-sort" className="form-select cx-support-select" value={sort} onChange={(e) => setSort(e.target.value as Sort)}>
              {(['oldest', 'newest'] as const).map((s) => <option key={s} value={s}>{t(`support.sort.${s}`)}</option>)}
            </select>
          </form>

          <div className="cx-support-body" role="tabpanel" id="support-panel" aria-labelledby={`support-tab-${tab}`}>
            <section className="cx-support-list" aria-labelledby="support-list-title">
              <h2 id="support-list-title" className="cx-support-list-title">
                {page ? t(`support.listTitle.${tab}`, { count: page.total }) : t('support.title')}
              </h2>
              {page === null ? (
                <p className="cx-muted cx-support-empty">{t('auth.working')}</p>
              ) : items.length === 0 ? (
                <p className="cx-muted cx-support-empty">{applied || mine || assignment !== 'anyone' ? t('support.noMatches') : t(`support.empty.${tab}`)}</p>
              ) : (
                <ul className="list-unstyled cx-support-items mb-0">
                  {items.map((item) => (
                    <SupportListItem key={item.conversation.conversationId} item={item} selected={item.conversation.conversationId === selectedId}
                      onOpen={() => go({ id: item.conversation.conversationId })}
                      onClaim={async (conversationId) => {
                        try {
                          await api.claimSupportConversation(conversationId);
                          reload();
                        } catch (err) {
                          setError(err);
                        }
                      }} />
                  ))}
                </ul>
              )}
              {page && page.total > 0 && (
                <div className="cx-support-list-foot">
                  <span className="cx-muted">{t('support.showing', { shown: items.length, total: page.total })}</span>
                  {items.length < page.total && (
                    <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setShown((s) => s + PAGE_SIZE)}>
                      {t('support.loadMore')}
                    </button>
                  )}
                </div>
              )}
            </section>

            <section className="cx-support-detail" aria-label={t('support.conversationLabel')}>
              {selectedId && selectedItem ? (
                <SupportConversation key={selectedId} item={selectedItem} agents={agents} memberIds={memberIds}
                  onBack={() => go({ id: null })} onChanged={reload} onError={setError} />
              ) : (
                <div className="cx-support-placeholder">
                  <NavIcon name="headset" size={36} />
                  <p className="cx-muted mb-0">{selectedId ? t('auth.working') : t('support.choose')}</p>
                </div>
              )}
            </section>
          </div>
        </>
      )}
    </div>
  );
}

function SupportListItem({ item, selected, onOpen, onClaim }: {
  item: SupportInboxItemResponse;
  selected: boolean;
  onOpen: () => void;
  onClaim: (conversationId: string) => Promise<void>;
}) {
  const { t, i18n } = useTranslation();
  const { user } = useAuth();
  const [claiming, setClaiming] = useState(false);
  const c = item.conversation;
  const customer = customerOf(c.conversationId, c.visitorName);
  const at = item.lastMessage?.sentAtUtc ?? c.lastActivityAtUtc;
  return (
    <li className="cx-support-list-row">
      <button type="button" className={`cx-support-item${selected ? ' is-selected' : ''}`} aria-current={selected ? 'true' : undefined} onClick={onOpen}>
        <Avatar user={customer} />
        <span className="cx-support-item-main">
          <span className="cx-support-item-top">
            <span className="cx-support-item-name">{c.visitorName}</span>
            <time className="cx-meta" dateTime={at} title={new Date(at).toLocaleString(i18n.language)}>{ago(at, t, i18n.language)}</time>
            {c.unreadCount > 0 && <span className="cx-support-unread"><span className="visually-hidden">{t('support.unread', { count: c.unreadCount })}</span></span>}
          </span>
          <span className="cx-support-item-title">{item.title ?? t('support.untitled')}</span>
          {item.lastMessage && (
            <span className="cx-support-item-preview">{item.lastMessage.text || t('support.files')}</span>
          )}
          <span className="cx-support-chips">
            <span className="cx-support-chip">
              <NavIcon name={channelIcon(c.widgetName)} size={14} />
              {c.widgetName ?? t('support.website')}
            </span>
            <span className="cx-support-chip">
              <NavIcon name="user" size={14} />
              {c.assignedTo ? (c.assignedTo.id === user?.id ? t('support.assignedToYou') : c.assignedTo.displayName) : t('support.unassigned')}
            </span>
          </span>
        </span>
      </button>
      {c.status === 'open' && !c.assignedTo && (
        <button
          type="button"
          className="cx-support-claim"
          aria-label={t('support.claimNamed', { name: c.visitorName })}
          disabled={claiming}
          onClick={async (event) => {
            event.stopPropagation();
            setClaiming(true);
            await onClaim(c.conversationId);
            setClaiming(false);
          }}
        >
          <NavIcon name="user" size={15} />
          {t('support.claim')}
        </button>
      )}
    </li>
  );
}

type Line =
  | { kind: 'message'; at: string; key: string; entry: ConversationEntryResponse }
  | { kind: 'event'; at: string; key: string; entry: ConversationEntryResponse }
  | { kind: 'note'; at: string; key: string; note: SupportNoteResponse };

function SupportConversation({
  item,
  agents,
  memberIds,
  onBack,
  onChanged,
  onError,
}: {
  item: SupportInboxItemResponse;
  agents: UserSummary[];
  memberIds: Set<string>;
  onBack: () => void;
  onChanged: () => void;
  onError: (error: unknown) => void;
}) {
  const { t, i18n } = useTranslation();
  const { user } = useAuth();
  const c = item.conversation;
  const id = c.conversationId;
  const [entries, setEntries] = useState<ConversationEntryResponse[] | null>(null);
  const [cursor, setCursor] = useState<string | null>(null);
  const [notes, setNotes] = useState<SupportNoteResponse[]>([]);
  const [editingSubject, setEditingSubject] = useState(false);
  const [subject, setSubject] = useState(c.subject ?? '');
  const participating = useRef(c.participating);
  participating.current = participating.current || c.participating;
  const timeline = useRef<HTMLDivElement>(null);
  const { client } = useRealtime();

  const load = useCallback(async () => {
    const [history, list] = await Promise.all([api.getSupportHistory(id), api.listSupportNotes(id)]);
    setEntries(history.items);
    setCursor(history.hasMore ? history.nextCursor : null);
    setNotes(list);
  }, [id]);

  useEffect(() => {
    load().catch(onError);
  }, [load, onError]);

  useEffect(() => {
    if (!client) return;
    const off = [
      client.onMessage((_type, event) => {
        if (event.conversationId === id) void load().catch(() => undefined);
      }),
      client.onConversationUpdated((event) => {
        if (event.conversationId === id) void load().catch(() => undefined);
      }),
    ];
    return () => off.forEach((o) => o());
  }, [client, id, load]);

  // Reading it as someone taking part clears its unread count.
  const lastSequence = entries?.at(-1)?.sequence ?? 0;
  useEffect(() => {
    if (!participating.current || c.unreadCount === 0 || lastSequence === 0) return;
    conversations.markRead(id, lastSequence).then(onChanged, () => undefined);
  }, [id, lastSequence, c.unreadCount, onChanged]);

  // Newest at the bottom, as in a chat.
  useEffect(() => {
    const box = timeline.current;
    if (box) box.scrollTop = box.scrollHeight;
  }, [entries?.length, notes.length]);

  const lines = useMemo<Line[]>(() => {
    const all: Line[] = [];
    for (const entry of entries ?? []) {
      if (entry.kind === 'message') {
        if (entry.threadRootId && !entry.shownInConversation) continue;
        all.push({ kind: 'message', at: entry.createdAtUtc, key: entry.id, entry });
      } else if (entry.kind === 'assigned' || entry.kind === 'status_changed' || entry.kind === 'member_added') {
        all.push({ kind: 'event', at: entry.createdAtUtc, key: entry.id, entry });
      }
    }
    for (const note of notes) all.push({ kind: 'note', at: note.createdAtUtc, key: note.id, note });
    return all.sort((a, b) => a.at.localeCompare(b.at));
  }, [entries, notes]);

  const ensureJoined = useCallback(async () => {
    if (participating.current) return;
    await api.joinSupportConversation(id);
    participating.current = true;
  }, [id]);

  const act = async (action: () => Promise<unknown>) => {
    try {
      await action();
      onChanged();
      await load();
    } catch (err) {
      onError(err);
    }
  };

  const setStatus = (status: SupportStatus) => act(() => api.setSupportStatus(id, status));
  const assignees = agents.some((a) => a.id === c.assignedTo?.id) || !c.assignedTo ? agents : [...agents, c.assignedTo];
  const title = c.subject ?? item.title ?? t('support.untitled');
  const channel = c.widgetName ?? t('support.website');
  const customerFirst = c.visitorName.split(' ')[0] ?? c.visitorName;

  let lastDay = '';
  return (
    <div className="cx-support-conversation">
      <header className="cx-support-head">
        <button type="button" className="btn btn-link cx-support-back" onClick={onBack}>
          <NavIcon name="back" size={18} />
          <span className="visually-hidden">{t('support.back')}</span>
        </button>
        <div className="cx-support-head-text">
          {editingSubject ? (
            <form
              className="cx-support-subject-form"
              onSubmit={(e) => {
                e.preventDefault();
                setEditingSubject(false);
                void act(() => api.setSupportSubject(id, subject.trim() || null));
              }}
            >
              <label className="visually-hidden" htmlFor="support-subject">{t('support.subject')}</label>
              <input id="support-subject" className="form-control" maxLength={120} value={subject} onChange={(e) => setSubject(e.target.value)} autoFocus />
              <button type="submit" className="btn btn-primary btn-sm">{t('support.saveSubject')}</button>
              <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setEditingSubject(false)}>{t('support.cancel')}</button>
            </form>
          ) : (
            <h2 className="cx-support-subject">{title}</h2>
          )}
          <p className="cx-muted mb-0">{c.visitorName} · {channel}</p>
        </div>
        <div className="cx-support-head-actions">
          {c.status === 'closed' ? (
            <button type="button" className="btn btn-outline-secondary cx-icon-button" onClick={() => void setStatus('open')}>
              <NavIcon name="refresh" size={17} />
              {t('support.reopen')}
            </button>
          ) : (
            <button type="button" className="btn btn-outline-secondary cx-icon-button" onClick={() => void setStatus('closed')}>
              <NavIcon name="check" size={17} />
              {t('support.markResolved')}
            </button>
          )}
          <RowMenu
            label={t('support.more', { name: c.visitorName })}
            items={[
              ...(c.status === 'open' ? [{ icon: 'clock' as const, label: t('support.markPending'), onSelect: () => void setStatus('pending') }] : []),
              ...(c.status === 'pending' ? [{ icon: 'refresh' as const, label: t('support.markOpen'), onSelect: () => void setStatus('open') }] : []),
              { icon: 'edit' as const, label: t('support.editSubject'), onSelect: () => { setSubject(c.subject ?? ''); setEditingSubject(true); } },
              ...(c.assignedTo?.id !== user?.id
                ? [{ icon: 'user' as const, label: t('support.claim'), onSelect: () => void act(() => api.claimSupportConversation(id)) }]
                : []),
              ...(c.participating
                ? [{ icon: 'chats' as const, label: t('support.openInChats'), onSelect: () => navigate(`/conversations?id=${encodeURIComponent(id)}`) }]
                : []),
            ]}
          />
        </div>
      </header>

      <div className="cx-support-state">
        <span className={`cx-support-status is-${c.status}`}>{t(`support.statuses.${c.status}`)}</span>
        <span className="cx-support-state-divider" aria-hidden="true" />
        <label className="cx-support-assignee">
          {c.assignedTo ? <Avatar user={c.assignedTo} size="sm" /> : <span className="cx-support-nobody"><NavIcon name="user" size={14} /></span>}
          <span className="visually-hidden">{t('support.assignee')}</span>
          <select className="form-select" value={c.assignedTo?.id ?? ''} onChange={(e) => void act(() => api.assignSupportConversation(id, e.target.value || null))}>
            <option value="">{t('support.unassigned')}</option>
            {assignees.map((a) => (
              <option key={a.id} value={a.id}>{a.id === user?.id ? t('support.you', { name: a.displayName }) : a.displayName}</option>
            ))}
          </select>
        </label>
        <span className="cx-support-state-divider" aria-hidden="true" />
        <span className="cx-meta">{t('support.started', { when: dayAndTime(c.createdAtUtc, t, i18n.language) })}</span>
      </div>

      <div className="cx-support-timeline" ref={timeline} aria-live="polite">
        {entries === null ? (
          <p className="cx-muted text-center my-4">{t('auth.working')}</p>
        ) : (
          <>
            {cursor && (
              <div className="text-center mb-2">
                <button
                  type="button"
                  className="btn btn-outline-secondary btn-sm"
                  onClick={() =>
                    void api.getSupportHistory(id, cursor).then((older) => {
                      setEntries((current) => [...older.items, ...(current ?? [])]);
                      setCursor(older.hasMore ? older.nextCursor : null);
                    }, onError)
                  }
                >
                  {t('support.earlier')}
                </button>
              </div>
            )}
            {lines.map((line) => {
              const day = new Date(line.at).toDateString();
              const separator = day !== lastDay ? <DaySeparator key={`d-${day}`} at={line.at} /> : null;
              lastDay = day;
              return (
                <div key={line.key}>
                  {separator}
                  {line.kind === 'note' ? (
                    <NoteCard note={line.note} />
                  ) : line.kind === 'event' ? (
                    <p className="cx-support-event">{describeEntry(line.entry, 'website', t)}</p>
                  ) : (
                    <MessageLine entry={line.entry} fromCustomer={!!line.entry.actor && !memberIds.has(line.entry.actor.id) && memberIds.size > 0} />
                  )}
                </div>
              );
            })}
          </>
        )}
      </div>

      <Composer conversationId={id} customer={customerFirst} ensureJoined={ensureJoined}
        onSent={() => {
          onChanged();
          void load().catch(onError);
        }}
        onError={onError} />
    </div>
  );
}

function DaySeparator({ at }: { at: string }) {
  const { t, i18n } = useTranslation();
  return (
    <div className="cx-support-day" role="separator">
      <span>{dayLabel(at, t, i18n.language)}</span>
    </div>
  );
}

function MessageLine({ entry, fromCustomer }: { entry: ConversationEntryResponse; fromCustomer: boolean }) {
  const { t, i18n } = useTranslation();
  const time = new Date(entry.createdAtUtc).toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' });
  const deleted = !!entry.deletedAtUtc;
  const body = deleted ? <em className="cx-muted">{t('messages.deleted')}</em> : entry.body ? <RichText text={entry.body} /> : null;
  const attachments = <AttachmentList items={entry.attachments ?? []} urlFor={(a, download) => files.attachmentUrl(a.id, download)} />;
  if (fromCustomer || !entry.actor) {
    return (
      <div className="cx-support-msg is-customer">
        {entry.actor && <Avatar user={entry.actor} />}
        <div className="cx-support-msg-body">
          <div className="cx-support-msg-meta">
            <strong>{entry.actor?.displayName ?? t('conversations.someone')}</strong>
            <time className="cx-meta" dateTime={entry.createdAtUtc}>{time}</time>
          </div>
          {body && <div className="cx-support-bubble">{body}</div>}
          {attachments}
        </div>
      </div>
    );
  }
  return (
    <div className="cx-support-msg is-team">
      <div className="cx-support-msg-body">
        <div className="cx-support-msg-meta">
          <span className="cx-meta">{entry.actor.displayName}</span>
          <time className="cx-meta" dateTime={entry.createdAtUtc}>{time}</time>
        </div>
        {body && <div className="cx-support-bubble">{body}</div>}
        {attachments}
      </div>
    </div>
  );
}

function NoteCard({ note }: { note: SupportNoteResponse }) {
  const { t, i18n } = useTranslation();
  const time = new Date(note.createdAtUtc).toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' });
  return (
    <div className="cx-support-note">
      <div className="cx-support-note-head">
        <NavIcon name="lock" size={15} />
        <strong>{t('support.note')}</strong>
        <span aria-hidden="true">·</span>
        <strong>{note.author?.displayName ?? t('conversations.someone')}</strong>
        <span aria-hidden="true">·</span>
        <time dateTime={note.createdAtUtc}>{time}</time>
      </div>
      <RichText text={note.body} className="cx-support-note-body" />
    </div>
  );
}

function Composer({
  conversationId,
  customer,
  ensureJoined,
  onSent,
  onError,
}: {
  conversationId: string;
  customer: string;
  ensureJoined: () => Promise<void>;
  onSent: () => void;
  onError: (error: unknown) => void;
}) {
  const { t, i18n } = useTranslation();
  const [mode, setMode] = useState<'reply' | 'note'>('reply');
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);
  const [limits, setLimits] = useState<AttachmentLimitsResponse | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const box = useRef<HTMLTextAreaElement>(null);
  const upload = useCallback(
    async (file: File) => {
      await ensureJoined();
      return files.uploadAttachment(conversationId, file);
    },
    [conversationId, ensureJoined],
  );
  const drafts = useDraftFiles(upload, files.deleteAttachment, files.getAttachment);

  useEffect(() => {
    files.getLimits().then(setLimits, () => setLimits(null));
  }, []);

  const format = (action: 'bold' | 'italic' | 'list') => {
    const area = box.current;
    const next = applyFormat(action, text, area?.selectionStart ?? text.length, area?.selectionEnd ?? text.length);
    setText(next.value);
    requestAnimationFrame(() => {
      area?.focus();
      area?.setSelectionRange(next.start, next.end);
    });
  };

  const insert = (value: string) => {
    const area = box.current;
    const start = area?.selectionStart ?? text.length;
    const end = area?.selectionEnd ?? text.length;
    setText(text.slice(0, start) + value + text.slice(end));
    requestAnimationFrame(() => {
      area?.focus();
      area?.setSelectionRange(start + value.length, start + value.length);
    });
  };

  const canSend = mode === 'note' ? !!text.trim() : !drafts.busy && (!!text.trim() || drafts.readyIds.length > 0);
  const submit = async () => {
    if (!canSend || busy) return;
    setBusy(true);
    try {
      if (mode === 'note') {
        await api.addSupportNote(conversationId, text);
      } else {
        await ensureJoined();
        await conversations.sendMessage(conversationId, { clientMessageId: crypto.randomUUID(), body: text, attachmentIds: drafts.readyIds });
        drafts.clear();
      }
      setText('');
      onSent();
    } catch (err) {
      onError(err);
    } finally {
      setBusy(false);
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      void submit();
    }
  };

  return (
    <form
      className={`cx-support-composer${mode === 'note' ? ' is-note' : ''}`}
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
    >
      <div className="cx-support-composer-tabs" role="tablist" aria-label={t('support.composerLabel')}>
        {(['reply', 'note'] as const).map((m) => (
          <button key={m} type="button" role="tab" aria-selected={mode === m} className={`cx-support-composer-tab${mode === m ? ' is-active' : ''}`}
            onClick={() => setMode(m)}>
            {m === 'note' && <NavIcon name="lock" size={15} />}
            {t(m === 'reply' ? 'support.reply' : 'support.note')}
          </button>
        ))}
      </div>
      <label htmlFor={`support-compose-${conversationId}`} className="cx-support-composer-label">
        {mode === 'reply' ? t('support.replyTo', { name: customer }) : t('support.noteLabel')}
      </label>
      {mode === 'reply' && <DraftFileList files={drafts.files} onRemove={drafts.remove} />}
      {notice && <p className="text-danger small mb-1" role="alert">{notice}</p>}
      <textarea
        id={`support-compose-${conversationId}`}
        ref={box}
        className="form-control cx-support-textarea"
        rows={2}
        placeholder={mode === 'reply' ? t('support.replyPlaceholder') : t('support.notePlaceholder')}
        value={text}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={onKeyDown}
      />
      <div className="cx-support-composer-bar">
        {mode === 'reply' && (
          <AttachButton
            id={`support-attach-${conversationId}`}
            accept={acceptFor(limits?.allowedExtensions)}
            onFiles={(chosen) => {
              setNotice(null);
              drafts.add(chosen, limits, setNotice, {
                tooLarge: t('attachments.tooLarge', { size: limits ? formatBytes(limits.maxFileSize, i18n.language) : '' }),
                type: t('attachments.typeNotAllowed'),
                tooMany: t('attachments.tooMany', { count: limits?.maxPerMessage ?? 0 }),
              });
            }}
          />
        )}
        <ReactionPicker compact label={t('messages.insertEmoji')} onPick={insert} />
        <span className="cx-composer-divider" aria-hidden="true" />
        <div className="cx-format-bar" role="toolbar" aria-label={t('format.label')}>
          {([['bold', 'B'], ['italic', 'I'], ['list', '•']] as const).map(([action, glyph]) => (
            <button key={action} type="button" className={`cx-format-button is-${action}`} aria-label={t(`format.${action}`)} title={t(`format.${action}`)}
              onMouseDown={(e) => e.preventDefault()} onClick={() => format(action)}>
              <span aria-hidden="true">{glyph}</span>
            </button>
          ))}
        </div>
        <span className="cx-composer-spacer" />
        <button type="submit" className="btn btn-primary" disabled={!canSend || busy}>
          {t(mode === 'reply' ? 'support.sendReply' : 'support.addNote')}
        </button>
      </div>
      <p className="cx-support-composer-hint">{mode === 'reply' ? t('support.replyHint') : t('support.noteHint')}</p>
    </form>
  );
}

// ---- Helpers --------------------------------------------------------------------------------

/** The customer's avatar: initials of the name and a colour stable per conversation. */
function customerOf(id: string, name: string): Pick<UserSummary, 'id' | 'avatar'> {
  const initials = name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('');
  return { id, avatar: { initials } };
}

const channelIcon = (name: string | null) => (name && /app/i.test(name) ? 'grid' : 'browser');

/** "now", "12 min", "3 hours", "2 days", then the date. */
function ago(iso: string, t: TFunction, language: string): string {
  const minutes = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
  if (minutes < 1) return t('support.ago.now');
  if (minutes < 60) return t('support.ago.minutes', { count: minutes });
  const hours = Math.round(minutes / 60);
  if (hours < 24) return t('support.ago.hours', { count: hours });
  const days = Math.round(hours / 24);
  if (days < 7) return t('support.ago.days', { count: days });
  return new Date(iso).toLocaleDateString(language, { day: 'numeric', month: 'short' });
}

function dayLabel(iso: string, t: TFunction, language: string): string {
  const date = new Date(iso);
  const today = new Date();
  const yesterday = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 1);
  const day = date.toLocaleDateString(language, { day: 'numeric', month: 'long' });
  if (date.toDateString() === today.toDateString()) return t('support.today', { day });
  if (date.toDateString() === yesterday.toDateString()) return t('support.yesterday', { day });
  return date.toLocaleDateString(language, { weekday: 'long', day: 'numeric', month: 'long' });
}

function dayAndTime(iso: string, t: TFunction, language: string): string {
  const date = new Date(iso);
  const time = date.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' });
  const today = new Date();
  if (date.toDateString() === today.toDateString()) return t('support.todayAt', { time });
  return `${date.toLocaleDateString(language, { day: 'numeric', month: 'short' })}, ${time}`;
}

function moveTab(event: KeyboardEvent<HTMLButtonElement>, current: Tab, select: (tab: Tab) => void) {
  const index = tabs.indexOf(current);
  const next =
    event.key === 'ArrowRight' ? tabs[(index + 1) % tabs.length] : event.key === 'ArrowLeft' ? tabs[(index + tabs.length - 1) % tabs.length] : event.key === 'Home' ? tabs[0] : event.key === 'End' ? tabs[tabs.length - 1] : undefined;
  if (!next) return;
  event.preventDefault();
  select(next);
  document.getElementById(`support-tab-${next}`)?.focus();
}
