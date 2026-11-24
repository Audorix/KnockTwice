import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { ContactRequestResponse, ContactResponse, UserSummary } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon } from '../../shared/components/NavIcon';
import { Pagination } from '../../shared/components/Pagination';
import { RowMenu } from '../../shared/components/RowMenu';
import { useConfirm } from '../../shared/components/ConfirmDialog';
import { errorMessage, fieldErrors } from '../auth/errors';
import { RequireUser, useAction } from '../auth/RequireUser';
import { useRealtime } from '../realtime/RealtimeContext';
import type { PresenceValue } from '../realtime/realtimeClient';
import { openDirect } from '../conversations/conversationsApi';
import { startPrivateChat } from '../private/privateChatsApi';
import { navigate } from '../../app/router';
import * as api from './contactsApi';
import { ContactDrawer } from './ContactProfile';

/**
 * Contacts (redesign 2026-11): find people, then the contacts table with tabs for requests and
 * blocked people, filtering, sorting, paging, a "…" menu per contact and a profile side panel.
 */
export function ContactsPage() {
  return (
    <RequireUser>
      <Contacts />
    </RequireUser>
  );
}

type Tab = 'all' | 'requests' | 'blocked';
type StatusFilter = 'all' | 'online' | 'offline';
type Sort = 'nameAsc' | 'nameDesc' | 'recent';
const pageSizes = [8, 16, 32] as const;

