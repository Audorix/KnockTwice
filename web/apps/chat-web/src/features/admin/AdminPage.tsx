import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from '../../app/router';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon, type NavIconName } from '../../shared/components/NavIcon';
import { Pagination } from '../../shared/components/Pagination';
import { RowMenu, type RowMenuItem } from '../../shared/components/RowMenu';
import { formatDateTime } from '../../shared/format';
import { formatBytes } from '../attachments/Attachments';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { ApiError } from '../../shared/api/httpClient';
import * as api from './adminApi';

/**
 * Installation administration (Phase 9): overview, users, organizations, roles, live settings,
 * providers and the audit log. Tabs and actions follow the signed-in user's permissions; the
 * server checks every call again.
 */

const tabs = ['overview', 'users', 'organizations', 'roles', 'settings', 'providers', 'audit'] as const;
type Tab = (typeof tabs)[number];

const tabPermission: Record<Tab, string> = {
  overview: 'installation.read',
  users: 'users.read',
  organizations: 'organizations.read',
  roles: 'users.read',
  settings: 'installation.read',
  providers: 'installation.read',
  audit: 'audit.read',
};

/** Whether the user may open the administration at all (the navigation uses the same rule from app/navigationRules). */
export { canAdminister } from '../../app/navigationRules';

export function AdminPage() {
  return (
    <RequireUser>
      <Administration />
    </RequireUser>
  );
}

function Administration() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const { query } = useLocation();
  const permissions = user?.permissions ?? [];
  const available = tabs.filter((tab) => permissions.includes(tabPermission[tab]));
  const requested = query.get('tab') as Tab | null;
  const tab = requested && available.includes(requested) ? requested : available[0];

  if (!tab) {
    return (
      <section className="cx-card">
        <h1 className="cx-card-title">{t('admin.title')}</h1>
        <FormAlert tone="danger">{t('admin.noAccess')}</FormAlert>
      </section>
    );
  }

  const can = (permission: string) => permissions.includes(permission);
  return (
    <div className="cx-admin">
      <header className="cx-page-header">
        <div>
          <nav aria-label={t('nav.label')} className="cx-breadcrumb">
            <span>{t('nav.workspace')}</span>
            <span aria-hidden="true">/</span>
            <span aria-current="page">{t('admin.title')}</span>
          </nav>
          <h1 className="cx-page-title mb-1">{t('admin.title')}</h1>
          <p className="cx-muted mb-0">{t('admin.subtitle')}</p>
        </div>
        <div className="cx-admin-badges">
          {!can('installation.manage') && !can('users.manage') && <span className="badge text-bg-secondary">{t('admin.readOnly')}</span>}
          {can('installation.read') && <InstallationBadge />}
        </div>
      </header>
      <nav className="cx-admin-tabs" aria-label={t('admin.sections')}>
        {available.map((name) => (
          <Link key={name} to={`/admin?tab=${name}`} className="cx-admin-tab" aria-current={name === tab ? 'page' : undefined}>
            {t(`admin.tabs.${name}`)}
          </Link>
        ))}
      </nav>
      {tab === 'overview' && <Overview available={available} />}
      {tab === 'users' && <Users canManage={can('users.manage')} currentUserId={user!.id} />}
      {tab === 'organizations' && <Organizations canManage={can('organizations.manage')} />}
      {tab === 'roles' && <RolesView canManage={can('users.manage')} />}
      {tab === 'settings' && <Settings canManage={can('installation.manage')} />}
      {tab === 'providers' && <Providers canManage={can('installation.manage')} />}
      {tab === 'audit' && <Audit />}
    </div>
  );
}

/** Loads data once and on demand; errors are shown in the section. */
function useLoad<T>(load: () => Promise<T>, deps: readonly unknown[]) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<unknown>(null);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const reload = useCallback(() => {
    setError(null);
    load().then(setData, setError);
  }, deps);
  useEffect(reload, [reload]);
  return { data, setData, error, setError, reload };
}

function Section({ title, children, actions }: { title: string; children: ReactNode; actions?: ReactNode }) {
  return (
    <section className="cx-card">
      <div className="cx-card-header">
        <h2 className="cx-card-title">{title}</h2>
        {actions}
      </div>
      {children}
    </section>
  );
}

function Loading() {
  const { t } = useTranslation();
  return <p className="cx-muted mb-0">{t('auth.working')}</p>;
}

// ---- Overview ------------------------------------------------------------------------------

/** Environment and version in the page header (redesign 2026-11-24). */
function InstallationBadge() {
  const [data, setData] = useState<api.AdminOverview | null>(null);
  useEffect(() => {
    api.getOverview().then(setData, () => undefined);
  }, []);
  if (!data) return null;
  return (
    <>
      <span className="cx-env-badge">{data.environment}</span>
      <span className="cx-version">v{data.version}</span>
    </>
  );
}

/** A link to an administration tab, shown only when the caller may open it. */
function TabLink({ tab, available, className, children, label }: { tab: Tab; available: readonly Tab[]; className: string; children: ReactNode; label?: string }) {
  if (!available.includes(tab)) return null;
  return (
    <Link to={`/admin?tab=${tab}`} className={className} aria-label={label}>
      {children}
    </Link>
  );
}

/**
 * The installation overview (redesigned to the owner's mockup 2026-11-24): counts with links to
 * their sections, the providers in use (with a warning while malware scanning is off), the
 * installation details and shortcuts to roles, settings and the audit log.
 */
