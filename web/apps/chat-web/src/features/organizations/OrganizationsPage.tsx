import { useCallback, useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { navigate } from '../../app/router';
import type { MyInvitationResponse, OrganizationResponse } from '../../shared/api/contracts';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon } from '../../shared/components/NavIcon';
import { Pagination } from '../../shared/components/Pagination';
import { errorMessage, fieldErrors } from '../auth/errors';
import { RequireUser, useAction } from '../auth/RequireUser';
import * as api from './organizationsApi';

/**
 * Organizations (redesigned to the owner's mockup 2026-11-24): invitations addressed to the caller,
 * tabs (all, owned by me, member), search, sorting and paging over a table with the caller's role,
 * Manage (owners and administrators) or Open, and a "Create an organization" panel beside the list.
 */
export function OrganizationsPage() {
  return (
    <RequireUser>
      <Organizations />
    </RequireUser>
  );
}

type Tab = 'all' | 'owned' | 'member';
type Sort = 'nameAsc' | 'nameDesc' | 'newest';

const tabs: Tab[] = ['all', 'owned', 'member'];
const pageSize = 8;

function Organizations() {
  const { t, i18n } = useTranslation();
  const [organizations, setOrganizations] = useState<OrganizationResponse[] | null>(null);
  const [invitations, setInvitations] = useState<MyInvitationResponse[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [creating, setCreating] = useState(false);
  const [tab, setTab] = useState<Tab>('all');
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState<Sort>('nameAsc');
  const [page, setPage] = useState(1);

  const reload = useCallback(() => {
    Promise.all([api.listOrganizations(), api.listMyInvitations()]).then(([o, i]) => {
      setOrganizations(o);
      setInvitations(i);
      if (o.length === 0) setCreating(true); // nothing to list yet: offer to create one
    }, setError);
  }, []);
  useEffect(reload, [reload]);
  const act = useAction(setError, reload);

  // Back to the first page whenever the list changes shape.
  useEffect(() => setPage(1), [tab, search, sort]);

  const all = organizations ?? [];
  const inTab = (o: OrganizationResponse, which: Tab) => which === 'all' || (which === 'owned' ? o.myRole === 'owner' : o.myRole !== 'owner');
  const counts = Object.fromEntries(tabs.map((x) => [x, all.filter((o) => inTab(o, x)).length])) as Record<Tab, number>;
  const term = search.trim().toLocaleLowerCase();
  const visible = all
    .filter((o) => inTab(o, tab))
    .filter((o) => !term || o.name.toLocaleLowerCase().includes(term))
    .sort((a, b) => sort === 'newest'
      ? b.createdAtUtc.localeCompare(a.createdAtUtc)
      : (sort === 'nameAsc' ? 1 : -1) * a.name.localeCompare(b.name, i18n.language));
  const pages = Math.max(1, Math.ceil(visible.length / pageSize));
  const current = Math.min(page, pages);
  const shown = visible.slice((current - 1) * pageSize, current * pageSize);

  return (
    <div className="cx-organizations">
      <header className="cx-page-header">
        <div>
          <nav aria-label={t('nav.label')} className="cx-breadcrumb">
            <span>{t('nav.workspace')}</span>
            <span aria-hidden="true">/</span>
            <span aria-current="page">{t('organizations.title')}</span>
          </nav>
          <h1 className="cx-page-title mb-1">{t('organizations.title')}</h1>
          <p className="cx-muted mb-0">{t('organizations.subtitle')}</p>
        </div>
        <button type="button" className="btn btn-primary cx-icon-button" aria-expanded={creating} aria-controls="create-organization" onClick={() => setCreating(true)}>
          <NavIcon name="plus" size={18} />
          {t('organizations.new')}
        </button>
      </header>

      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      {invitations.length > 0 && (
        <section className="cx-card mb-3" aria-labelledby="invitations-title">
          <h2 id="invitations-title" className="cx-card-title">
            {t('organizations.invitations')}
          </h2>
          <ul className="list-unstyled mb-0 cx-session-list">
            {invitations.map((invitation) => (
              <li key={invitation.id} className="cx-session">
                <span>
                  <strong>{invitation.organizationName}</strong>
                  <span className="cx-meta d-block">
                    {t('organizations.invitedAs', { role: t(`organizations.roles.${invitation.role}`) })}
                    {invitation.invitedBy && ` · ${t('organizations.invitedBy', { name: invitation.invitedBy.displayName })}`}
                  </span>
                </span>
                <span className="d-flex gap-2">
                  <button type="button" className="btn btn-sm btn-primary" onClick={() => void act(() => api.acceptInvitation(invitation.id))}>
                    {t('organizations.join')}
                  </button>
                  <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.declineInvitation(invitation.id))}>
                    {t('contacts.decline')}
                  </button>
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="cx-tabs" role="tablist" aria-label={t('organizations.tabsLabel')}>
        {tabs.map((x) => (
          <button
            key={x}
            id={`organizations-tab-${x}`}
            type="button"
            role="tab"
            aria-selected={tab === x}
            aria-controls="organizations-panel"
            tabIndex={tab === x ? 0 : -1}
            className={`cx-tab${tab === x ? ' is-active' : ''}`}
            onClick={() => setTab(x)}
            onKeyDown={(e) => moveTab(e, x, setTab)}
          >
            {t(`organizations.tabs.${x}`)}
            {organizations !== null && <> <span className="cx-tab-count">{counts[x]}</span></>}
          </button>
        ))}
      </div>

      <div className="cx-organizations-layout">
        <section className="cx-card cx-table-card" role="tabpanel" id="organizations-panel" aria-labelledby={`organizations-tab-${tab}`}>
          <div className="cx-table-tools">
            <label className="cx-search-field">
              <NavIcon name="search" size={18} />
              <span className="visually-hidden">{t('organizations.findLabel')}</span>
              <input type="search" className="form-control" placeholder={t('organizations.findPlaceholder')} value={search} onChange={(e) => setSearch(e.target.value)} />
            </label>
            <div className="cx-table-selects">
              <label className="visually-hidden" htmlFor="organizations-sort">{t('organizations.sort.label')}</label>
              <select id="organizations-sort" className="form-select" value={sort} onChange={(e) => setSort(e.target.value as Sort)}>
                {(['nameAsc', 'nameDesc', 'newest'] as const).map((s) => <option key={s} value={s}>{t(`organizations.sort.${s}`)}</option>)}
              </select>
            </div>
          </div>

          {organizations === null ? (
            <p className="cx-muted cx-table-empty">{t('auth.working')}</p>
          ) : visible.length === 0 ? (
            <p className="cx-muted cx-table-empty">{counts[tab] === 0 ? t(`organizations.empty.${tab}`) : t('organizations.noMatches')}</p>
          ) : (
            <table className="cx-table cx-organization-table">
              <thead>
                <tr>
                  <th scope="col">{t('organizations.columns.organization')}</th>
                  <th scope="col">{t('organizations.columns.role')}</th>
                  <th scope="col"><span className="visually-hidden">{t('organizations.columns.actions')}</span></th>
                </tr>
              </thead>
              <tbody>
                {shown.map((o) => {
                  const manages = o.myRole === 'owner' || o.myRole === 'administrator';
                  return (
                    <tr key={o.id}>
                      <td data-label={t('organizations.columns.organization')}>
                        <div className="cx-organization-cell">
                          <span className="cx-organization-mark" aria-hidden="true">{initialsOf(o.name)}</span>
                          <span className="min-w-0">
                            <strong className="d-block text-truncate">{o.name}</strong>
                            <span className="cx-muted small">{t('organizations.since', { date: formatDay(o.createdAtUtc, i18n.language) })}</span>
                          </span>
                        </div>
                      </td>
                      <td data-label={t('organizations.columns.role')}>
                        <span className={`cx-role-pill is-${o.myRole}`}>{t(`organizations.roles.${o.myRole}`)}</span>
                      </td>
                      <td className="cx-table-actions">
                        <button type="button" className="btn btn-sm btn-outline-secondary cx-organization-action"
                          aria-label={t(manages ? 'organizations.manageNamed' : 'organizations.openNamed', { name: o.name })}
                          onClick={() => navigate(`/organization?id=${encodeURIComponent(o.id)}`)}>
                          {t(manages ? 'organizations.manage' : 'organizations.open')}
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}

          {visible.length > 0 && (
            <div className="cx-table-footer">
              <span className="cx-muted">
                {t('organizations.showing', { from: (current - 1) * pageSize + 1, to: Math.min(current * pageSize, visible.length), count: visible.length })}
              </span>
              {pages > 1 && <Pagination page={current} pages={pages} onPage={setPage} />}
            </div>
          )}
        </section>

        <aside className="cx-organizations-side">
          {creating && <CreateOrganization onClose={() => setCreating(false)} />}
          <div className="cx-organizations-help">
            <strong className="d-block">{t('organizations.lookingTitle')}</strong>
            <span className="cx-muted">{t('organizations.lookingText')}</span>
          </div>
        </aside>
      </div>
    </div>
  );
}

/** "Create an organization": name, what happens, create or cancel. Opens the new organization. */
function CreateOrganization({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const input = useRef<HTMLInputElement>(null);

  useEffect(() => {
    input.current?.focus();
  }, []);

  const create = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const created = await api.createOrganization(name);
      navigate(`/organization?id=${encodeURIComponent(created.id)}`);
    } catch (err) {
      setError(err);
      setBusy(false);
    }
  };

  const nameError = error !== null ? fieldErrors(error, 'name', t)[0] : undefined;
  return (
    <section id="create-organization" className="cx-card cx-create-organization" aria-labelledby="create-organization-title"
      onKeyDown={(e) => e.key === 'Escape' && onClose()}>
      <div className="cx-create-organization-head">
        <span className="cx-create-organization-icon" aria-hidden="true">
          <NavIcon name="building" size={28} />
        </span>
        <span className="min-w-0 flex-grow-1">
          <h2 id="create-organization-title">{t('organizations.create')}</h2>
          <span className="cx-muted">{t('organizations.createTagline')}</span>
        </span>
        <button type="button" className="cx-tool" onClick={onClose} title={t('organizations.closeCreate')}>
          <NavIcon name="close" size={18} />
          <span className="visually-hidden">{t('organizations.closeCreate')}</span>
        </button>
      </div>
      <form onSubmit={(e) => void create(e)} noValidate>
        {error !== null && !nameError && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        <label htmlFor="organization-name" className="form-label fw-semibold">{t('organizations.name')}</label>
        <input
          ref={input}
          id="organization-name"
          className={`form-control${nameError ? ' is-invalid' : ''}`}
          maxLength={100}
          placeholder={t('organizations.namePlaceholder')}
          value={name}
          aria-describedby="organization-name-hint"
          onChange={(e) => setName(e.target.value)}
        />
        {nameError && <div className="invalid-feedback d-block">{nameError}</div>}
        <p id="organization-name-hint" className="form-text">{t('organizations.createHint')}</p>
        <ul className="list-unstyled cx-create-organization-facts">
          <li><NavIcon name="check" size={18} />{t('organizations.becomeOwner')}</li>
          <li><NavIcon name="check" size={18} />{t('organizations.generalWorkspace')}</li>
        </ul>
        <button type="submit" className="btn btn-primary w-100" disabled={!name.trim() || busy}>
          {busy ? t('auth.working') : t('organizations.createSubmit')}
        </button>
        <button type="button" className="btn btn-link w-100 cx-create-organization-cancel" onClick={onClose}>
          {t('conversations.cancel')}
        </button>
      </form>
    </section>
  );
}

const initialsOf = (name: string) => name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '?';

function formatDay(value: string, language: string) {
  return new Intl.DateTimeFormat(language, { dateStyle: 'medium' }).format(new Date(value));
}

/** Arrow keys move between tabs (WAI-ARIA tabs pattern). */
function moveTab(event: KeyboardEvent<HTMLButtonElement>, current: Tab, select: (tab: Tab) => void) {
  const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
  if (step === 0) return;
  event.preventDefault();
  const next = tabs[(tabs.indexOf(current) + step + tabs.length) % tabs.length]!;
  select(next);
  document.getElementById(`organizations-tab-${next}`)?.focus();
}