function Contacts() {
  const { t, i18n } = useTranslation();
  const { presence } = useRealtime();
  const [contacts, setContacts] = useState<ContactResponse[]>([]);
  const [requests, setRequests] = useState<ContactRequestResponse[]>([]);
  const [blocked, setBlocked] = useState<ContactResponse[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);
  // `?tab=requests` (from the home page) opens the requests.
  const [tab, setTab] = useState<Tab>(() =>
    typeof window !== 'undefined' && new URLSearchParams(window.location.search).get('tab') === 'requests' ? 'requests' : 'all');
  const [filter, setFilter] = useState('');
  const [status, setStatus] = useState<StatusFilter>('all');
  const [sort, setSort] = useState<Sort>('nameAsc');
  const [pageSize, setPageSize] = useState<number>(pageSizes[0]);
  const [page, setPage] = useState(1);
  const [profile, setProfile] = useState<ContactResponse | null>(null);
  const searchInput = useRef<HTMLInputElement>(null);

  const reload = useCallback(() => {
    Promise.all([api.listContacts(), api.listRequests(), api.listBlocked()]).then(([c, r, b]) => {
      setContacts(c);
      setRequests(r);
      setBlocked(b);
    }, setError);
  }, []);
  useEffect(reload, [reload]);
  const act = useAction(setError, reload);

  const statusOf = useCallback((id: string): PresenceValue => presence.get(id) ?? 'offline', [presence]);

  const visible = useMemo(() => {
    const needle = filter.trim().toLocaleLowerCase(i18n.language);
    const collator = new Intl.Collator(i18n.language, { sensitivity: 'base' });
    return contacts
      .filter((c) => !needle || c.user.displayName.toLocaleLowerCase(i18n.language).includes(needle)
        || (c.sharedOrganizations ?? []).some((o) => o.name.toLocaleLowerCase(i18n.language).includes(needle)))
      .filter((c) => status === 'all' || statusOf(c.user.id) === status)
      .sort((a, b) =>
        sort === 'recent' ? Date.parse(b.sinceUtc) - Date.parse(a.sinceUtc)
          : sort === 'nameDesc' ? collator.compare(b.user.displayName, a.user.displayName)
            : collator.compare(a.user.displayName, b.user.displayName));
  }, [contacts, filter, status, sort, statusOf, i18n.language]);

  // Back to the first page whenever what is shown changes.
  useEffect(() => setPage(1), [filter, status, sort, pageSize]);
  const pages = Math.max(1, Math.ceil(visible.length / pageSize));
  const current = Math.min(page, pages);
  const shown = visible.slice((current - 1) * pageSize, current * pageSize);

  const message = (userId: string) =>
    void openDirect(userId).then((conversation) => navigate(`/conversations?id=${encodeURIComponent(conversation.id)}`), setError);
  const remove = (user: UserSummary) => void act(async () => {
    await api.removeContact(user.id);
    setProfile(null);
  });
  const [confirm, confirmDialog] = useConfirm();
  const block = (user: UserSummary) =>
    void confirm({
      title: t('confirm.block.title', { name: user.displayName }),
      text: t('confirm.block.text'),
      confirmLabel: t('confirm.block.action'),
    }).then((ok) => { if (ok) void (act(async () => {
      await api.blockUser(user.id);
      setProfile(null);
    })); });

  // Sends the private chat invitation and opens the chat, which waits for the contact to accept.
  const startPrivate = (user: UserSummary) =>
    void act(async () => {
      const chat = await startPrivateChat(user.id);
      navigate(`/private?id=${encodeURIComponent(chat.id)}`);
    });

  const incoming = requests.filter((r) => r.direction === 'incoming');
  const outgoing = requests.filter((r) => r.direction === 'outgoing');

  return (
    <div className="cx-contacts">
      {confirmDialog}
      <header className="cx-page-header">
        <div>
          <nav aria-label={t('nav.label')} className="cx-breadcrumb">
            <span>{t('nav.workspace')}</span>
            <span aria-hidden="true">/</span>
            <span aria-current="page">{t('contacts.title')}</span>
          </nav>
          <h1 className="cx-page-title mb-1">{t('contacts.title')}</h1>
          <p className="cx-muted mb-0">{t('contacts.subtitle')}</p>
        </div>
        <button type="button" className="btn btn-primary cx-icon-button" onClick={() => searchInput.current?.focus()}>
          <NavIcon name="plus" size={18} />
          {t('contacts.findPeople')}
        </button>
      </header>

      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {notice && <FormAlert tone="success">{notice}</FormAlert>}

      <UserSearch
        inputRef={searchInput}
        onRequest={(user) =>
          void act(async () => {
            const result = await api.sendRequest(user.id);
            setNotice(t(result === 'accepted' ? 'contacts.nowContacts' : 'contacts.requestSent', { name: user.displayName }));
          })
        }
      />

      <section className="cx-card cx-contacts-card" aria-label={t('contacts.list', { count: contacts.length })}>
        <div className="cx-tabs" role="tablist" aria-label={t('contacts.tabs.label')}>
          {(['all', 'requests', 'blocked'] as const).map((key) => {
            const count = key === 'all' ? contacts.length : key === 'requests' ? requests.length : blocked.length;
            return (
              <button
                key={key}
                type="button"
                role="tab"
                id={`contacts-tab-${key}`}
                aria-controls={`contacts-panel-${key}`}
                aria-selected={tab === key}
                tabIndex={tab === key ? 0 : -1}
                className={`cx-tab${tab === key ? ' is-active' : ''}`}
                onClick={() => setTab(key)}
                onKeyDown={(e) => moveTab(e, key, setTab)}
              >
                {t(`contacts.tabs.${key}`)}
                {count > 0 && <> <span className="cx-tab-count">{count}</span></>}
              </button>
            );
          })}
        </div>

        {tab === 'all' && (
          <div role="tabpanel" id="contacts-panel-all" aria-labelledby="contacts-tab-all">
            <div className="cx-table-tools">
              <label className="cx-search-field">
                <NavIcon name="search" size={18} />
                <span className="visually-hidden">{t('contacts.filterLabel')}</span>
                <input type="search" className="form-control" placeholder={t('contacts.filterPlaceholder')} value={filter} onChange={(e) => setFilter(e.target.value)} />
              </label>
              <div className="cx-table-selects">
                <label className="visually-hidden" htmlFor="contacts-status">{t('contacts.statusFilter.label')}</label>
                <select id="contacts-status" className="form-select" value={status} onChange={(e) => setStatus(e.target.value as StatusFilter)}>
                  {(['all', 'online', 'offline'] as const).map((s) => <option key={s} value={s}>{t(`contacts.statusFilter.${s}`)}</option>)}
                </select>
                <label className="visually-hidden" htmlFor="contacts-sort">{t('contacts.sort.label')}</label>
                <select id="contacts-sort" className="form-select" value={sort} onChange={(e) => setSort(e.target.value as Sort)}>
                  {(['nameAsc', 'nameDesc', 'recent'] as const).map((s) => <option key={s} value={s}>{t(`contacts.sort.${s}`)}</option>)}
                </select>
              </div>
            </div>

            {contacts.length === 0 ? (
              <p className="cx-muted cx-table-empty">{t('contacts.empty')}</p>
            ) : visible.length === 0 ? (
              <p className="cx-muted cx-table-empty">{t('contacts.noMatches')}</p>
            ) : (
              <table className="cx-table">
                <thead>
                  <tr>
                    <th scope="col">{t('contacts.columns.name')}</th>
                    <th scope="col">{t('contacts.columns.status')}</th>
                    <th scope="col">{t('contacts.columns.organization')}</th>
                    <th scope="col"><span className="visually-hidden">{t('contacts.columns.actions')}</span></th>
                  </tr>
                </thead>
                <tbody>
                  {shown.map((c) => (
                    <tr key={c.user.id}>
                      <td data-label={t('contacts.columns.name')}>
                        <button type="button" className="cx-person" onClick={() => setProfile(c)}>
                          <Avatar user={c.user} size="sm" presence={presence.get(c.user.id)} />
                          <span className="cx-person-name">{c.user.displayName}</span>
                        </button>
                      </td>
                      <td data-label={t('contacts.columns.status')}>
                        <PresenceLabel value={statusOf(c.user.id)} />
                      </td>
                      <td data-label={t('contacts.columns.organization')}>
                        <OrganizationTags organizations={c.sharedOrganizations} />
                      </td>
                      <td className="cx-table-actions">
                        <button type="button" className="btn btn-sm btn-outline-primary cx-icon-button" aria-label={t('contacts.messageNamed', { name: c.user.displayName })}
                          onClick={() => message(c.user.id)}>
                          <NavIcon name="message" size={16} />
                          {t('contacts.message')}
                        </button>
                        <RowMenu
                          label={t('contacts.moreFor', { name: c.user.displayName })}
                          items={[
                            { icon: 'user', label: t('contacts.viewProfile'), onSelect: () => setProfile(c) },
                            { icon: 'lock', label: t('contacts.privateChat'), onSelect: () => startPrivate(c.user) },
                            { icon: 'trash', label: t('contacts.removeContact'), onSelect: () => remove(c.user), separated: true },
                            { icon: 'block', label: t('contacts.blockContact'), onSelect: () => block(c.user), danger: true },
                          ]}
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}

            {visible.length > 0 && (
              <div className="cx-table-footer">
                <span className="cx-muted">
                  {t('contacts.showing', { from: (current - 1) * pageSize + 1, to: Math.min(current * pageSize, visible.length), total: visible.length })}
                </span>
                <div className="cx-table-paging">
                  <label className="visually-hidden" htmlFor="contacts-page-size">{t('contacts.perPageLabel')}</label>
                  <select id="contacts-page-size" className="form-select form-select-sm" value={pageSize} onChange={(e) => setPageSize(Number(e.target.value))}>
                    {pageSizes.map((size) => <option key={size} value={size}>{t('contacts.perPage', { count: size })}</option>)}
                  </select>
                  <Pagination page={current} pages={pages} onPage={setPage} />
                </div>
              </div>
            )}
          </div>
        )}

        {tab === 'requests' && (
          <div role="tabpanel" id="contacts-panel-requests" aria-labelledby="contacts-tab-requests">
            {requests.length === 0 && <p className="cx-muted cx-table-empty">{t('contacts.noRequests')}</p>}
            {incoming.length > 0 && (
              <section aria-labelledby="incoming-title" className="cx-table-section">
                <h2 id="incoming-title" className="cx-section-title">{t('contacts.incoming')}</h2>
                <PeopleRows
                  items={incoming.map((r) => ({ key: r.id, user: r.user, meta: t('contacts.requestedOn', { date: formatDay(r.createdAtUtc, i18n.language) }) }))}
                  actions={(item) => (
                    <>
                      <button type="button" className="btn btn-sm btn-primary" onClick={() => void act(() => api.acceptRequest(item.key))}>{t('contacts.accept')}</button>
                      <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.rejectRequest(item.key))}>{t('contacts.decline')}</button>
                    </>
                  )}
                />
              </section>
            )}
            {outgoing.length > 0 && (
              <section aria-labelledby="outgoing-title" className="cx-table-section">
                <h2 id="outgoing-title" className="cx-section-title">{t('contacts.outgoing')}</h2>
                <PeopleRows
                  items={outgoing.map((r) => ({ key: r.id, user: r.user, meta: t('contacts.requestedOn', { date: formatDay(r.createdAtUtc, i18n.language) }) }))}
                  actions={(item) => (
                    <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.cancelRequest(item.key))}>{t('contacts.cancel')}</button>
                  )}
                />
              </section>
            )}
          </div>
        )}

        {tab === 'blocked' && (
          <div role="tabpanel" id="contacts-panel-blocked" aria-labelledby="contacts-tab-blocked">
            {blocked.length === 0 ? (
              <p className="cx-muted cx-table-empty">{t('contacts.noBlocked')}</p>
            ) : (
              <PeopleRows
                items={blocked.map((b) => ({ key: b.user.id, user: b.user }))}
                actions={(item) => (
                  <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.unblockUser(item.key))}>{t('contacts.unblock')}</button>
                )}
              />
            )}
          </div>
        )}
      </section>

      {profile && (
        <ContactDrawer
          contact={profile}
          status={statusOf(profile.user.id)}
          onClose={() => setProfile(null)}
          onMessage={() => message(profile.user.id)}
          onRemove={() => remove(profile.user)}
          onBlock={() => block(profile.user)}
        />
      )}
    </div>
  );
}