function Overview({ available }: { available: readonly Tab[] }) {
  const { t } = useTranslation();
  const { data, error } = useLoad(api.getOverview, []);
  const go = (label: string) => (
    <>
      {label}
      <NavIcon name="arrow-right" size={16} />
    </>
  );
  return (
    <div className="cx-admin-overview">
      <h2 className="cx-admin-heading">{t('admin.overview.title')}</h2>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {!data ? (
        error === null && <Loading />
      ) : (
        <>
          {data.restartRequiredFor.length > 0 && (
            <FormAlert tone="info">{t('admin.restartPending', { groups: data.restartRequiredFor.map((g) => t(`admin.providers.${g}`)).join(', ') })}</FormAlert>
          )}
          <div className="cx-admin-stat-cards">
            <StatCard icon="people" label={t('admin.overview.users')} value={String(data.users)}
              hint={data.disabledUsers ? t('admin.overview.disabled', { count: data.disabledUsers }) : undefined}
              link={<TabLink tab="users" available={available} className="cx-admin-link">{go(t('admin.overview.manageUsers'))}</TabLink>} />
            <StatCard icon="building" label={t('admin.overview.organizations')} value={String(data.organizations)}
              link={<TabLink tab="organizations" available={available} className="cx-admin-link">{go(t('admin.overview.manageOrganizations'))}</TabLink>} />
            <StatCard icon="door" label={t('admin.overview.registration')} value={t(`admin.registrationModes.${data.registrationMode}`)}
              link={<TabLink tab="settings" available={available} className="cx-admin-link">{go(t('admin.overview.reviewRegistration'))}</TabLink>} />
          </div>

          <div className="cx-admin-overview-grid">
            <section className="cx-card cx-admin-panel" aria-labelledby="admin-providers-title">
              <h3 id="admin-providers-title">{t('admin.overview.providersTitle')}</h3>
              <p className="cx-muted">{t('admin.overview.providersSubtitle')}</p>
              <ul className="list-unstyled cx-admin-providers">
                <li>
                  <NavIcon name="database" size={22} />
                  <span>{t('admin.overview.database')}</span>
                  <strong>{databaseName(data.databaseProvider)}</strong>
                  <TabLink tab="providers" available={available} className="cx-admin-link" label={t('admin.overview.viewProviderNamed', { name: t('admin.overview.database') })}>
                    {go(t('admin.overview.viewProvider'))}
                  </TabLink>
                </li>
                <li>
                  <NavIcon name="folder" size={22} />
                  <span>{t('admin.overview.storage')}</span>
                  <strong>{data.storageProvider}</strong>
                  <TabLink tab="providers" available={available} className="cx-admin-link" label={t('admin.overview.viewProviderNamed', { name: t('admin.overview.storage') })}>
                    {go(t('admin.overview.viewProvider'))}
                  </TabLink>
                </li>
                <li>
                  <NavIcon name="shield" size={22} />
                  <span>{t('admin.overview.scanning')}</span>
                  <strong className="cx-admin-state">
                    <span className={`cx-dot ${data.scanningEnabled ? 'cx-dot-ok' : 'cx-dot-warn'}`} aria-hidden="true" />
                    {data.scanningEnabled ? t('admin.on') : t('admin.off')}
                  </strong>
                  <TabLink tab="providers" available={available} className="cx-admin-link" label={t('admin.overview.configureScanning')}>
                    {go(t('admin.overview.configure'))}
                  </TabLink>
                </li>
              </ul>
              {!data.scanningEnabled && (
                <div className="cx-admin-warning" role="note">
                  <NavIcon name="alert" size={22} />
                  <span>
                    <strong className="d-block">{t('admin.overview.scanningOffTitle')}</strong>
                    {t('admin.overview.scanningOffText')}
                  </span>
                </div>
              )}
            </section>

            <section className="cx-card cx-admin-panel" aria-labelledby="admin-details-title">
              <h3 id="admin-details-title">{t('admin.overview.detailsTitle')}</h3>
              <dl className="cx-admin-details">
                <dt>{t('admin.overview.environment')}</dt>
                <dd><span className="cx-env-badge">{data.environment}</span></dd>
                <dt>{t('admin.overview.version')}</dt>
                <dd>{data.version}</dd>
                <dt>{t('admin.overview.registration')}</dt>
                <dd>{t(`admin.registrationModes.${data.registrationMode}`)}</dd>
              </dl>
              <div className="cx-admin-detail-links">
                <TabLink tab="settings" available={available} className="cx-admin-link is-large">
                  <NavIcon name="settings" size={20} />
                  {go(t('admin.overview.applicationSettings'))}
                </TabLink>
                <TabLink tab="providers" available={available} className="cx-admin-link is-large">
                  <NavIcon name="layers" size={20} />
                  {go(t('admin.overview.providerSettings'))}
                </TabLink>
              </div>
            </section>
          </div>

          <h2 className="cx-admin-heading">{t('admin.overview.shortcutsTitle')}</h2>
          <div className="cx-admin-shortcuts">
            <TabLink tab="roles" available={available} className="cx-card cx-admin-shortcut">
              <NavIcon name="shield" size={30} />
              <span><strong className="d-block">{t('admin.overview.rolesTitle')}</strong><span className="cx-muted">{t('admin.overview.rolesText')}</span></span>
              <NavIcon name="arrow-right" size={20} />
            </TabLink>
            <TabLink tab="settings" available={available} className="cx-card cx-admin-shortcut">
              <NavIcon name="sliders" size={30} />
              <span><strong className="d-block">{t('admin.tabs.settings')}</strong><span className="cx-muted">{t('admin.overview.settingsText')}</span></span>
              <NavIcon name="arrow-right" size={20} />
            </TabLink>
            <TabLink tab="audit" available={available} className="cx-card cx-admin-shortcut">
              <NavIcon name="list" size={30} />
              <span><strong className="d-block">{t('admin.tabs.audit')}</strong><span className="cx-muted">{t('admin.overview.auditText')}</span></span>
              <NavIcon name="arrow-right" size={20} />
            </TabLink>
          </div>
          <p className="cx-admin-footnote cx-muted">
            <NavIcon name="info" size={18} />
            {t('admin.overview.footnote')}
          </p>
        </>
      )}
    </div>
  );
}

/** The product's own spelling of the database engines the API names by provider key. */
const databaseName = (provider: string) => ({ sqlite: 'SQLite', postgresql: 'PostgreSQL' } as Record<string, string>)[provider.toLowerCase()] ?? provider;

function StatCard({ icon, label, value, hint, link }: { icon: NavIconName; label: string; value: string; hint?: string | undefined; link: ReactNode }) {
  return (
    <div className="cx-card cx-admin-stat-card">
      <NavIcon name={icon} size={36} />
      <div className="cx-admin-stat-value">
        <span className="cx-muted">{label}</span>
        <strong>{value}</strong>
        {hint && <span className="cx-meta d-block">{hint}</span>}
      </div>
      {link}
    </div>
  );
}

// ---- Users -----------------------------------------------------------------------------------

const pageSizes = [8, 25, 50] as const;

/** "Users 301" with a subtitle: the heading of a redesigned administration section (2026-11-24). */
function SectionHeading({ id, title, count, subtitle, actions }: { id: string; title: string; count?: number | undefined; subtitle: string; actions?: ReactNode }) {
  return (
    <div className="cx-admin-section-head">
      <div>
        <h2 id={id} className="cx-admin-heading mb-1">
          {title}
          {count !== undefined && <> <span className="cx-admin-count">{count}</span></>}
        </h2>
        <p className="cx-muted mb-0">{subtitle}</p>
      </div>
      {actions}
    </div>
  );
}

/** "Showing 1–8 of 301 users", page size and page numbers under a server-paged table. */
function PageFooter({ skip, take, total, noun, onSkip, onTake }: {
  skip: number;
  take: number;
  total: number;
  noun: 'users' | 'organizations';
  onSkip: (skip: number) => void;
  onTake: (take: number) => void;
}) {
  const { t } = useTranslation();
  const pages = Math.max(1, Math.ceil(total / take));
  return (
    <div className="cx-table-footer">
      <span className="cx-muted">{t(`admin.showing.${noun}`, { from: total === 0 ? 0 : skip + 1, to: Math.min(skip + take, total), count: total })}</span>
      <div className="cx-table-paging">
        <label className="visually-hidden" htmlFor={`admin-${noun}-page-size`}>{t('contacts.perPageLabel')}</label>
        <select id={`admin-${noun}-page-size`} className="form-select form-select-sm" value={take} onChange={(e) => onTake(Number(e.target.value))}>
          {pageSizes.map((size) => <option key={size} value={size}>{t('contacts.perPage', { count: size })}</option>)}
        </select>
        {pages > 1 && <Pagination page={Math.floor(skip / take) + 1} pages={pages} onPage={(page) => onSkip((page - 1) * take)} />}
      </div>
    </div>
  );
}