/** Arrow keys move between tabs (WAI-ARIA tabs pattern). */
function moveTab(event: KeyboardEvent<HTMLButtonElement>, current: Tab, select: (tab: Tab) => void) {
  const order: Tab[] = ['all', 'requests', 'blocked'];
  const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
  if (step === 0) return;
  event.preventDefault();
  const next = order[(order.indexOf(current) + step + order.length) % order.length]!;
  select(next);
  document.getElementById(`contacts-tab-${next}`)?.focus();
}

function formatDay(value: string, language: string) {
  return new Intl.DateTimeFormat(language, { dateStyle: 'medium' }).format(new Date(value));
}

function PresenceLabel({ value }: { value: PresenceValue }) {
  const { t } = useTranslation();
  return (
    <span className="cx-presence-label">
      <span className={`cx-dot ${value === 'online' ? 'cx-dot-ok' : 'cx-dot-idle'}`} aria-hidden="true" />
      {t(`contacts.presence.${value}`)}
    </span>
  );
}

function OrganizationTags({ organizations }: { organizations: ContactResponse['sharedOrganizations'] }) {
  if (!organizations || organizations.length === 0) return <span className="cx-muted" aria-label="—">—</span>;
  return (
    <span className="cx-tags">
      {organizations.map((o) => <span key={o.id} className="cx-tag">{o.name}</span>)}
    </span>
  );
}

function UserSearch({ onRequest, inputRef }: { onRequest: (user: UserSummary) => void; inputRef: React.RefObject<HTMLInputElement | null> }) {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<UserSummary[] | null>(null);
  const [error, setError] = useState<unknown>(null);

  const search = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      setResults(await api.searchUsers(query.trim()));
    } catch (err) {
      setError(err);
      setResults(null);
    }
  };

  const errors = fieldErrors(error, 'q', t);
  return (
    <section className="cx-card" aria-labelledby="search-title">
      <h2 id="search-title" className="cx-card-title">{t('contacts.connectTitle')}</h2>
      <form onSubmit={search} className="cx-search-row" role="search">
        <label className="cx-search-field flex-grow-1">
          <NavIcon name="search" size={18} />
          <span className="visually-hidden">{t('contacts.searchLabel')}</span>
          <input
            ref={inputRef}
            type="search"
            className={`form-control${errors.length > 0 ? ' is-invalid' : ''}`}
            placeholder={t('contacts.searchPlaceholder')}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            aria-describedby="search-hint"
          />
        </label>
        <button type="submit" className="btn btn-primary" disabled={query.trim().length < 2}>{t('contacts.search')}</button>
      </form>
      <p id="search-hint" className="cx-meta mt-2 mb-0">{t('contacts.searchShortHint')}</p>
      {errors.map((message) => <p key={message} className="text-danger small mb-0">{message}</p>)}
      {error !== null && errors.length === 0 && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {results !== null && (
        <div role="status" aria-live="polite" className="mt-3">
          {results.length === 0 ? (
            <p className="cx-muted mb-0">{t('contacts.noResults')}</p>
          ) : (
            <PeopleRows
              items={results.map((u) => ({ key: u.id, user: u }))}
              actions={(item) => <button type="button" className="btn btn-sm btn-primary" onClick={() => onRequest(item.user)}>{t('contacts.add')}</button>}
            />
          )}
        </div>
      )}
    </section>
  );
}

/** Simple rows of people with actions (search results, requests, blocked people). */
function PeopleRows<T extends { key: string; user: UserSummary; meta?: string }>({ items, actions }: { items: T[]; actions: (item: T) => React.ReactNode }) {
  const { presence } = useRealtime();
  return (
    <ul className="list-unstyled mb-0 cx-people-rows">
      {items.map((item) => (
        <li key={item.key}>
          <span className="cx-person">
            <Avatar user={item.user} size="sm" presence={presence.get(item.user.id)} />
            <span className="min-w-0">
              <span className="cx-person-name d-block">{item.user.displayName}</span>
              {item.meta && <span className="cx-meta">{item.meta}</span>}
            </span>
          </span>
          <span className="d-flex gap-2 flex-wrap justify-content-end">{actions(item)}</span>
        </li>
      ))}
    </ul>
  );
}

/** Kept for other pages: a plain list of people with optional actions. */
export function PeopleList<T extends { key: string; user: UserSummary }>({ items, actions }: { items: T[]; actions?: (item: T) => React.ReactNode }) {
  return <PeopleRows items={items} actions={actions ?? (() => null)} />;
}