function Users({ canManage, currentUserId }: { canManage: boolean; currentUserId: string }) {
  const { t, i18n } = useTranslation();
  const [query, setQuery] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [status, setStatus] = useState('all');
  const [role, setRole] = useState('all');
  const [sort, setSort] = useState('nameAsc');
  const [skip, setSkip] = useState(0);
  const [take, setTake] = useState<number>(pageSizes[0]);
  const [notice, setNotice] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const { data, setData, error, setError } = useLoad(() => api.searchUsers(submitted, status, skip, take, role, sort), [submitted, status, skip, take, role, sort]);

  const replace = (updated: api.AdminUser) => setData((page) => page && { ...page, items: page.items.map((u) => (u.id === updated.id ? updated : u)) });
  const act = async (action: () => Promise<api.AdminUser | unknown>, success?: string) => {
    setError(null);
    setNotice(null);
    try {
      const result = await action();
      if (result && typeof result === 'object' && 'email' in result) replace(result as api.AdminUser);
      if (success) setNotice(success);
    } catch (err) {
      setError(err);
    }
  };

  const actions: UserActions = {
    enable: (u) => void act(() => api.enableUser(u.id)),
    disable: (u) => {
      if (window.confirm(t('admin.users.disableConfirm', { name: u.displayName }))) void act(() => api.disableUser(u.id));
    },
    signOut: (u) => void act(async () => {
      await api.revokeUserSessions(u.id);
      return { ...u, activeSessions: 0 };
    }, t('admin.users.signedOut', { name: u.displayName })),
    confirm: (u) => void act(() => api.confirmUserEmail(u.id)),
    resend: (u) => void act(() => api.resendConfirmation(u.id), t('admin.users.resent')),
    toggleRole: (u, name) => void act(() => (u.roles.includes(name) ? api.revokeRole(u.id, name) : api.grantRole(u.id, name))),
    remove: (u) => {
      if (window.confirm(t('admin.users.deleteConfirm', { name: u.displayName }))) {
        void act(async () => {
          await api.deleteUser(u.id);
          setSelected(null);
          setData((page) => page && { ...page, total: page.total - 1, items: page.items.filter((x) => x.id !== u.id) });
        }, t('admin.users.deleted', { name: u.displayName }));
      }
    },
  };

  const search = (event: FormEvent) => {
    event.preventDefault();
    setSkip(0);
    setSubmitted(query.trim());
  };
  const filterBy = (set: (value: string) => void) => (value: string) => {
    setSkip(0);
    set(value);
  };
  const shown = data?.items.find((u) => u.id === selected) ?? null;

  return (
    <section className="cx-admin-section" aria-labelledby="admin-users-title">
      <SectionHeading id="admin-users-title" title={t('admin.tabs.users')} count={data?.total} subtitle={t('admin.users.subtitle')} />
      <form className="cx-card cx-admin-filter-bar" onSubmit={search} role="search">
        <label className="cx-search-field">
          <NavIcon name="search" size={18} />
          <span className="visually-hidden">{t('admin.users.search')}</span>
          <input id="admin-user-search" className="form-control" type="search" placeholder={t('admin.users.search')} value={query} onChange={(e) => setQuery(e.target.value)} />
        </label>
        <label className="visually-hidden" htmlFor="admin-user-status">{t('admin.users.statusLabel')}</label>
        <select id="admin-user-status" className="form-select" value={status} onChange={(e) => filterBy(setStatus)(e.target.value)}>
          {['all', 'enabled', 'disabled', 'unconfirmed'].map((f) => <option key={f} value={f}>{t(`admin.users.status.${f}`)}</option>)}
        </select>
        <label className="visually-hidden" htmlFor="admin-user-role">{t('admin.users.roleLabel')}</label>
        <select id="admin-user-role" className="form-select" value={role} onChange={(e) => filterBy(setRole)(e.target.value)}>
          {['all', 'InstallationAdministrator', 'Auditor', 'none'].map((r) => <option key={r} value={r}>{t(`admin.users.roleFilter.${r}`)}</option>)}
        </select>
        <button type="submit" className="btn btn-primary">{t('admin.search')}</button>
      </form>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {notice && <FormAlert tone="success">{notice}</FormAlert>}

      <div className="cx-card cx-table-card">
        <div className="cx-admin-table-tools">
          <label className="visually-hidden" htmlFor="admin-user-sort">{t('admin.sortLabel')}</label>
          <select id="admin-user-sort" className="form-select cx-admin-sort" value={sort} onChange={(e) => filterBy(setSort)(e.target.value)}>
            {['nameAsc', 'nameDesc'].map((s) => <option key={s} value={s}>{t(`admin.sort.${s}`)}</option>)}
          </select>
        </div>
        {!data ? (
          error === null && <Loading />
        ) : data.items.length === 0 ? (
          <p className="cx-muted cx-table-empty">{t('admin.none')}</p>
        ) : (
          <>
            <table className="cx-table cx-admin-table">
              <thead>
                <tr>
                  <th scope="col">{t('admin.users.columns.user')}</th>
                  <th scope="col">{t('admin.users.columns.status')}</th>
                  <th scope="col">{t('admin.users.columns.role')}</th>
                  <th scope="col">{t('admin.users.lastSignIn')}</th>
                  <th scope="col">{t('admin.users.columns.sessions')}</th>
                  <th scope="col">{t('admin.users.columns.organizations')}</th>
                  <th scope="col">{t('admin.users.columns.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((u) => (
                  <tr key={u.id} className={u.disabled ? 'is-disabled' : undefined}>
                    <td data-label={t('admin.users.columns.user')}>
                      <span className="cx-admin-person">
                        <Avatar user={{ id: u.id, avatar: { initials: u.initials } }} size="sm" />
                        <span className="min-w-0">
                          <strong className="d-block text-truncate">{u.displayName}</strong>
                          <span className="cx-muted small d-block text-truncate">{u.email}</span>
                        </span>
                      </span>
                    </td>
                    <td data-label={t('admin.users.columns.status')}><UserStatus user={u} /></td>
                    <td data-label={t('admin.users.columns.role')}><RolePills roles={u.roles} /></td>
                    <td data-label={t('admin.users.lastSignIn')} className="cx-muted">
                      {u.lastSignInAtUtc ? formatDateTime(u.lastSignInAtUtc, i18n.language) : t('admin.never')}
                    </td>
                    <td data-label={t('admin.users.columns.sessions')}>
                      <span title={t('admin.users.sessions', { count: u.activeSessions })}>{u.activeSessions}</span>
                    </td>
                    <td data-label={t('admin.users.columns.organizations')}>{u.organizations}</td>
                    <td className="cx-table-actions">
                      <button type="button" className="btn btn-sm cx-admin-outline" onClick={() => setSelected(u.id)} aria-label={t('admin.users.manageNamed', { name: u.displayName })}>
                        {t('admin.manage')}
                      </button>
                      <RowMenu label={t('admin.users.moreFor', { name: u.displayName })} items={userMenu(u, canManage, currentUserId, t, actions, () => setSelected(u.id))} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <PageFooter skip={skip} take={take} total={data.total} noun="users" onSkip={setSkip} onTake={(size) => { setSkip(0); setTake(size); }} />
          </>
        )}
      </div>
      <p className="cx-admin-footnote cx-muted">
        <NavIcon name="info" size={18} />
        {t('admin.users.footnote')}
      </p>
      {shown && <UserPanel user={shown} canManage={canManage} self={shown.id === currentUserId} actions={actions} onClose={() => setSelected(null)} />}
    </section>
  );
}

interface UserActions {
  enable: (u: api.AdminUser) => void;
  disable: (u: api.AdminUser) => void;
  signOut: (u: api.AdminUser) => void;
  confirm: (u: api.AdminUser) => void;
  resend: (u: api.AdminUser) => void;
  toggleRole: (u: api.AdminUser, role: string) => void;
  remove: (u: api.AdminUser) => void;
}

const installationRoles = ['InstallationAdministrator', 'Auditor'] as const;

function userMenu(u: api.AdminUser, canManage: boolean, currentUserId: string, t: TFunction, on: UserActions, open: () => void): RowMenuItem[] {
  const items: RowMenuItem[] = [{ icon: 'eye', label: t('admin.users.viewDetails'), onSelect: open }];
  if (!canManage) return items;
  items.push({ icon: 'layers', label: t('admin.users.manageRoles'), onSelect: open });
  if (u.activeSessions > 0) items.push({ icon: 'leave', label: t('admin.users.signOut'), onSelect: () => on.signOut(u) });
  if (!u.emailConfirmed) items.push({ icon: 'check', label: t('admin.users.confirm'), onSelect: () => on.confirm(u) });
  if (u.disabled) items.push({ icon: 'check', label: t('admin.users.enableAccount'), onSelect: () => on.enable(u), separated: true });
  else if (u.id !== currentUserId) items.push({ icon: 'block', label: t('admin.users.disableAccount'), onSelect: () => on.disable(u), separated: true });
  if (u.id !== currentUserId) items.push({ icon: 'trash', label: t('admin.users.deleteAccount'), onSelect: () => on.remove(u), danger: true });
  return items;
}

function UserStatus({ user }: { user: api.AdminUser }) {
  const { t } = useTranslation();
  return (
    <span className="cx-admin-status">
      <span className={`cx-dot ${user.disabled ? 'cx-dot-off' : user.lockedOut ? 'cx-dot-warn' : 'cx-dot-ok'}`} aria-hidden="true" />
      {user.disabled ? t('admin.users.disabled') : user.lockedOut ? t('admin.users.lockedOut') : t('admin.users.enabled')}
      {!user.emailConfirmed && <span className="cx-admin-pill is-muted" title={t('admin.users.unconfirmed')}>{t('admin.users.unconfirmedShort')}</span>}
    </span>
  );
}

function RolePills({ roles }: { roles: string[] }) {
  const { t } = useTranslation();
  if (roles.length === 0) return <span className="cx-admin-pill">{t('admin.users.plainUser')}</span>;
  return (
    <span className="cx-tags">
      {roles.map((r) => <span key={r} className="cx-admin-pill is-accent">{t(`admin.roles.names.${r}`)}</span>)}
    </span>
  );
}

/** A user's details, roles and actions in a side panel. */
function UserPanel({ user, canManage, self, actions, onClose }: { user: api.AdminUser; canManage: boolean; self: boolean; actions: UserActions; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  return (
    <Drawer title={user.displayName} label={t('admin.users.detailsFor', { name: user.displayName })} onClose={onClose}>
      <div className="cx-drawer-identity">
        <Avatar user={{ id: user.id, avatar: { initials: user.initials } }} size="md" />
        <p className="cx-drawer-name">{user.displayName}</p>
        <span className="cx-muted">{user.email}</span>
        <UserStatus user={user} />
      </div>
      <dl className="cx-admin-details">
        <dt>{t('admin.users.created')}</dt>
        <dd>{formatDateTime(user.createdAtUtc, i18n.language)}</dd>
        <dt>{t('admin.users.lastSignIn')}</dt>
        <dd>{user.lastSignInAtUtc ? formatDateTime(user.lastSignInAtUtc, i18n.language) : t('admin.never')}</dd>
        <dt>{t('admin.users.columns.sessions')}</dt>
        <dd>{user.activeSessions}</dd>
        <dt>{t('admin.users.columns.organizations')}</dt>
        <dd>{user.organizations}</dd>
      </dl>
      <fieldset className="cx-admin-roles-edit">
        <legend className="cx-section-title">{t('admin.users.installationRoles')}</legend>
        {installationRoles.map((r) => (
          <div key={r} className="form-check form-switch">
            <input id={`role-${user.id}-${r}`} className="form-check-input" type="checkbox" role="switch" checked={user.roles.includes(r)} disabled={!canManage}
              onChange={() => actions.toggleRole(user, r)} />
            <label className="form-check-label" htmlFor={`role-${user.id}-${r}`}>{t(`admin.roles.names.${r}`)}</label>
          </div>
        ))}
      </fieldset>
      {canManage && (
        <div className="cx-drawer-actions">
          {user.activeSessions > 0 && <button type="button" className="btn btn-outline-secondary" onClick={() => actions.signOut(user)}>{t('admin.users.signOut')}</button>}
          {!user.emailConfirmed && (
            <>
              <button type="button" className="btn btn-outline-secondary" onClick={() => actions.confirm(user)}>{t('admin.users.confirm')}</button>
              <button type="button" className="btn btn-outline-secondary" onClick={() => actions.resend(user)}>{t('admin.users.resend')}</button>
            </>
          )}
          {user.disabled ? (
            <button type="button" className="btn btn-outline-primary" onClick={() => actions.enable(user)}>{t('admin.users.enableAccount')}</button>
          ) : (
            !self && <button type="button" className="btn btn-outline-danger" onClick={() => actions.disable(user)}>{t('admin.users.disableAccount')}</button>
          )}
          {!self && <button type="button" className="btn btn-danger" onClick={() => actions.remove(user)}>{t('admin.users.deleteAccount')}</button>}
        </div>
      )}
    </Drawer>
  );
}

/** A modal side panel: Escape, the close button or the backdrop close it; focus returns where it was. */
function Drawer({ title, label, onClose, children }: { title: string; label: string; onClose: () => void; children: ReactNode }) {
  const { t } = useTranslation();
  const close = useRef<HTMLButtonElement>(null);
  const opener = useRef<Element | null>(null);
  useEffect(() => {
    opener.current = document.activeElement;
    close.current?.focus();
    return () => (opener.current as HTMLElement | null)?.focus?.();
  }, []);
  return (
    <div className="cx-drawer-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <aside className="cx-drawer cx-admin-drawer" role="dialog" aria-modal="true" aria-label={label} onKeyDown={(e) => e.key === 'Escape' && onClose()}>
        <div className="cx-drawer-header">
          <h2 className="cx-drawer-title text-truncate">{title}</h2>
          <button ref={close} type="button" className="cx-tool" onClick={onClose} title={t('admin.close')}>
            <NavIcon name="close" size={18} />
            <span className="visually-hidden">{t('admin.close')}</span>
          </button>
        </div>
        {children}
      </aside>
    </div>
  );
}

// ---- Organizations ---------------------------------------------------------------------------

function Organizations({ canManage }: { canManage: boolean }) {
  const { t, i18n } = useTranslation();
  const [query, setQuery] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [sort, setSort] = useState('nameAsc');
  const [skip, setSkip] = useState(0);
  const [take, setTake] = useState<number>(pageSizes[0]);
  const [selected, setSelected] = useState<api.AdminOrganization | null>(null);
  const { data, error, reload } = useLoad(() => api.searchOrganizations(submitted, skip, take, sort), [submitted, skip, take, sort]);

  return (
    <section className="cx-admin-section" aria-labelledby="admin-organizations-title">
      <SectionHeading id="admin-organizations-title" title={t('admin.tabs.organizations')} count={data?.total} subtitle={t('admin.organizations.subtitle')} />
      <form className="cx-card cx-admin-filter-bar" role="search" onSubmit={(e) => { e.preventDefault(); setSkip(0); setSubmitted(query.trim()); }}>
        <label className="cx-search-field">
          <NavIcon name="search" size={18} />
          <span className="visually-hidden">{t('admin.organizations.search')}</span>
          <input id="admin-org-search" className="form-control" type="search" placeholder={t('admin.organizations.search')} value={query} onChange={(e) => setQuery(e.target.value)} />
        </label>
        <button type="submit" className="btn btn-primary">{t('admin.search')}</button>
        <label className="visually-hidden" htmlFor="admin-org-sort">{t('admin.sortLabel')}</label>
        <select id="admin-org-sort" className="form-select" value={sort} onChange={(e) => { setSkip(0); setSort(e.target.value); }}>
          {['nameAsc', 'nameDesc', 'members'].map((s) => <option key={s} value={s}>{t(`admin.sort.${s}`)}</option>)}
        </select>
      </form>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <div className="cx-card cx-table-card">
        {!data ? (
          error === null && <Loading />
        ) : data.items.length === 0 ? (
          <p className="cx-muted cx-table-empty">{t('admin.none')}</p>
        ) : (
          <>
            <table className="cx-table cx-admin-table">
              <thead>
                <tr>
                  <th scope="col">{t('admin.organizations.columns.organization')}</th>
                  <th scope="col">{t('admin.organizations.columns.members')}</th>
                  <th scope="col">{t('admin.organizations.columns.created')}</th>
                  <th scope="col">{t('admin.users.columns.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((o) => (
                  <tr key={o.id}>
                    <td data-label={t('admin.organizations.columns.organization')}>
                      <span className="cx-organization-cell">
                        <span className="cx-organization-mark" aria-hidden="true">{initialsOf(o.name)}</span>
                        <strong className="text-truncate">{o.name}</strong>
                      </span>
                    </td>
                    <td data-label={t('admin.organizations.columns.members')}>
                      <span className="cx-channel-members"><NavIcon name="people" size={18} />{t('admin.organizations.members', { count: o.members })}</span>
                    </td>
                    <td data-label={t('admin.organizations.columns.created')}>
                      <span className="d-block">{new Date(o.createdAtUtc).toLocaleDateString(i18n.language, { day: 'numeric', month: 'short', year: 'numeric' })}</span>
                      <span className="cx-muted small">{new Date(o.createdAtUtc).toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' })}</span>
                    </td>
                    <td className="cx-table-actions">
                      <button type="button" className="btn btn-sm btn-outline-primary cx-icon-button" onClick={() => setSelected(o)}
                        aria-label={t('admin.organizations.viewMembersOf', { name: o.name })}>
                        <NavIcon name="people" size={18} />
                        {t('admin.organizations.viewMembers')}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <PageFooter skip={skip} take={take} total={data.total} noun="organizations" onSkip={setSkip} onTake={(size) => { setSkip(0); setTake(size); }} />
          </>
        )}
      </div>
      <p className="cx-admin-footnote cx-muted">
        <NavIcon name="bank" size={18} />
        {t('admin.organizations.footnote')}
      </p>
      {selected && (
        <Drawer title={selected.name} label={t('admin.organizations.membersOf', { name: selected.name })} onClose={() => setSelected(null)}>
          <OrganizationDetail
            id={selected.id}
            canManage={canManage}
            onChanged={reload}
            onDeleted={() => {
              setSelected(null);
              reload();
            }}
          />
        </Drawer>
      )}
    </section>
  );
}

const initialsOf = (name: string) => name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '?';

function OrganizationDetail({ id, canManage, onChanged, onDeleted }: { id: string; canManage: boolean; onChanged: () => void; onDeleted: () => void }) {
  const { t } = useTranslation();
  const { data, setData, error, setError } = useLoad(() => api.getOrganization(id), [id]);
  const [name, setName] = useState('');
  useEffect(() => {
    if (data) setName(data.organization.name);
  }, [data]);

  const act = async (action: () => Promise<api.AdminOrganizationDetail | void | unknown>) => {
    setError(null);
    try {
      const result = await action();
      if (result && typeof result === 'object' && 'members' in result) setData(result as api.AdminOrganizationDetail);
      else setData(await api.getOrganization(id));
      onChanged();
    } catch (err) {
      setError(err);
    }
  };

  return (
    <div className="cx-admin-detail">
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {!data ? (
        <Loading />
      ) : (
        <>
          {canManage && (
            <form className="cx-admin-rename" onSubmit={(e) => { e.preventDefault(); void act(() => api.renameOrganization(id, name)); }}>
              <label className="form-label" htmlFor={`org-name-${id}`}>{t('admin.organizations.name')}</label>
              <div className="d-flex gap-2">
                <input id={`org-name-${id}`} className="form-control" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
                <button type="submit" className="btn btn-outline-primary" disabled={!name.trim() || name.trim() === data.organization.name}>
                  {t('admin.organizations.rename')}
                </button>
              </div>
            </form>
          )}
          <h3 className="cx-section-title mt-3">{t('admin.organizations.members', { count: data.members.length })}</h3>
          <ul className="list-unstyled mb-0 cx-admin-members">
            {data.members.map((m) => (
              <li key={m.userId} className="cx-admin-member">
                <Avatar user={{ id: m.userId, avatar: { initials: m.initials } }} size="sm" />
                <span className="flex-grow-1 min-w-0">
                  <strong className="d-block text-truncate">{m.displayName}</strong>
                  <span className="cx-muted small d-block text-truncate">{m.email}</span>
                </span>
                {canManage ? (
                  <>
                    <label className="visually-hidden" htmlFor={`role-${id}-${m.userId}`}>
                      {t('admin.organizations.role', { name: m.displayName })}
                    </label>
                    <select id={`role-${id}-${m.userId}`} className="form-select form-select-sm w-auto" value={m.role} onChange={(e) => void act(() => api.setMemberRole(id, m.userId, e.target.value))}>
                      {(['owner', 'administrator', 'member'] as const).map((r) => (
                        <option key={r} value={r}>{t(`organizations.roles.${r}`)}</option>
                      ))}
                    </select>
                    <button
                      type="button"
                      className="cx-tool"
                      title={t('admin.organizations.removeNamed', { name: m.displayName })}
                      onClick={() => {
                        if (window.confirm(t('admin.organizations.removeConfirm', { name: m.displayName }))) void act(() => api.removeMember(id, m.userId));
                      }}
                    >
                      <NavIcon name="trash" size={18} />
                      <span className="visually-hidden">{t('admin.organizations.removeNamed', { name: m.displayName })}</span>
                    </button>
                  </>
                ) : (
                  <span className={`cx-role-pill is-${m.role}`}>{t(`organizations.roles.${m.role}`)}</span>
                )}
              </li>
            ))}
          </ul>
          {canManage && (
            <div className="cx-drawer-actions">
              <button
                type="button"
                className="btn btn-outline-danger"
                onClick={() => {
                  const typed = window.prompt(t('organizations.deletion.prompt', { name: data.organization.name }));
                  if (typed === null) return;
                  setError(null);
                  api.deleteOrganization(id, typed).then(onDeleted, setError);
                }}
              >
                {t('organizations.deletion.submit')}
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

// ---- Roles -----------------------------------------------------------------------------------

/** Every installation permission, in the order of the comparison table. */
const permissionOrder = [
  'installation.read', 'installation.manage', 'users.read', 'users.manage', 'organizations.read', 'organizations.manage', 'audit.read', 'installation.demo_data',
] as const;

function RolesView({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation();
  const { data, error } = useLoad(api.getRoles, []);
  const roleIcon: Record<string, NavIconName> = { InstallationAdministrator: 'shield', Auditor: 'eye' };
  return (
    <section className="cx-card cx-admin-panel" aria-labelledby="admin-roles-title">
      <SectionHeading
        id="admin-roles-title"
        title={t('admin.roles.title')}
        subtitle={t('admin.roles.subtitle')}
        actions={canManage && (
          <Link to="/admin?tab=users" className="btn btn-primary cx-icon-button">
            <NavIcon name="people" size={18} />
            {t('admin.roles.manageAssignments')}
          </Link>
        )}
      />
      <p className="cx-admin-footnote cx-muted mt-2">
        <NavIcon name="info" size={18} />
        {t('admin.roles.intro')}
      </p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {!data ? (
        error === null && <Loading />
      ) : (
        <>
          <div className="cx-admin-role-cards">
            {data.map((role) => (
              <div key={role.name} className="cx-admin-role-card">
                <NavIcon name={roleIcon[role.name] ?? 'shield'} size={28} />
                <span>
                  <span className="cx-admin-role-name">
                    <strong>{t(`admin.roles.names.${role.name}`)}</strong>
                    <span className="cx-admin-pill is-outline">{t('admin.roles.members', { count: role.members })}</span>
                  </span>
                  <span className="cx-muted d-block">{t(`admin.roles.descriptions.${role.name}`)}</span>
                </span>
              </div>
            ))}
          </div>
          <h3 className="cx-admin-subheading">{t('admin.roles.comparison')}</h3>
          <div className="cx-admin-matrix-wrap">
            <table className="cx-admin-matrix">
              <thead>
                <tr>
                  <th scope="col">{t('admin.roles.capability')}</th>
                  {data.map((role) => <th key={role.name} scope="col">{t(`admin.roles.names.${role.name}`)}</th>)}
                </tr>
              </thead>
              <tbody>
                {permissionOrder.map((permission) => (
                  <tr key={permission}>
                    <th scope="row">{t(`admin.permissions.${permission.replace('.', '_')}`)}</th>
                    {data.map((role) => (
                      <td key={role.name}>
                        <Granted granted={role.permissions.includes(permission)} />
                        {permission === 'installation.demo_data' && role.permissions.includes(permission) && (
                          <span className="cx-admin-pill is-outline ms-2">{t('admin.roles.developmentOnly')}</span>
                        )}
                      </td>
                    ))}
                  </tr>
                ))}
                <tr className="is-separated">
                  <th scope="row">{t('admin.roles.conversations')}</th>
                  {data.map((role) => <td key={role.name}><Granted granted={false} /></td>)}
                </tr>
              </tbody>
            </table>
          </div>
          <div className="cx-admin-lock-note">
            <NavIcon name="lock" size={24} />
            <span>
              <strong className="d-block">{t('admin.roles.noConversationsTitle')}</strong>
              <span className="cx-muted">{t('admin.roles.noConversationsText')}</span>
            </span>
          </div>
        </>
      )}
    </section>
  );
}

function Granted({ granted }: { granted: boolean }) {
  const { t } = useTranslation();
  return (
    <span className={`cx-admin-granted${granted ? ' is-granted' : ''}`}>
      <NavIcon name={granted ? 'check' : 'minus'} size={18} />
      {granted ? t('admin.roles.allowed') : t('admin.roles.notGranted')}
    </span>
  );
}

// ---- Live settings ---------------------------------------------------------------------------

const settingGroups = ['access', 'features', 'limits', 'localization', 'audit'] as const;

function Settings({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation();
  const { data, setData, error, setError } = useLoad(api.getSettings, []);
  const [draft, setDraft] = useState<Record<string, unknown>>({});
  const [saved, setSaved] = useState(false);
  const [details, setDetails] = useState<readonly string[]>([]);

  const save = async (values: Record<string, unknown>) => {
    setError(null);
    setDetails([]);
    setSaved(false);
    try {
      setData(await api.saveSettings(values));
      setDraft({});
      setSaved(true);
    } catch (err) {
      setError(err);
      if (err instanceof ApiError) setDetails(err.details);
    }
  };

  const changed = Object.keys(draft).length > 0;
  return (
    <Section
      title={t('admin.tabs.settings')}
      actions={
        canManage && (
          <button type="button" className="btn btn-primary btn-sm" disabled={!changed} onClick={() => void save(draft)}>
            {t('admin.save')}
          </button>
        )
      }
    >
      <p className="cx-muted">{t('admin.settings.intro')}</p>
      {saved && <FormAlert tone="success">{t('admin.settings.saved')}</FormAlert>}
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {details.length > 0 && (
        <ul className="text-danger small">
          {details.map((d) => (
            <li key={d}>{d}</li>
          ))}
        </ul>
      )}
      {!data ? (
        <Loading />
      ) : (
        settingGroups.map((group) => (
          <fieldset key={group} className="cx-admin-group">
            <legend>{t(`admin.settings.groups.${group}`)}</legend>
            {data.filter((s) => s.group === group).map((setting) => (
              <SettingRow
                key={setting.key}
                setting={setting}
                value={setting.key in draft ? draft[setting.key] : setting.value}
                disabled={!canManage || setting.locked}
                fieldErrors={error instanceof ApiError ? error.fieldErrors[setting.key] ?? [] : []}
                onChange={(value) => {
                  setSaved(false);
                  setDraft((d) => ({ ...d, [setting.key]: value }));
                }}
                onReset={canManage && setting.source === 'database' ? () => void save({ [setting.key]: null }) : undefined}
              />
            ))}
          </fieldset>
        ))
      )}
    </Section>
  );
}

function SettingRow({
  setting,
  value,
  disabled,
  fieldErrors,
  onChange,
  onReset,
}: {
  setting: api.RuntimeSetting;
  value: unknown;
  disabled: boolean;
  fieldErrors: readonly string[];
  onChange: (value: unknown) => void;
  onReset: (() => void) | undefined;
}) {
  const { t, i18n } = useTranslation();
  const id = `setting-${setting.key.replace(/:/g, '-')}`;
  const label = t(`admin.settings.keys.${setting.key.replace(/:/g, '_')}`);
  const hint = setting.locked ? t('admin.lockedHint', { source: t(`admin.sources.${setting.source}`) }) : t(`admin.sources.${setting.source}`);

  let control: ReactNode;
  switch (setting.kind) {
    case 'boolean':
      control = (
        <div className="form-check form-switch">
          <input id={id} className="form-check-input" type="checkbox" role="switch" checked={Boolean(value)} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
          <label className="form-check-label" htmlFor={id}>
            {label}
          </label>
        </div>
      );
      break;
    case 'choice':
      control = (
        <select id={id} className="form-select" value={String(value ?? '')} disabled={disabled} onChange={(e) => onChange(e.target.value)}>
          {setting.choices?.map((c) => (
            <option key={c} value={c}>
              {t(`admin.choices.${c}`, { defaultValue: c })}
            </option>
          ))}
        </select>
      );
      break;
    case 'languages':
    case 'extensions': {
      const list = Array.isArray(value) ? (value as string[]) : [];
      control = (
        <div className="cx-admin-checks" role="group" aria-labelledby={`${id}-label`}>
          {setting.choices?.map((c) => (
            <label key={c} className="form-check form-check-inline">
              <input
                className="form-check-input"
                type="checkbox"
                checked={list.includes(c)}
                disabled={disabled}
                onChange={(e) => onChange(e.target.checked ? [...list, c] : list.filter((x) => x !== c))}
              />
              <span className="form-check-label">{setting.kind === 'languages' ? t(`language.${c}`) : c}</span>
            </label>
          ))}
        </div>
      );
      break;
    }
    default: {
      const numeric = typeof value === 'number' ? value : Number(value ?? 0);
      const unit = setting.kind === 'bytes' ? 1024 * 1024 : 1;
      control = (
        <div className="input-group">
          <input
            id={id}
            className="form-control"
            type="number"
            min={setting.min !== null ? Math.ceil(setting.min / unit) : undefined}
            max={setting.max !== null ? Math.floor(setting.max / unit) : undefined}
            step={setting.kind === 'bytes' ? 'any' : 1}
            value={setting.kind === 'bytes' ? Math.round((numeric / unit) * 100) / 100 : numeric}
            disabled={disabled}
            onChange={(e) => onChange(Math.round(Number(e.target.value) * unit))}
          />
          <span className="input-group-text">{setting.kind === 'bytes' ? 'MB' : setting.kind === 'duration' ? t('admin.seconds') : '#'}</span>
        </div>
      );
    }
  }

  return (
    <div className={`cx-admin-setting${setting.locked ? ' is-locked' : ''}`}>
      {setting.kind !== 'boolean' && (
        <label id={`${id}-label`} htmlFor={id} className="form-label">
          {label}
        </label>
      )}
      {control}
      <div className="form-text">
        {hint}
        {setting.kind === 'bytes' && typeof value === 'number' && ` · ${formatBytes(value, i18n.language)}`}
        {onReset && (
          <>
            {' · '}
            <button type="button" className="btn btn-link btn-sm p-0 align-baseline" onClick={onReset}>
              {t('admin.reset')}
            </button>
          </>
        )}
      </div>
      {fieldErrors.map((code) => (
        <div key={code} className="invalid-feedback d-block">
          {t(`admin.errors.${code}`, { defaultValue: t('fieldErrors.invalid') })}
        </div>
      ))}
    </div>
  );
}

// ---- Providers -------------------------------------------------------------------------------

function Providers({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation();
  const { data, setData, error } = useLoad(api.getProviders, []);
  return (
    <>
      <Section title={t('admin.tabs.providers')}>
        <p className="cx-muted mb-0">{t('admin.providers.intro')}</p>
        {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      </Section>
      {!data ? (
        <Loading />
      ) : (
        data.map((group) => (
          <ProviderForm
            key={group.name}
            group={group}
            canManage={canManage}
            onSaved={(updated) => setData((list) => list && list.map((g) => (g.name === updated.name ? updated : g)))}
          />
        ))
      )}
      <SearchIndex canManage={canManage} />
    </>
  );
}

/** The search index (Phase 14): provider in use, changes waiting, and a rebuild. */
function SearchIndex({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation();
  const { data, setData, error, setError } = useLoad(api.getSearchStatus, []);
  const [notice, setNotice] = useState<string | null>(null);
  const rebuild = async () => {
    if (!window.confirm(t('admin.searchIndex.rebuildConfirm'))) return;
    setError(null);
    setNotice(null);
    try {
      const { queued } = await api.rebuildSearchIndex();
      setNotice(t('admin.searchIndex.rebuilt', { count: queued }));
      setData(await api.getSearchStatus());
    } catch (err) {
      setError(err);
    }
  };
  return (
    <Section title={t('admin.searchIndex.title')}>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {notice && <FormAlert tone="success">{notice}</FormAlert>}
      {!data ? (
        <Loading />
      ) : (
        <>
          <p className="mb-2">
            {t('admin.searchIndex.status', { provider: data.provider, count: data.pending })}
            {!data.enabled && <span className="badge text-bg-secondary ms-2">{t('admin.off')}</span>}
          </p>
          <p className="cx-muted">{t('admin.searchIndex.hint')}</p>
          {canManage && (
            <button type="button" className="btn btn-outline-primary" onClick={() => void rebuild()}>
              {t('admin.searchIndex.rebuild')}
            </button>
          )}
        </>
      )}
    </Section>
  );
}

function ProviderForm({ group, canManage, onSaved }: { group: api.ProviderGroup; canManage: boolean; onSaved: (group: api.ProviderGroup) => void }) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<Record<string, unknown>>({});
  const [error, setError] = useState<unknown>(null);
  const [details, setDetails] = useState<readonly string[]>([]);
  const [outcome, setOutcome] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    setDetails([]);
    setOutcome(null);
    try {
      await action();
    } catch (err) {
      setError(err);
      if (err instanceof ApiError) setDetails(err.details);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Section
      title={t(`admin.providers.${group.name}`)}
      actions={group.restartRequired && <span className="badge text-bg-warning">{t('admin.providers.restartRequired')}</span>}
    >
      <form
        className="cx-admin-provider"
        onSubmit={(e) => {
          e.preventDefault();
          void run(async () => {
            onSaved(await api.saveProvider(group.name, draft));
            setDraft({});
            setOutcome('saved');
          });
        }}
      >
        {group.fields.map((field) => {
          const id = `provider-${field.key.replace(/:/g, '-')}`;
          const disabled = !canManage || field.locked;
          const current = field.key in draft ? draft[field.key] : field.value;
          const label = t(`admin.providers.keys.${field.key.replace(/:/g, '_')}`);
          const set = (value: unknown) => setDraft((d) => ({ ...d, [field.key]: value }));
          return (
            <div key={field.key} className="cx-admin-setting">
              {field.kind === 'boolean' ? (
                <div className="form-check form-switch">
                  <input id={id} className="form-check-input" type="checkbox" role="switch" checked={Boolean(current)} disabled={disabled} onChange={(e) => set(e.target.checked)} />
                  <label className="form-check-label" htmlFor={id}>
                    {label}
                  </label>
                </div>
              ) : (
                <>
                  <label htmlFor={id} className="form-label">
                    {label}
                  </label>
                  {field.kind === 'choice' ? (
                    <select id={id} className="form-select" value={String(current ?? '')} disabled={disabled} onChange={(e) => set(e.target.value)}>
                      {!field.hasValue && !(field.key in draft) && <option value="">—</option>}
                      {field.choices?.map((c) => (
                        <option key={c} value={c}>
                          {c}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <input
                      id={id}
                      className="form-control"
                      type={field.kind === 'secret' ? 'password' : field.kind === 'integer' ? 'number' : 'text'}
                      autoComplete={field.kind === 'secret' ? 'new-password' : 'off'}
                      placeholder={field.kind === 'secret' ? t(field.hasValue ? 'admin.providers.secretSet' : 'admin.providers.secretEmpty') : undefined}
                      value={field.kind === 'secret' ? String(draft[field.key] ?? '') : String(current ?? '')}
                      disabled={disabled}
                      onChange={(e) => set(field.kind === 'integer' ? (e.target.value === '' ? null : Number(e.target.value)) : e.target.value)}
                    />
                  )}
                </>
              )}
              <div className="form-text">
                {field.locked ? t('admin.lockedHint', { source: t(`admin.sources.${field.source}`) }) : t(`admin.sources.${field.source}`)}
              </div>
            </div>
          );
        })}
        {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        {details.length > 0 && (
          <ul className="text-danger small">
            {details.map((d) => (
              <li key={d}>{d}</li>
            ))}
          </ul>
        )}
        {outcome && <FormAlert tone={outcome === 'failed' || outcome === 'unavailable' ? 'danger' : 'success'}>{t(`admin.providers.outcomes.${outcome}`)}</FormAlert>}
        {canManage && (
          <div className="d-flex gap-2 flex-wrap">
            <button type="submit" className="btn btn-primary" disabled={busy || Object.keys(draft).length === 0}>
              {t('admin.save')}
            </button>
            {group.name !== 'captcha' && (
              <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void run(async () => setOutcome((await api.testProvider(group.name, draft)).outcome))}>
                {t(group.name === 'email' ? 'admin.providers.testEmail' : 'admin.providers.test')}
              </button>
            )}
          </div>
        )}
      </form>
    </Section>
  );
}

// ---- Audit -----------------------------------------------------------------------------------

function Audit() {
  const { t, i18n } = useTranslation();
  const [action, setAction] = useState('');
  const [outcome, setOutcome] = useState<'all' | 'success' | 'failure'>('all');
  const [entries, setEntries] = useState<api.AuditEntry[] | null>(null);
  const [next, setNext] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);

  const load = useCallback(
    async (before: string | null) => {
      setError(null);
      try {
        const page = await api.getAudit({ action: action.trim(), succeeded: outcome === 'all' ? null : outcome === 'success', before });
        setEntries((current) => (before && current ? [...current, ...page.items] : page.items));
        setNext(page.nextBefore);
      } catch (err) {
        setError(err);
      }
    },
    [action, outcome],
  );

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => void load(null), [outcome]);

  return (
    <Section title={t('admin.tabs.audit')}>
      <form className="cx-admin-filters" role="search" onSubmit={(e) => { e.preventDefault(); void load(null); }}>
        <label className="visually-hidden" htmlFor="audit-action">
          {t('admin.audit.action')}
        </label>
        <input id="audit-action" className="form-control" placeholder={t('admin.audit.actionPlaceholder')} value={action} onChange={(e) => setAction(e.target.value)} />
        <label className="visually-hidden" htmlFor="audit-outcome">
          {t('admin.audit.outcome')}
        </label>
        <select id="audit-outcome" className="form-select" value={outcome} onChange={(e) => setOutcome(e.target.value as typeof outcome)}>
          {(['all', 'success', 'failure'] as const).map((o) => (
            <option key={o} value={o}>
              {t(`admin.audit.outcomes.${o}`)}
            </option>
          ))}
        </select>
        <button type="submit" className="btn btn-primary">
          {t('admin.search')}
        </button>
      </form>
      <p className="cx-meta">{t('admin.audit.intro')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {!entries ? (
        <Loading />
      ) : entries.length === 0 ? (
        <p className="cx-muted mb-0">{t('admin.none')}</p>
      ) : (
        <div className="table-responsive">
          <table className="table table-sm cx-admin-audit">
            <thead>
              <tr>
                <th scope="col">{t('admin.audit.when')}</th>
                <th scope="col">{t('admin.audit.action')}</th>
                <th scope="col">{t('admin.audit.actor')}</th>
                <th scope="col">{t('admin.audit.target')}</th>
                <th scope="col">{t('admin.audit.details')}</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((e) => (
                <tr key={e.id} className={e.succeeded ? undefined : 'is-failure'}>
                  <td className="text-nowrap">{formatDateTime(e.occurredAtUtc, i18n.language)}</td>
                  <td>
                    <code>{e.action}</code> {!e.succeeded && <span className="badge text-bg-danger">{t('admin.audit.failed')}</span>}
                  </td>
                  <td>
                    {e.actorName ?? (e.actorUserId ? t('admin.audit.deletedUser') : t('admin.audit.anonymous'))}
                    {e.ipAddress && <span className="cx-meta d-block">{e.ipAddress}</span>}
                  </td>
                  <td className="text-break">{e.targetType ? `${e.targetType} ${e.targetId ?? ''}` : '—'}</td>
                  <td className="text-break">
                    <code className="cx-admin-details">{e.details ?? ''}</code>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {next && (
        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void load(next)}>
          {t('admin.audit.more')}
        </button>
      )}
    </Section>
  );
}
