import { Fragment, useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from '../../app/router';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon, type NavIconName } from '../../shared/components/NavIcon';
import { Pagination } from '../../shared/components/Pagination';
import { RowMenu, type RowMenuItem } from '../../shared/components/RowMenu';
import { useConfirm } from '../../shared/components/ConfirmDialog';
import { OrganizationMark } from '../organizations/OrganizationMark';
import { formatDateTime } from '../../shared/format';
import { formatBytes } from '../attachments/Attachments';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { ApiError } from '../../shared/api/httpClient';
import * as api from './adminApi';

/**
 * Installation administration: overview, users, organizations, roles, groups, live settings,
 * providers and the audit log. Tabs and actions follow the signed-in user's permissions; the
 * server checks every call again.
 */

const tabs = ['overview', 'users', 'organizations', 'roles', 'groups', 'settings', 'providers', 'audit'] as const;
type Tab = (typeof tabs)[number];

const tabPermission: Record<Tab, string> = {
  overview: 'installation.read',
  users: 'users.read',
  organizations: 'organizations.read',
  roles: 'roles.read',
  groups: 'groups.read',
  settings: 'settings.read',
  providers: 'providers.read',
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
      {tab === 'users' && <Users canManage={can('users.manage')} canReadPermissions={can('permissions.read')} currentUserId={user!.id} />}
      {tab === 'organizations' && <Organizations canManage={can('organizations.manage')} />}
      {tab === 'roles' && <RolesView canManage={can('roles.manage') && can('permissions.manage') && can('permissions.read')} canAssignGroups={can('groups.manage') && can('roles.manage')} canReadPermissions={can('permissions.read')} />}
      {tab === 'groups' && <GroupsView canManage={can('groups.manage')} canAssignRoles={can('roles.manage')} initialRoleId={query.get('role') ?? ''} />}
      {tab === 'settings' && <Settings canManage={can('settings.manage')} />}
      {tab === 'providers' && <Providers canManage={can('providers.manage')} />}
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

function Users({ canManage, canReadPermissions, currentUserId }: { canManage: boolean; canReadPermissions: boolean; currentUserId: string }) {
  const { t, i18n } = useTranslation();
  const [query, setQuery] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [status, setStatus] = useState('all');
  const [sort, setSort] = useState('nameAsc');
  const [skip, setSkip] = useState(0);
  const [take, setTake] = useState<number>(pageSizes[0]);
  const [notice, setNotice] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const { data, setData, error, setError } = useLoad(() => api.searchUsers(submitted, status, skip, take, 'all', sort), [submitted, status, skip, take, sort]);
  const [confirm, confirmDialog] = useConfirm();

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
    disable: (u) => void confirm({
      title: t('confirm.disableUser.title', { name: u.displayName }),
      text: t('confirm.disableUser.text'),
      confirmLabel: t('confirm.disableUser.action'),
    }).then((ok) => { if (ok) void (act(() => api.disableUser(u.id))); }),
    signOut: (u) => void act(async () => {
      await api.revokeUserSessions(u.id);
      return { ...u, activeSessions: 0 };
    }, t('admin.users.signedOut', { name: u.displayName })),
    confirm: (u) => void act(() => api.confirmUserEmail(u.id)),
    resend: (u) => void act(() => api.resendConfirmation(u.id), t('admin.users.resent')),
    remove: (u) => void confirm({
      title: t('confirm.deleteUser.title', { name: u.displayName }),
      text: t('confirm.deleteUser.text'),
      confirmLabel: t('confirm.deleteUser.action'),
    }).then((ok) => { if (ok) void (act(async () => {
      await api.deleteUser(u.id);
      setSelected(null);
      setData((page) => page && { ...page, total: page.total - 1, items: page.items.filter((x) => x.id !== u.id) });
    }, t('admin.users.deleted', { name: u.displayName }))); }),
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
      {confirmDialog}
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
                        <Avatar user={{ id: u.id, avatar: { initials: u.initials, photoUrl: u.photoUrl ?? null } }} size="sm" />
                        <span className="min-w-0">
                          <strong className="d-block text-truncate">{u.displayName}</strong>
                          <span className="cx-muted small d-block text-truncate">{u.email}</span>
                        </span>
                      </span>
                    </td>
                    <td data-label={t('admin.users.columns.status')}><UserStatus user={u} /></td>
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
      {shown && <UserPanel user={shown} canManage={canManage} canReadPermissions={canReadPermissions} self={shown.id === currentUserId} actions={actions} onClose={() => setSelected(null)} />}
    </section>
  );
}

interface UserActions {
  enable: (u: api.AdminUser) => void;
  disable: (u: api.AdminUser) => void;
  signOut: (u: api.AdminUser) => void;
  confirm: (u: api.AdminUser) => void;
  resend: (u: api.AdminUser) => void;
  remove: (u: api.AdminUser) => void;
}

function userMenu(u: api.AdminUser, canManage: boolean, currentUserId: string, t: TFunction, on: UserActions, open: () => void): RowMenuItem[] {
  const items: RowMenuItem[] = [{ icon: 'eye', label: t('admin.users.viewDetails'), onSelect: open }];
  if (!canManage) return items;
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

/** A user's details and account actions in a side panel. */
function UserPanel({ user, canManage, canReadPermissions, self, actions, onClose }: { user: api.AdminUser; canManage: boolean; canReadPermissions: boolean; self: boolean; actions: UserActions; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const [review, setReview] = useState<api.EffectiveAccessReview | null>(null);
  const [reviewContexts, setReviewContexts] = useState<api.EffectiveAccessReviewContext[]>([]);
  const [reviewContext, setReviewContext] = useState<api.EffectiveAccessReviewContext | null>(null);
  const [reviewOpen, setReviewOpen] = useState(false);
  const [reviewLoading, setReviewLoading] = useState(false);
  const [reviewError, setReviewError] = useState(false);
  const loadReview = async (context?: api.EffectiveAccessReviewContext) => {
    if (reviewOpen && !context) { setReviewOpen(false); return; }
    setReviewOpen(true);
    setReviewLoading(true);
    setReviewError(false);
    try {
      const contexts = reviewContexts.length ? reviewContexts : await api.getEffectiveAccessReviewContexts(user.id);
      setReviewContexts(contexts);
      const selected = context ?? reviewContext ?? contexts[0];
      if (!selected) throw new Error('No review context is available.');
      setReviewContext(selected);
      setReview(await api.getEffectiveAccessReview(user.id, selected));
    }
    catch { setReviewError(true); }
    finally { setReviewLoading(false); }
  };
  return (
    <Drawer title={user.displayName} label={t('admin.users.detailsFor', { name: user.displayName })} onClose={onClose}>
      <div className="cx-drawer-identity">
        <Avatar user={{ id: user.id, avatar: { initials: user.initials, photoUrl: user.photoUrl ?? null } }} size="md" />
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
      {canReadPermissions && <section className="cx-admin-access-review" aria-label={t('admin.users.accessReview.title')}>
        <button type="button" className="btn btn-outline-secondary" aria-expanded={reviewOpen} onClick={() => void loadReview()}>
          {reviewOpen ? t('admin.users.accessReview.hide') : t('admin.users.accessReview.show')}
        </button>
        {reviewOpen && <div className="cx-admin-access-review-content" aria-live="polite">
          {reviewLoading && <p className="cx-muted">{t('admin.users.accessReview.loading')}</p>}
          {reviewError && <p role="alert" className="cx-admin-error">{t('admin.users.accessReview.error')}</p>}
          {reviewContexts.length > 1 && <label className="cx-field-label">{t('admin.users.accessReview.context')}
            <select className="form-select" value={reviewContext ? `${reviewContext.scope}:${reviewContext.organizationId ?? ''}:${reviewContext.workspaceId ?? ''}` : ''}
              onChange={event => {
                const selected = reviewContexts.find(context => `${context.scope}:${context.organizationId ?? ''}:${context.workspaceId ?? ''}` === event.target.value);
                if (selected) void loadReview(selected);
              }}>
              {reviewContexts.map(context => <option key={`${context.scope}:${context.organizationId ?? ''}:${context.workspaceId ?? ''}`} value={`${context.scope}:${context.organizationId ?? ''}:${context.workspaceId ?? ''}`}>{context.name}</option>)}
            </select>
          </label>}
          {review && <>
            <h3>{t('admin.users.accessReview.groups')}</h3>
            {review.groups.length === 0 ? <p className="cx-muted">{t('admin.users.accessReview.noGroups')}</p> : <ul>
              {review.groups.map(group => <li key={group.id}><strong>{group.name}</strong> <span className="cx-muted">({group.scope})</span> - {group.roles.length ? group.roles.join(', ') : t('admin.users.accessReview.noRoles')}</li>)}
            </ul>}
            <h3>{t('admin.users.accessReview.permissions')}</h3>
            <ul className="cx-admin-access-permissions">
              {review.permissions.map(permission => <li key={permission.key}>
                <span><code>{permission.key}</code></span>
                <span className={`cx-admin-pill ${permission.outcome === 'allowed' ? 'is-success' : permission.outcome === 'denied' ? 'is-danger' : 'is-muted'}`}>{t(`admin.users.accessReview.outcomes.${permission.outcome}`)}</span>
                <small>{permission.decisions.join('; ') || t(`admin.users.accessReview.sources.${permission.source}`)}</small>
              </li>)}
            </ul>
          </>}
        </div>}
      </section>}
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
                        <OrganizationMark organization={o} />
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
  const [confirm, confirmDialog] = useConfirm();
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
      {confirmDialog}
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
                <Avatar user={{ id: m.userId, avatar: { initials: m.initials, photoUrl: m.photoUrl ?? null } }} size="sm" />
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
                        void confirm({
                          title: t('confirm.removeMember.title', { name: m.displayName }),
                          text: t('confirm.removeMember.text'),
                          confirmLabel: t('confirm.removeMember.action'),
                        }).then((ok) => { if (ok) void (act(() => api.removeMember(id, m.userId))); });
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
                  const name = data.organization.name;
                  void confirm({
                    title: t('confirm.deleteOrganization.title', { name }),
                    text: t('confirm.deleteOrganization.text'),
                    confirmLabel: t('organizations.deletion.submit'),
                    typeToConfirm: name,
                    typeLabel: t('organizations.deletion.typeName', { name }),
                  }).then((ok) => {
                    if (!ok) return;
                    setError(null);
                    api.deleteOrganization(id, name).then(onDeleted, setError);
                  });
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

function RolesView({ canManage, canAssignGroups, canReadPermissions }: { canManage: boolean; canAssignGroups: boolean; canReadPermissions: boolean }) {
  const { t } = useTranslation();
  const { data, error } = useLoad(api.getRoles, []);
  const { data: catalog, error: catalogError } = useLoad(() => canReadPermissions ? api.getPermissionCatalog() : Promise.resolve([]), [canReadPermissions]);
  const roleIcon: Record<string, NavIconName> = { InstallationAdministrator: 'shield', Auditor: 'eye' };
  return (
    <section className="cx-card cx-admin-panel" aria-labelledby="admin-roles-title">
      <SectionHeading
        id="admin-roles-title"
        title={t('admin.roles.title')}
        subtitle={t('admin.roles.subtitle')}
        actions={canAssignGroups && (
          <Link to="/admin?tab=groups" className="btn btn-primary cx-icon-button">
            <NavIcon name="people" size={18} />
            {t('admin.roles.manageAssignments')}
          </Link>
        )}
      />
      <p className="cx-admin-footnote cx-muted mt-2">
        <NavIcon name="info" size={18} />
        {t('admin.roles.intro')}
      </p>
      <AccessControlManager canManage={canManage} canAssignGroups={canAssignGroups} catalog={catalog ?? []} />
      {catalogError !== null && <FormAlert tone="danger">{errorMessage(catalogError, t)}</FormAlert>}
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
          {catalog && (
            <>
              <h3 className="cx-admin-subheading">{t('admin.permissionCatalog.title')}</h3>
              <p className="cx-admin-footnote cx-muted">{t('admin.permissionCatalog.intro')}</p>
              <div className="cx-admin-matrix-wrap">
                <table className="cx-admin-matrix">
                  <thead><tr>
                    <th scope="col">{t('admin.permissionCatalog.area')}</th>
                    <th scope="col">{t('admin.permissionCatalog.key')}</th>
                    <th scope="col">{t('admin.permissionCatalog.scope')}</th>
                    <th scope="col">{t('admin.permissionCatalog.state')}</th>
                  </tr></thead>
                  <tbody>{catalog.map((permission) => (
                    <tr key={permission.key}>
                      <th scope="row">{t(`admin.permissionCatalog.areas.${permission.area}`)}</th>
                      <td><code>{permission.key}</code></td>
                      <td>{t(`admin.permissionCatalog.scopes.${permission.scope}`)}</td>
                      <td><span className={`cx-admin-pill ${permission.state === 'implemented' ? 'is-outline' : ''}`}>
                        {t(`admin.permissionCatalog.states.${permission.state}`)}
                      </span></td>
                    </tr>
                  ))}</tbody>
                </table>
              </div>
            </>
          )}
        </>
      )}
    </section>
  );
}

function AccessControlManager({ canManage, canAssignGroups, catalog }: { canManage: boolean; canAssignGroups: boolean; catalog: api.AdminPermissionDefinition[] }) {
  const { t } = useTranslation();
  const roles = useLoad(api.getAccessRoles, []);
  const [roleName, setRoleName] = useState('');
  const [roleDescription, setRoleDescription] = useState('');
  const [permissionEffects, setPermissionEffects] = useState<Record<string, 'allow' | 'deny'>>({});
  const [editingRoleId, setEditingRoleId] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const available = catalog.filter(item => item.scope === 'installation' && item.state === 'implemented');

  const run = async (operation: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await operation();
      roles.reload();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };
  const createRole = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      const request = { name: roleName, description: roleDescription, permissions: Object.entries(permissionEffects).map(([key, effect]) => ({ key, effect })) };
      if (editingRoleId) await api.updateAccessRole(editingRoleId, request);
      else await api.createAccessRole(request);
      setEditingRoleId(null); setRoleName(''); setRoleDescription(''); setPermissionEffects({});
    });
  };
  const editRole = (role: api.AccessControlRole) => {
    setEditingRoleId(role.id);
    setRoleName(role.name);
    setRoleDescription(role.description);
    setPermissionEffects(Object.fromEntries(role.permissions.map(permission => [permission.key, permission.effect])));
  };
  return (
    <section className="cx-admin-subsection" aria-labelledby="custom-access-title">
      <h3 id="custom-access-title" className="cx-admin-subheading">Granular access management</h3>
      <p className="cx-admin-footnote cx-muted">Users belong to groups; groups receive roles. Set each role permission to Allow, Deny, or leave it unassigned. A Deny from any group overrides Allow from every other role.</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {roles.error !== null && <FormAlert tone="danger">{errorMessage(roles.error, t)}</FormAlert>}
      {canManage && <div className="row g-3">
        <div className="col-xl-6">
          <form className="cx-card p-3" onSubmit={createRole}>
            <h4 className="h6">{editingRoleId ? 'Edit installation role' : 'Create installation role'}</h4>
            <label className="form-label" htmlFor="access-role-name">Name</label>
            <input id="access-role-name" className="form-control mb-2" value={roleName} onChange={event => setRoleName(event.target.value)} required maxLength={80} />
            <label className="form-label" htmlFor="access-role-description">Description</label>
            <input id="access-role-description" className="form-control mb-2" value={roleDescription} onChange={event => setRoleDescription(event.target.value)} maxLength={300} />
            <fieldset className="mb-3">
              <legend className="form-label">Permissions</legend>
              <div className="d-grid gap-2">{available.map(item => (
                <label key={item.key} className="d-flex align-items-center justify-content-between gap-3">
                  <span><code>{item.key}</code><span className="cx-muted d-block">{item.description}</span></span>
                  <select className="form-select w-auto" aria-label={`Decision for ${item.key}`} value={permissionEffects[item.key] ?? ''}
                    onChange={event => setPermissionEffects(current => {
                      const next = { ...current };
                      if (event.target.value === '') delete next[item.key];
                      else next[item.key] = event.target.value as 'allow' | 'deny';
                      return next;
                    })}>
                    <option value="">Not assigned</option>
                    <option value="allow">Allow</option>
                    <option value="deny">Deny</option>
                  </select>
                </label>
              ))}</div>
            </fieldset>
            <button className="btn btn-primary" type="submit" disabled={busy || !roleName.trim()}>{editingRoleId ? 'Save role' : 'Create role'}</button>
            {editingRoleId && <button className="btn btn-outline-secondary ms-2" type="button" disabled={busy} onClick={() => { setEditingRoleId(null); setRoleName(''); setRoleDescription(''); setPermissionEffects({}); }}>Cancel</button>}
          </form>
        </div>
      </div>}
      <div className="cx-admin-role-cards mt-3">
        {(roles.data ?? []).map(role => <article className="cx-admin-role-card" key={role.id}>
          <NavIcon name="shield" size={22} />
          <span><strong>{role.name}</strong><span className="cx-muted d-block">{role.description}{role.description && role.permissions.length ? ' · ' : ''}{role.permissions.length ? role.permissions.map(permission => `${permission.key}: ${permission.effect}`).join(', ') : 'No permissions'} · {role.groups} groups</span></span>
          {canManage && <button className="btn btn-sm btn-outline-secondary ms-auto" type="button" onClick={() => editRole(role)}>Edit</button>}
          {canAssignGroups && <Link className="btn btn-sm btn-outline-primary" to={`/admin?tab=groups&role=${encodeURIComponent(role.id)}`}>Manage assignments</Link>}
        </article>)}
      </div>
    </section>
  );
}

function GroupsView({ canManage, canAssignRoles, initialRoleId }: { canManage: boolean; canAssignRoles: boolean; initialRoleId: string }) {
  const { t } = useTranslation();
  const groups = useLoad(api.getAccessGroups, []);
  const roles = useLoad(api.getAccessRoles, []);
  const people = useLoad(() => api.searchUsers('', 'enabled', 0, 100), []);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [initialMembers, setInitialMembers] = useState<string[]>([]);
  const [selectedGroup, setSelectedGroup] = useState('');
  const [selectedUser, setSelectedUser] = useState('');
  const [selectedRole, setSelectedRole] = useState(initialRoleId);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const selectedGroupData = groups.data?.find(group => group.id === selectedGroup) ?? groups.data?.[0];

  const run = async (operation: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await operation();
      groups.reload();
      roles.reload();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const createGroup = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      const created = await api.createAccessGroup({ name, description });
      for (const userId of initialMembers) await api.addAccessGroupMember(created.id, userId);
      setSelectedGroup(created.id);
      setName('');
      setDescription('');
      setInitialMembers([]);
    });
  };

  return (
    <section className="cx-admin-section" aria-labelledby="admin-groups-title">
      <SectionHeading id="admin-groups-title" title="Groups" subtitle="Users are added to groups. Assign roles to groups to grant or deny permissions." />
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {groups.error !== null && <FormAlert tone="danger">{errorMessage(groups.error, t)}</FormAlert>}
      {roles.error !== null && <FormAlert tone="danger">{errorMessage(roles.error, t)}</FormAlert>}
      {canManage && <form className="cx-card p-3 mb-3" onSubmit={createGroup}>
        <h2 className="h6">Create group</h2>
        <div className="row g-3 align-items-end">
          <div className="col-md-4"><label className="form-label" htmlFor="access-group-name">Name</label><input id="access-group-name" className="form-control" value={name} onChange={event => setName(event.target.value)} required maxLength={80} /></div>
          <div className="col-md-4"><label className="form-label" htmlFor="access-group-description">Description</label><input id="access-group-description" className="form-control" value={description} onChange={event => setDescription(event.target.value)} maxLength={300} /></div>
          <div className="col-md-4"><label className="form-label" htmlFor="access-group-users">Add users to this group</label><select id="access-group-users" className="form-select" multiple size={3} value={initialMembers} onChange={event => setInitialMembers(Array.from(event.target.selectedOptions, option => option.value))}>
            {people.data?.items.map(person => <option key={person.id} value={person.id}>{person.displayName} · {person.email}</option>)}
          </select></div>
        </div>
        <button className="btn btn-primary mt-3" type="submit" disabled={busy || !name.trim()}>Create group</button>
      </form>}
      {!groups.data ? (groups.error === null && <Loading />) : groups.data.length === 0 ? (
        <div className="cx-card cx-table-empty cx-muted" role="status">No groups created yet.</div>
      ) : (
        <div className="row g-3">
          <div className="col-lg-5"><div className="cx-card p-2" aria-label="Created groups">
            {groups.data.map(group => <button key={group.id} type="button" className={`cx-admin-settings-group w-100 text-start${(selectedGroupData?.id ?? groups.data?.[0]?.id) === group.id ? ' is-active' : ''}`} onClick={() => setSelectedGroup(group.id)}>
              <strong className="d-block">{group.name}</strong><span className="cx-muted small">{group.memberIds.length} users · {group.roleIds.length} roles</span>
            </button>)}
          </div></div>
          {selectedGroupData && <div className="col-lg-7"><section className="cx-card p-3" aria-label={`Manage group ${selectedGroupData.name}`}>
            <h2 className="h5">{selectedGroupData.name}</h2>
            {selectedGroupData.description && <p className="cx-muted">{selectedGroupData.description}</p>}
            <h3 className="h6 mt-3">Users in this group</h3>
            {selectedGroupData.memberIds.length === 0 ? <p className="cx-muted">No users in this group.</p> : <ul className="list-unstyled">
              {selectedGroupData.memberIds.map(userId => {
                const person = people.data?.items.find(item => item.id === userId);
                return <li className="d-flex align-items-center gap-2 py-1" key={userId}><span className="me-auto">{person ? `${person.displayName} · ${person.email}` : userId}</span>{canManage && <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={() => void run(() => api.removeAccessGroupMember(selectedGroupData.id, userId))}>Remove</button>}</li>;
              })}
            </ul>}
            {canManage && <div className="d-flex flex-wrap gap-2 mt-3 align-items-end">
              <div><label htmlFor="group-add-user" className="form-label">Add user</label><select id="group-add-user" className="form-select" value={selectedUser} onChange={event => setSelectedUser(event.target.value)}><option value="">Select user</option>{people.data?.items.filter(person => !selectedGroupData.memberIds.includes(person.id)).map(person => <option key={person.id} value={person.id}>{person.displayName} · {person.email}</option>)}</select></div>
              <button type="button" className="btn btn-outline-primary" disabled={busy || !selectedUser} onClick={() => void run(async () => { await api.addAccessGroupMember(selectedGroupData.id, selectedUser); setSelectedUser(''); })}>Add user</button>
            </div>}
            <h3 className="h6 mt-4">Roles assigned to this group</h3>
            {selectedGroupData.roleIds.length === 0 ? <p className="cx-muted">No roles assigned.</p> : <ul className="list-unstyled">{selectedGroupData.roleIds.map(roleId => {
              const role = roles.data?.find(item => item.id === roleId);
              return <li className="d-flex align-items-center gap-2 py-1" key={roleId}><span className="me-auto">{role?.name ?? roleId}</span>{canManage && canAssignRoles && <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={() => void run(() => api.removeAccessRoleFromGroup(roleId, selectedGroupData.id))}>Remove role</button>}</li>;
            })}</ul>}
            {canManage && canAssignRoles && <div className="d-flex flex-wrap gap-2 mt-3 align-items-end">
              <div><label htmlFor="group-assign-role" className="form-label">Assign role</label><select id="group-assign-role" className="form-select" value={selectedRole} onChange={event => setSelectedRole(event.target.value)}><option value="">Select role</option>{roles.data?.filter(role => !selectedGroupData.roleIds.includes(role.id)).map(role => <option key={role.id} value={role.id}>{role.name}</option>)}</select></div>
              <button type="button" className="btn btn-outline-primary" disabled={busy || !selectedRole} onClick={() => void run(() => api.assignAccessRoleToGroup(selectedRole, selectedGroupData.id))}>Assign role</button>
            </div>}
          </section></div>}
        </div>
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

const settingGroups = ['access', 'features', 'limits', 'localization', 'audit', 'retention'] as const;
type SettingGroup = (typeof settingGroups)[number];

const groupIcons: Record<SettingGroup, NavIconName> = {
  access: 'user', features: 'grid', limits: 'layers', localization: 'globe', audit: 'list', retention: 'clock',
};

/** How each group's settings are arranged in cards (redesign 2026-11-24); unknown keys land in a last card. */
const settingSections: Record<SettingGroup, { key: string; icon: NavIconName; settings: string[] }[]> = {
  access: [
    { key: 'signup', icon: 'door', settings: ['Identity:RegistrationMode', 'Organizations:AllowCreation'] },
    { key: 'discovery', icon: 'search', settings: ['Discovery:AllowEmailLookup', 'Discovery:AllowOrganizationDirectory'] },
  ],
  features: [
    { key: 'features', icon: 'grid', settings: ['PrivateChats:Enabled', 'Attachments:Enabled', 'Widgets:Enabled', 'Search:Enabled'] },
    { key: 'notifications', icon: 'bell', settings: ['Notifications:EmailEnabled', 'Notifications:PushEnabled'] },
  ],
  limits: [
    { key: 'messages', icon: 'message', settings: ['Messaging:MaxLength', 'Messaging:EditWindow', 'Notifications:EmailDelay', 'Notifications:EmailInterval'] },
    {
      key: 'attachments',
      icon: 'clip',
      settings: ['Attachments:MaxFileSize', 'Attachments:MaxPerMessage', 'Attachments:UserQuota', 'Conversations:MaxGroupParticipants', 'Attachments:AllowedExtensions'],
    },
    { key: 'privateChats', icon: 'lock', settings: ['PrivateChats:MaxFileSize', 'PrivateChats:MaxBytesPerChat'] },
  ],
  localization: [{ key: 'languages', icon: 'globe', settings: ['Localization:DefaultLanguage', 'Localization:SupportedLanguages'] }],
  audit: [{ key: 'audit', icon: 'list', settings: ['Audit:RetentionDays'] }],
  retention: [
    { key: 'retention', icon: 'clock', settings: ['Retention:MessageDays', 'Retention:ClosedSessionDays', 'Retention:UnconfirmedAccountDays', 'Retention:VisitorInactiveDays'] },
  ],
};

/** The unit shown after a number: from the kind, or for counts from the setting. */
function unitOf(setting: api.RuntimeSetting, t: TFunction): string | null {
  if (setting.kind === 'bytes') return 'MB';
  if (setting.kind === 'duration') return t('admin.units.seconds');
  if (setting.kind !== 'integer') return null;
  if (setting.key.endsWith('Days')) return t('admin.units.days');
  const units: Record<string, string> = {
    'Messaging:MaxLength': 'characters', 'Attachments:MaxPerMessage': 'files', 'Conversations:MaxGroupParticipants': 'members',
  };
  return units[setting.key] ? t(`admin.units.${units[setting.key]}`) : null;
}

/** "15 minutes", "1 hour", "2 days" for a number of seconds. */
function humanDuration(seconds: number, t: TFunction): string {
  if (seconds > 0 && seconds % 86400 === 0) return t('admin.duration.days', { count: seconds / 86400 });
  if (seconds > 0 && seconds % 3600 === 0) return t('admin.duration.hours', { count: seconds / 3600 });
  if (seconds > 0 && seconds % 60 === 0) return t('admin.duration.minutes', { count: seconds / 60 });
  return t('admin.duration.seconds', { count: seconds });
}

function Settings({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation();
  const { query } = useLocation();
  const requested = query.get('group') as SettingGroup | null;
  const { data, setData, error, setError } = useLoad(api.getSettings, []);
  const [group, setGroup] = useState<SettingGroup>(requested && settingGroups.includes(requested) ? requested : 'access');
  const [find, setFind] = useState('');
  const [draft, setDraft] = useState<Record<string, unknown>>({});
  const [saved, setSaved] = useState(false);
  const [showSources, setShowSources] = useState(true);
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

  const label = (s: api.RuntimeSetting) => t(`admin.settings.keys.${s.key.replace(/:/g, '_')}`);
  const term = find.trim().toLocaleLowerCase();
  const available = settingGroups.filter((g) => data?.some((s) => s.group === g));
  const current = available.includes(group) ? group : available[0] ?? group;
  const changes = Object.keys(draft).length;
  const field = (setting: api.RuntimeSetting) => (
    <SettingRow
      key={setting.key}
      setting={setting}
      value={setting.key in draft ? draft[setting.key] : setting.value}
      disabled={!canManage || setting.locked}
      showSource={showSources}
      fieldErrors={error instanceof ApiError ? error.fieldErrors[setting.key] ?? [] : []}
      onChange={(value) => {
        setSaved(false);
        setDraft((d) => ({ ...d, [setting.key]: value }));
      }}
      onReset={canManage && setting.source === 'database' ? () => void save({ [setting.key]: null }) : undefined}
    />
  );

  const sectionsOf = (g: SettingGroup) => {
    if (!data) return [];
    const inGroup = data.filter((s) => s.group === g);
    const placed = new Set(settingSections[g].flatMap((s) => s.settings));
    const sections = settingSections[g].map((section) => ({ ...section, items: section.settings.map((k) => inGroup.find((s) => s.key === k)).filter((s): s is api.RuntimeSetting => !!s) }));
    const rest = inGroup.filter((s) => !placed.has(s.key));
    if (rest.length > 0) sections.push({ key: 'more', icon: 'sliders', settings: [], items: rest });
    return sections.filter((s) => s.items.length > 0);
  };

  return (
    <section className="cx-card cx-admin-panel cx-admin-settings" aria-labelledby="admin-settings-title">
      <SectionHeading
        id="admin-settings-title"
        title={t('admin.tabs.settings')}
        subtitle={t('admin.settings.subtitle')}
        actions={(
          <button type="button" className="btn btn-link cx-admin-link p-0" aria-pressed={showSources} onClick={() => setShowSources((v) => !v)}>
            {t(showSources ? 'admin.settings.hideSources' : 'admin.settings.viewSources')}
          </button>
        )}
      />
      <p className="cx-admin-note"><NavIcon name="info" size={18} />{t('admin.settings.intro')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {details.length > 0 && (
        <ul className="text-danger small">
          {details.map((d) => <li key={d}>{d}</li>)}
        </ul>
      )}
      {!data ? (
        error === null && <Loading />
      ) : (
        <div className="cx-admin-settings-layout">
          <nav className="cx-admin-settings-nav" aria-label={t('admin.settings.groupsLabel')}>
            <label className="cx-search-field">
              <NavIcon name="search" size={18} />
              <span className="visually-hidden">{t('admin.settings.find')}</span>
              <input type="search" className="form-control" placeholder={t('admin.settings.find')} value={find} onChange={(e) => setFind(e.target.value)} />
            </label>
            {available.map((g) => (
              <button key={g} type="button" className={`cx-admin-settings-group${g === current && !term ? ' is-active' : ''}`} aria-current={g === current && !term ? 'true' : undefined}
                onClick={() => { setFind(''); setGroup(g); }}>
                <NavIcon name={groupIcons[g]} size={20} />
                {t(`admin.settings.groups.${g}`)}
              </button>
            ))}
          </nav>
          <div className="cx-admin-settings-body">
            {term ? (
              <>
                <h3 className="cx-admin-group-title">{t('admin.settings.results')}</h3>
                {data.filter((s) => label(s).toLocaleLowerCase().includes(term)).length === 0 ? (
                  <p className="cx-muted">{t('admin.none')}</p>
                ) : (
                  <div className="cx-admin-setting-card"><div className="cx-admin-setting-grid">{data.filter((s) => label(s).toLocaleLowerCase().includes(term)).map(field)}</div></div>
                )}
              </>
            ) : (
              <>
                <h3 className="cx-admin-group-title">{t(`admin.settings.groups.${current}`)}</h3>
                <p className="cx-muted">{t(`admin.settings.groupText.${current}`)}</p>
                {sectionsOf(current).map((section) => (
                  <fieldset key={section.key} className="cx-admin-setting-card">
                    <legend className="cx-admin-setting-card-head">
                      <NavIcon name={section.icon} size={24} />
                      <span>
                        <strong className="d-block">{t(`admin.settings.sections.${section.key}.title`)}</strong>
                        <span className="cx-muted small">{t(`admin.settings.sections.${section.key}.text`)}</span>
                      </span>
                    </legend>
                    <div className="cx-admin-setting-grid">{section.items.map(field)}</div>
                  </fieldset>
                ))}
              </>
            )}
          </div>
        </div>
      )}
      {canManage && data && (
        <div className="cx-admin-savebar" role="status">
          <span className="cx-admin-savebar-state">
            <NavIcon name={changes > 0 ? 'edit' : 'check'} size={20} />
            {saved && changes === 0 ? t('admin.settings.saved') : changes > 0 ? t('admin.settings.unsaved', { count: changes }) : t('admin.settings.noChanges')}
          </span>
          <button type="button" className="btn btn-outline-secondary" disabled={changes === 0} onClick={() => { setDraft({}); setError(null); }}>{t('admin.discard')}</button>
          <button type="button" className="btn btn-primary" disabled={changes === 0} onClick={() => void save(draft)}>{t('admin.save')}</button>
        </div>
      )}
      <p className="cx-admin-footnote cx-muted">{t('admin.settings.lockedNote')}</p>
    </section>
  );
}

function SettingRow({
  setting,
  value,
  disabled,
  showSource,
  fieldErrors,
  onChange,
  onReset,
}: {
  setting: api.RuntimeSetting;
  value: unknown;
  disabled: boolean;
  showSource: boolean;
  fieldErrors: readonly string[];
  onChange: (value: unknown) => void;
  onReset: (() => void) | undefined;
}) {
  const { t, i18n } = useTranslation();
  const id = `setting-${setting.key.replace(/:/g, '-')}`;
  const label = t(`admin.settings.keys.${setting.key.replace(/:/g, '_')}`);
  const source = setting.locked ? t('admin.lockedHint', { source: t(`admin.sources.${setting.source}`) }) : t(`admin.sources.${setting.source}`);
  const numeric = typeof value === 'number' ? value : Number(value ?? 0);
  const extra = setting.kind === 'duration'
    ? humanDuration(numeric, t)
    : setting.kind === 'bytes' && numeric === 0 && setting.min === 0
      ? t('admin.settings.zeroUnlimited')
      : setting.kind === 'bytes' ? formatBytes(numeric, i18n.language) : null;

  let control: ReactNode;
  switch (setting.kind) {
    case 'boolean':
      control = (
        <div className="form-check form-switch">
          <input id={id} className="form-check-input" type="checkbox" role="switch" checked={Boolean(value)} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
          <label className="form-check-label" htmlFor={id}>{label}</label>
        </div>
      );
      break;
    case 'choice':
      control = (
        <select id={id} className="form-select" value={String(value ?? '')} disabled={disabled} onChange={(e) => onChange(e.target.value)}>
          {setting.choices?.map((c) => <option key={c} value={c}>{t(`admin.choices.${c}`, { defaultValue: c })}</option>)}
        </select>
      );
      break;
    case 'languages':
    case 'extensions': {
      const list = Array.isArray(value) ? (value as string[]) : [];
      const checks = (
        <div className="cx-admin-checks" role="group" aria-labelledby={`${id}-label`}>
          {setting.choices?.map((c) => (
            <label key={c} className="form-check form-check-inline">
              <input className="form-check-input" type="checkbox" checked={list.includes(c)} disabled={disabled}
                onChange={(e) => onChange(e.target.checked ? [...list, c] : list.filter((x) => x !== c))} />
              <span className="form-check-label">{setting.kind === 'languages' ? t(`language.${c}`) : c}</span>
            </label>
          ))}
        </div>
      );
      control = setting.kind === 'extensions' ? (
        <details className="cx-admin-extensions">
          <summary>
            <NavIcon name="chevron-down" size={18} />
            <span id={`${id}-label`}>{label}</span>
            <span className="cx-muted ms-auto">{t('admin.settings.extensionsCount', { count: list.length })}</span>
          </summary>
          {checks}
        </details>
      ) : checks;
      break;
    }
    default: {
      const unit = setting.kind === 'bytes' ? 1024 * 1024 : 1;
      const suffix = unitOf(setting, t);
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
          {suffix && <span className="input-group-text">{suffix}</span>}
        </div>
      );
    }
  }

  const wide = setting.kind === 'extensions' || setting.kind === 'languages' || setting.kind === 'boolean';
  return (
    <div className={`cx-admin-setting${setting.locked ? ' is-locked' : ''}${wide ? ' is-wide' : ''}`}>
      {setting.kind !== 'boolean' && setting.kind !== 'extensions' && (
        <label id={`${id}-label`} htmlFor={id} className="form-label">{label}</label>
      )}
      {control}
      {(showSource || extra || onReset) && (
        <div className="form-text">
          {setting.locked && <NavIcon name="lock" size={13} />}
          {[showSource ? source : null, extra].filter(Boolean).join(' • ')}
          {onReset && (
            <>
              {' · '}
              <button type="button" className="btn btn-link btn-sm p-0 align-baseline" onClick={onReset}>{t('admin.reset')}</button>
            </>
          )}
        </div>
      )}
      {fieldErrors.map((code) => (
        <div key={code} className="invalid-feedback d-block">{t(`admin.errors.${code}`, { defaultValue: t('fieldErrors.invalid') })}</div>
      ))}
    </div>
  );
}

// ---- Providers -------------------------------------------------------------------------------

type ProviderKey = 'database' | 'search' | api.ProviderGroup['name'];

const providerIcons: Record<ProviderKey, NavIconName> = {
  database: 'database', storage: 'folder', email: 'mail', captcha: 'shield', scanning: 'shield', search: 'search', queue: 'server', ai: 'globe',
};

/** The selected provider of a group: its "…:Provider" field. */
const providerOf = (group: api.ProviderGroup) => {
  const field = group.fields.find((f) => f.key.endsWith(':Provider'));
  return field && field.value !== null && field.value !== '' ? String(field.value) : null;
};

const isOff = (name: string | null) => !name || ['none', 'disabled', 'off'].includes(name.toLowerCase());

/**
 * Providers (redesigned to the owner's mockups 2026-11-24): a card per service with its provider and
 * state, and the selected one's configuration below with a test beside it. Saved values apply after a
 * restart; secrets are never shown.
 */
function Providers({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation();
  const overview = useLoad(api.getOverview, []);
  const { data, setData, error } = useLoad(api.getProviders, []);
  const search = useLoad(api.getSearchStatus, []);
  const aiTranslationProfiles = useLoad(api.getAiTranslationProviders, []);
  const [selected, setSelected] = useState<ProviderKey>('database');
  const [tests, setTests] = useState<Partial<Record<ProviderKey, { outcome: string; at: Date }>>>({});
  const groups = data ?? [];
  const scanningOff = overview.data ? !overview.data.scanningEnabled : false;

  const card = (key: ProviderKey, label: string, value: ReactNode, pill: string | null, state: string, warn = false) => (
    <div key={key} className={`cx-card cx-provider-card${selected === key ? ' is-selected' : ''}`}>
      <NavIcon name={providerIcons[key]} size={30} />
      <div className="cx-provider-card-main">
        <span className="cx-provider-label-row">
          <span className="cx-muted small text-truncate">{label}</span>
          {pill && <span className="cx-admin-pill is-muted">{pill}</span>}
        </span>
        <strong className={warn ? 'cx-admin-state' : undefined}>{value}</strong>
      </div>
      <span className="cx-muted small cx-provider-state">{state}</span>
      <button type="button" className="btn btn-sm cx-admin-outline cx-provider-configure" aria-pressed={selected === key}
        aria-label={t('admin.providers.configureNamed', { name: label })} onClick={() => setSelected(key)}>
        {t('admin.providers.configure')}
      </button>
    </div>
  );

  const testState = (key: ProviderKey) => {
    const test = tests[key];
    return test ? t(`admin.providers.outcomes.${test.outcome}`) : t('admin.providers.notTested');
  };
  const selectedGroup = groups.find((g) => g.name === selected);

  return (
    <section className="cx-admin-section" aria-labelledby="admin-providers-title">
      <SectionHeading id="admin-providers-title" title={t('admin.tabs.providers')} subtitle={t('admin.providers.subtitle')} />
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <div className="cx-provider-cards">
        {overview.data && card('database', t('admin.overview.database'), databaseName(overview.data.databaseProvider), t('admin.providers.selected'),
          t('admin.providers.databaseState'))}
        {groups.map((g) => {
          // Search runs on its built-in default when nothing is configured: the live status names it.
          const activeTranslation = g.name === 'ai' ? aiTranslationProfiles.data?.find(profile => profile.active) : undefined;
          const name = activeTranslation?.name ?? providerOf(g) ?? (g.name === 'search' ? search.data?.provider ?? null : null);
          const value = g.name === 'scanning'
            ? <><span className={`cx-dot ${scanningOff ? 'cx-dot-warn' : 'cx-dot-ok'}`} aria-hidden="true" />{scanningOff ? t('admin.off') : name}</>
            : activeTranslation ? `${activeTranslation.name} · ${activeTranslation.provider}` : name ?? t('admin.providers.none');
          const pill = activeTranslation ? t('admin.providers.selected') : g.restartRequired ? t('admin.providers.restartRequired') : isOff(name) ? t('admin.providers.notConfigured') : t('admin.providers.selected');
          return card(g.name, t(`admin.providers.${g.name}`), value, g.name === 'scanning' ? null : pill, testState(g.name), g.name === 'scanning');
        })}
        {/* Older servers do not list search among the provider groups: show the index status as its own card. */}
        {search.data && !groups.some((g) => g.name === 'search') && card('search', t('admin.providers.search'), search.data.provider,
          search.data.enabled ? t('admin.providers.selected') : t('admin.off'), t('admin.searchIndex.pending', { count: search.data.pending }))}
      </div>

      {!data ? (
        error === null && <Loading />
      ) : selected === 'database' ? (
        overview.data && <DatabasePanel provider={databaseName(overview.data.databaseProvider)} />
      ) : selected === 'search' ? (
        <SearchIndex canManage={canManage} />
      ) : selectedGroup ? (
        <>
          {selectedGroup.name === 'ai' ? <AiTranslationProviders canManage={canManage} /> : <ProviderForm
              key={selectedGroup.name}
              group={selectedGroup}
              canManage={canManage}
              lastTest={tests[selectedGroup.name] ?? null}
              onTested={(outcome) => setTests((current) => ({ ...current, [selectedGroup.name]: { outcome, at: new Date() } }))}
              onSaved={(updated) => setData((list) => list && list.map((g) => (g.name === updated.name ? updated : g)))}
            />}
          {selectedGroup.name === 'search' && <SearchIndex canManage={canManage} />}
        </>
      ) : null}

      <StorageMigrationDryRun canManage={canManage} activeProvider={overview.data?.storageProvider ?? 'Local'} />
      {scanningOff && (
        <div className="cx-admin-warning cx-admin-warning-wide" role="note">
          <NavIcon name="alert" size={22} />
          <span className="flex-grow-1">
            <strong className="d-block">{t('admin.providers.scanningOffTitle')}</strong>
            {t('admin.providers.scanningOffText')}
          </span>
          {selected !== 'scanning' && groups.some((g) => g.name === 'scanning') && (
            <button type="button" className="btn btn-link cx-admin-link" onClick={() => setSelected('scanning')}>
              {t('admin.providers.reviewScanning')}<NavIcon name="arrow-right" size={16} />
            </button>
          )}
        </div>
      )}
    </section>
  );
}

function AiTranslationProviders({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation();
  const [profiles, setProfiles] = useState<api.AiTranslationProviderProfile[] | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [provider, setProvider] = useState<'OpenAICompatible' | 'Ollama'>('OpenAICompatible');
  const [endpoint, setEndpoint] = useState('');
  const [model, setModel] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [maxOutputTokens, setMaxOutputTokens] = useState(4000);
  const [timeoutSeconds, setTimeoutSeconds] = useState(45);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [details, setDetails] = useState<readonly string[]>([]);
  const [outcome, setOutcome] = useState<string | null>(null);
  const [confirm, confirmDialog] = useConfirm();

  const refresh = useCallback(async () => setProfiles(await api.getAiTranslationProviders()), []);
  useEffect(() => { void refresh().catch(setError); }, [refresh]);

  const reset = () => {
    setSelectedId(null);
    setEditingId(null); setName(''); setProvider('OpenAICompatible'); setEndpoint(''); setModel('');
    setApiKey(''); setMaxOutputTokens(4000); setTimeoutSeconds(45); setError(null); setDetails([]); setOutcome(null);
  };
  const edit = (item: api.AiTranslationProviderProfile) => {
    setSelectedId(item.id);
    setEditingId(item.legacy ? null : item.id); setName(item.legacy ? '' : item.name);
    setProvider(item.provider === 'Ollama' ? 'Ollama' : 'OpenAICompatible'); setEndpoint(item.endpoint); setModel(item.model);
    setApiKey(''); setMaxOutputTokens(item.maxOutputTokens); setTimeoutSeconds(item.timeoutSeconds); setError(null); setDetails([]); setOutcome(null);
  };
  const save = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setError(null); setDetails([]); setOutcome(null);
    try {
      const request: api.AiTranslationProviderProfileRequest = { name, provider, endpoint, model, apiKey, maxOutputTokens, timeoutSeconds };
      const saved = editingId ? await api.updateAiTranslationProvider(editingId, request) : await api.createAiTranslationProvider(request);
      await refresh(); edit(saved); setOutcome('saved');
    } catch (cause) { setError(cause); if (cause instanceof ApiError) setDetails(cause.details); }
    finally { setBusy(false); }
  };
  const test = async () => {
    const profileId = editingId ?? (selected?.legacy ? 'legacy' : null);
    if (!profileId) return;
    setBusy(true); setError(null); setDetails([]); setOutcome(null);
    try { setOutcome((await api.testAiTranslationProvider(profileId)).outcome); }
    catch (cause) { setError(cause); if (cause instanceof ApiError) setDetails(cause.details); }
    finally { setBusy(false); }
  };
  const activate = async (id: string) => {
    setBusy(true); setError(null); setDetails([]);
    try { await api.activateAiTranslationProvider(id); await refresh(); }
    catch (cause) { setError(cause); if (cause instanceof ApiError) setDetails(cause.details); }
    finally { setBusy(false); }
  };
  const remove = async (item: api.AiTranslationProviderProfile) => {
    const ok = await confirm({ title: t('admin.aiTranslation.deleteTitle'), text: t('admin.aiTranslation.deleteText', { name: item.name }), confirmLabel: t('admin.aiTranslation.deleteAction') });
    if (!ok) return;
    setBusy(true); setError(null); setDetails([]);
    try { await api.deleteAiTranslationProvider(item.id); await refresh(); if (editingId === item.id) reset(); }
    catch (cause) { setError(cause); if (cause instanceof ApiError) setDetails(cause.details); }
    finally { setBusy(false); }
  };

  const selected = profiles?.find(item => item.id === selectedId);
  return (
    <section className="cx-card cx-provider-panel" aria-labelledby="ai-translation-providers-title">
      <div className="cx-provider-panel-head">
        <NavIcon name="globe" size={30} />
        <span><h3 id="ai-translation-providers-title">{t('admin.aiTranslation.title')}</h3><span className="cx-muted">{t('admin.aiTranslation.hint')}</span></span>
        {canManage && <button type="button" className="btn btn-sm btn-primary ms-auto" onClick={reset}>{t('admin.aiTranslation.add')}</button>}
      </div>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {details.length > 0 && <ul className="text-danger small">{details.map(detail => <li key={detail}>{detail}</li>)}</ul>}
      {profiles === null ? <p className="cx-muted">{t('auth.working')}</p> : profiles.length === 0 ? <p className="cx-muted">{t('admin.aiTranslation.empty')}</p> : (
        <ul className="list-unstyled cx-ai-provider-list">
          {profiles.map(item => (
            <li key={item.id} className="cx-ai-provider-item">
              <button type="button" className="cx-ai-provider-select" onClick={() => edit(item)} aria-current={selectedId === item.id ? 'true' : undefined}>
                <strong>{item.name}</strong><span className="cx-muted">{item.provider} · {item.model}</span>
              </button>
              {item.active && <span className="cx-admin-pill is-accent">{t('admin.aiTranslation.active')}</span>}
              {canManage && !item.active && !item.legacy && <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={() => void activate(item.id)}>{t('admin.aiTranslation.makeActive')}</button>}
              {canManage && !item.active && !item.legacy && <button type="button" className="btn btn-sm btn-outline-danger" disabled={busy} onClick={() => void remove(item)}>{t('admin.aiTranslation.delete')}</button>}
            </li>
          ))}
        </ul>
      )}
      {canManage && (
        <form className="cx-provider-form cx-ai-provider-editor" onSubmit={save}>
          <h4>{editingId ? t('admin.aiTranslation.edit') : t('admin.aiTranslation.new')}</h4>
          {selected?.legacy && <p className="cx-admin-note">{t('admin.aiTranslation.legacyHint')}</p>}
          <div className="cx-admin-setting-grid">
            <label className="cx-admin-setting"><span className="form-label">{t('admin.aiTranslation.name')}</span><input className="form-control" required maxLength={80} value={name} onChange={e => setName(e.target.value)} /></label>
            <label className="cx-admin-setting"><span className="form-label">{t('admin.aiTranslation.provider')}</span><select className="form-select" value={provider} onChange={e => setProvider(e.target.value as typeof provider)}><option value="OpenAICompatible">OpenAI compatible</option><option value="Ollama">Ollama</option></select></label>
            <label className="cx-admin-setting is-wide"><span className="form-label">{t('admin.aiTranslation.endpoint')}</span><input className="form-control" type="url" required value={endpoint} onChange={e => setEndpoint(e.target.value)} placeholder="https://api.example.com/v1" /></label>
            <label className="cx-admin-setting"><span className="form-label">{t('admin.aiTranslation.model')}</span><input className="form-control" required value={model} onChange={e => setModel(e.target.value)} /></label>
            <label className="cx-admin-setting"><span className="form-label">{t('admin.aiTranslation.apiKey')}</span><input className="form-control" type="password" autoComplete="new-password" value={apiKey} onChange={e => setApiKey(e.target.value)} placeholder={selected?.hasApiKey ? t('admin.providers.secretSet') : t('admin.providers.secretEmpty')} />{selected?.hasApiKey && <small className="form-text">{t('admin.aiTranslation.keyPreserved')}</small>}</label>
            <label className="cx-admin-setting"><span className="form-label">{t('admin.aiTranslation.maxOutputTokens')}</span><input className="form-control" type="number" min={64} max={16000} required value={maxOutputTokens} onChange={e => setMaxOutputTokens(Number(e.target.value))} /></label>
            <label className="cx-admin-setting"><span className="form-label">{t('admin.aiTranslation.timeout')}</span><input className="form-control" type="number" min={5} max={120} required value={timeoutSeconds} onChange={e => setTimeoutSeconds(Number(e.target.value))} /></label>
          </div>
          {outcome && <FormAlert tone={outcome === 'saved' || outcome === 'ok' ? 'success' : 'danger'}>{t(outcome === 'saved' ? 'admin.aiTranslation.saved' : `admin.providers.outcomes.${outcome}`, { defaultValue: outcome })}</FormAlert>}
          <div className="d-flex gap-2 flex-wrap mt-3">
            <button type="submit" className="btn btn-primary" disabled={busy}>{t('admin.providers.saveConfiguration')}</button>
            {(editingId || selected?.legacy) && <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void test()}>{t('admin.providers.test')}</button>}
            {editingId && <button type="button" className="btn btn-link" disabled={busy} onClick={reset}>{t('conversations.cancel')}</button>}
          </div>
        </form>
      )}
      {confirmDialog}
    </section>
  );
}

function StorageMigrationDryRun({ canManage, activeProvider }: { canManage: boolean; activeProvider: string }) {
  const { t, i18n } = useTranslation();
  const [source, setSource] = useState(activeProvider);
  const [destination, setDestination] = useState(activeProvider === 'Local' ? 'S3' : 'Local');
  const [report, setReport] = useState<api.StorageMigrationDryRun | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [jobs, setJobs] = useState<api.StorageMigrationJob[]>([]);
  const [jobBusy, setJobBusy] = useState<string | null>(null);
  const providers = ['Local', 'S3', 'AzureBlob'];
  useEffect(() => { setSource(activeProvider); }, [activeProvider]);
  useEffect(() => {
    let alive = true;
    const refresh = async () => { try { const current = await api.getStorageMigrations(); if (alive) setJobs(current); } catch { /* the page still works if migration history is temporarily unavailable */ } };
    void refresh();
    const timer = window.setInterval(() => void refresh(), 3000);
    return () => { alive = false; window.clearInterval(timer); };
  }, []);
  const run = async () => {
    setBusy(true); setError(null); setReport(null);
    try { setReport(await api.dryRunStorageMigration(source, destination)); }
    catch (cause) { setError(errorMessage(cause, t)); }
    finally { setBusy(false); }
  };
  const start = async () => {
    setJobBusy('start'); setError(null);
    try { await api.startStorageMigration(source, destination); setJobs(await api.getStorageMigrations()); }
    catch (cause) { setError(errorMessage(cause, t)); }
    finally { setJobBusy(null); }
  };
  const setPaused = async (job: api.StorageMigrationJob) => {
    setJobBusy(job.id); setError(null);
    try {
      if (job.state === 'Paused') await api.resumeStorageMigration(job.id);
      else await api.pauseStorageMigration(job.id);
      setJobs(await api.getStorageMigrations());
    } catch (cause) { setError(errorMessage(cause, t)); }
    finally { setJobBusy(null); }
  };
  const cutover = async (job: api.StorageMigrationJob) => {
    if (!window.confirm(t('admin.providers.migration.confirmCutover'))) return;
    setJobBusy(job.id); setError(null);
    try { await api.cutoverStorageMigration(job.id); setJobs(await api.getStorageMigrations()); }
    catch (cause) { setError(errorMessage(cause, t)); }
    finally { setJobBusy(null); }
  };
  const startReverseCopy = async (job: api.StorageMigrationJob) => {
    setJobBusy(job.id); setError(null);
    try { await api.startStorageMigration(job.destinationProvider, job.sourceProvider); setJobs(await api.getStorageMigrations()); }
    catch (cause) { setError(errorMessage(cause, t)); }
    finally { setJobBusy(null); }
  };
  const rollback = async (job: api.StorageMigrationJob, reverse: api.StorageMigrationJob) => {
    if (!window.confirm(t('admin.providers.migration.confirmRollback'))) return;
    setJobBusy(job.id); setError(null);
    try { await api.rollbackStorageMigration(job.id, reverse.id); setJobs(await api.getStorageMigrations()); }
    catch (cause) { setError(errorMessage(cause, t)); }
    finally { setJobBusy(null); }
  };
  return (
    <section className="cx-card cx-admin-panel cx-storage-migration-preview" aria-labelledby="storage-migration-preview-title">
      <h3 id="storage-migration-preview-title">{t('admin.providers.migration.title')}</h3>
      <p className="cx-muted">{t('admin.providers.migration.description')}</p>
      <div className="cx-admin-filter-bar">
        <label className="cx-field-label">{t('admin.providers.migration.source')}
          <select className="form-select" value={source} onChange={event => {
            const value = event.target.value;
            setSource(value);
            if (destination === value) setDestination(providers.find(provider => provider !== value)!);
          }}>{providers.map(provider => <option key={provider}>{provider}</option>)}</select>
        </label>
        <label className="cx-field-label">{t('admin.providers.migration.destination')}
          <select className="form-select" value={destination} onChange={event => setDestination(event.target.value)}>{providers.filter(provider => provider !== source).map(provider => <option key={provider}>{provider}</option>)}</select>
        </label>
        <button type="button" className="btn btn-primary" disabled={!canManage || busy} onClick={() => void run()}>
          {busy ? t('admin.providers.migration.running') : t('admin.providers.migration.run')}
        </button>
      </div>
      {error && <FormAlert tone="danger">{error}</FormAlert>}
      {report && <div className="cx-storage-migration-report" aria-live="polite">
        <h4>{t('admin.providers.migration.reportTitle')}</h4>
        <p>{t('admin.providers.migration.summary', { count: report.totalFiles, bytes: formatBytes(report.totalBytes, i18n.language) })}</p>
        <p className="cx-muted">{t('admin.providers.migration.destinationCheck', { state: t(`admin.providers.migration.${report.destinationReachable ? 'reachable' : 'unreachable'}`) })}</p>
        {report.blockers.length > 0 && <div role="alert"><strong>{t('admin.providers.migration.blockers')}</strong><ul>{report.blockers.map((blocker, index) => <li key={`${blocker}-${index}`}>{blocker}</li>)}</ul></div>}
        {report.warnings.length > 0 && <ul className="cx-muted">{report.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul>}
        <ul>{report.files.map(file => <li key={file.id}><code>{file.fileName}</code> — {formatBytes(file.size, i18n.language)}{file.blockers.length > 0 && <strong> ({file.blockers.join(', ')})</strong>}</li>)}</ul>
        {report.filesTruncated && <p className="cx-muted">{t('admin.providers.migration.truncated')}</p>}
        <button type="button" className="btn btn-primary" disabled={!canManage || busy || jobBusy !== null || report.blockers.length > 0} onClick={() => void start()}>
          {jobBusy === 'start' ? t('admin.providers.migration.starting') : t('admin.providers.migration.startCopy')}
        </button>
      </div>}
      <div className="cx-storage-migration-jobs" aria-live="polite">
        <h4>{t('admin.providers.migration.jobs')}</h4>
        {jobs.length === 0 ? <p className="cx-muted">{t('admin.providers.migration.noJobs')}</p> : jobs.map(job => {
          const reverse = jobs.find(candidate => candidate.sourceProvider === job.destinationProvider
            && candidate.destinationProvider === job.sourceProvider && candidate.id !== job.id);
          return (
          <article className="cx-storage-migration-job" key={job.id}>
            <div><strong>{job.sourceProvider} → {job.destinationProvider}</strong><span className="cx-admin-pill is-muted">{job.state}</span></div>
            <p className="cx-muted">{t('admin.providers.migration.progress', { copied: job.copiedFiles, total: job.totalFiles, failed: job.failedFiles, skipped: job.skippedFiles })}</p>
            {(job.state === 'Queued' || job.state === 'Running' || job.state === 'Paused') && <button type="button" className="btn btn-outline-secondary btn-sm" disabled={!canManage || jobBusy !== null} onClick={() => void setPaused(job)}>{jobBusy === job.id ? '…' : job.state === 'Paused' ? t('admin.providers.migration.resume') : t('admin.providers.migration.pause')}</button>}
            {job.state === 'Completed' && job.sourceProvider === activeProvider && <button type="button" className="btn btn-primary btn-sm" disabled={!canManage || jobBusy !== null} onClick={() => void cutover(job)}>{jobBusy === job.id ? t('admin.providers.migration.switching') : t('admin.providers.migration.cutover')}</button>}
            {job.state === 'CutoverComplete' && job.destinationProvider === activeProvider && (!reverse || reverse.state === 'Completed' || reverse.state === 'CompletedWithErrors') && <button type="button" className="btn btn-outline-secondary btn-sm" disabled={!canManage || jobBusy !== null} onClick={() => void startReverseCopy(job)}>{jobBusy === job.id ? t('admin.providers.migration.starting') : t(reverse?.state === 'Completed' ? 'admin.providers.migration.refreshRollbackCopy' : 'admin.providers.migration.startRollbackCopy')}</button>}
            {job.state === 'CutoverComplete' && reverse?.state === 'Completed' && <button type="button" className="btn btn-outline-danger btn-sm" disabled={!canManage || jobBusy !== null} onClick={() => void rollback(job, reverse)}>{jobBusy === job.id ? t('admin.providers.migration.switching') : t('admin.providers.migration.rollback')}</button>}
            {(job.state === 'AwaitingRestart' || job.state === 'RollbackAwaitingRestart') && <p className="cx-muted">{t('admin.providers.migration.restartRequired')}</p>}
            {job.state === 'CutoverComplete' && <p className="cx-muted">{t('admin.providers.migration.rollbackInstructions')}</p>}
            {job.lastError && <p className="text-danger small">{job.lastError}</p>}
          </article>
          );
        })}
      </div>
    </section>
  );
}

/** The database: chosen at setup and kept in the settings file; the page loading is the connection check. */
function DatabasePanel({ provider }: { provider: string }) {
  const { t, i18n } = useTranslation();
  const [checkedAt] = useState(() => new Date());
  return (
    <section className="cx-card cx-provider-panel" aria-labelledby="provider-panel-title">
      <div className="cx-provider-panel-head">
        <NavIcon name="database" size={30} />
        <span>
          <h3 id="provider-panel-title">{t('admin.providers.databaseTitle')} <span className="cx-admin-pill is-muted">{provider}</span></h3>
          <span className="cx-muted">{t('admin.providers.databaseText')}</span>
        </span>
      </div>
      <div className="cx-provider-panel-grid">
        <div className="cx-provider-form">
          <label className="form-label" htmlFor="provider-database-current">{t('admin.providers.currentProvider')}</label>
          <input id="provider-database-current" className="form-control" value={provider} readOnly disabled />
          <label className="form-label mt-3" htmlFor="provider-database-connection">{t('admin.providers.connection')}</label>
          <div className="cx-secret-field">
            <NavIcon name="lock" size={18} />
            <input id="provider-database-connection" className="form-control" value="••••••••••••••••" readOnly disabled />
          </div>
          <p className="form-text">{t('admin.providers.hidden')}</p>
          <p className="cx-muted small mb-0">{t('admin.providers.databaseSetup')}</p>
        </div>
        <aside className="cx-provider-test">
          <h4>{t('admin.providers.connectionCheck')}</h4>
          <div className="cx-provider-test-state">
            <NavIcon name="database" size={44} />
            <strong>{t('admin.providers.databaseAnswered')}</strong>
            <span className="cx-muted small">{t('admin.providers.databaseAnsweredText')}</span>
          </div>
          <p className="cx-provider-last">{t('admin.providers.lastChecked')} {checkedAt.toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' })}</p>
        </aside>
      </div>
    </section>
  );
}

/** The search index (Phase 14): provider in use, changes waiting, and a rebuild. */
function SearchIndex({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation();
  const { data, setData, error, setError } = useLoad(api.getSearchStatus, []);
  const [notice, setNotice] = useState<string | null>(null);
  const [confirm, confirmDialog] = useConfirm();
  const rebuild = async () => {
    const ok = await confirm({
      title: t('confirm.rebuildIndex.title'),
      text: t('confirm.rebuildIndex.text'),
      confirmLabel: t('confirm.rebuildIndex.action'),
      tone: 'primary',
    });
    if (!ok) return;
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
    <section className="cx-card cx-provider-panel" aria-labelledby="provider-panel-title">
      {confirmDialog}
      <div className="cx-provider-panel-head">
        <NavIcon name="search" size={30} />
        <span>
          <h3 id="provider-panel-title">{t('admin.searchIndex.title')}</h3>
          <span className="cx-muted">{t('admin.searchIndex.hint')}</span>
        </span>
      </div>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {notice && <FormAlert tone="success">{notice}</FormAlert>}
      {!data ? (
        <Loading />
      ) : (
        <>
          <p className="mb-3">
            {t('admin.searchIndex.status', { provider: data.provider, count: data.pending })}
            {!data.enabled && <span className="cx-admin-pill is-muted ms-2">{t('admin.off')}</span>}
          </p>
          {canManage && (
            <button type="button" className="btn btn-outline-primary" onClick={() => void rebuild()}>{t('admin.searchIndex.rebuild')}</button>
          )}
        </>
      )}
    </section>
  );
}

/** "Email:Smtp:Host" → "Smtp"; two-part keys belong to the general part of the form. */
const fieldSection = (key: string) => (key.split(':').length > 2 ? key.split(':')[1]! : '');

function ProviderForm({ group, canManage, lastTest, onTested, onSaved }: {
  group: api.ProviderGroup;
  canManage: boolean;
  lastTest: { outcome: string; at: Date } | null;
  onTested: (outcome: string) => void;
  onSaved: (group: api.ProviderGroup) => void;
}) {
  const { t, i18n } = useTranslation();
  const { user } = useAuth();
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

  const providerField = group.fields.find((f) => f.key.endsWith(':Provider'));
  const chosen = String((providerField && (providerField.key in draft ? draft[providerField.key] : providerField.value)) ?? '').toLowerCase();
  // Only the fields of the chosen provider (Email:Smtp:* for SMTP), plus the shared ones.
  const visible = group.name === 'ai' ? group.fields : group.fields.filter((f) => !fieldSection(f.key) || fieldSection(f.key).toLowerCase() === chosen);
  const sections = [...new Set(visible.map((f) => fieldSection(f.key)))];
  const testable = group.name !== 'captcha';

  const fieldControl = (field: api.ProviderField) => {
    const id = `provider-${field.key.replace(/:/g, '-')}`;
    const disabled = !canManage || field.locked;
    const current = field.key in draft ? draft[field.key] : field.value;
    const label = t(`admin.providers.keys.${field.key.replace(/:/g, '_')}`);
    const set = (value: unknown) => setDraft((d) => ({ ...d, [field.key]: value }));
    return (
      <div key={field.key} className={`cx-admin-setting${field.kind === 'boolean' || field.kind === 'secret' ? ' is-wide' : ''}`}>
        {field.kind === 'boolean' ? (
          <div className="form-check form-switch">
            <input id={id} className="form-check-input" type="checkbox" role="switch" checked={Boolean(current)} disabled={disabled} onChange={(e) => set(e.target.checked)} />
            <label className="form-check-label" htmlFor={id}>{label}</label>
          </div>
        ) : (
          <>
            <label htmlFor={id} className="form-label">{label}</label>
            {field.kind === 'choice' ? (
              <select id={id} className="form-select" value={String(current ?? '')} disabled={disabled} onChange={(e) => set(e.target.value)}>
                {!field.hasValue && !(field.key in draft) && <option value="">—</option>}
                {field.choices?.map((c) => <option key={c} value={c}>{c}</option>)}
              </select>
            ) : (
              <div className={field.kind === 'secret' ? 'cx-secret-field' : undefined}>
                {field.kind === 'secret' && <NavIcon name="lock" size={18} />}
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
              </div>
            )}
          </>
        )}
        {field.locked && <div className="form-text"><NavIcon name="lock" size={13} />{t('admin.lockedHint', { source: t(`admin.sources.${field.source}`) })}</div>}
      </div>
    );
  };

  return (
    <section className="cx-card cx-provider-panel" aria-labelledby="provider-panel-title">
      <div className="cx-provider-panel-head">
        <NavIcon name={providerIcons[group.name]} size={30} />
        <span>
          <h3 id="provider-panel-title">
            {t('admin.providers.configurationOf', { name: t(`admin.providers.${group.name}`) })}
            {group.restartRequired && <span className="cx-admin-pill is-accent ms-2">{t('admin.providers.restartRequired')}</span>}
          </h3>
          <span className="cx-muted">{t(`admin.providers.texts.${group.name}`)}</span>
        </span>
      </div>
      <div className={`cx-provider-panel-grid${testable ? '' : ' is-single'}`}>
        <form
          className="cx-provider-form"
          onSubmit={(e) => {
            e.preventDefault();
            void run(async () => {
              onSaved(await api.saveProvider(group.name, draft));
              setDraft({});
              setOutcome('saved');
            });
          }}
        >
          {sections.map((section) => (
            <div key={section || 'general'} className="cx-provider-section">
              {section && <h4 className="cx-provider-section-title">{t(`admin.providers.sections.${section}`, { defaultValue: section })}</h4>}
              <div className="cx-admin-setting-grid">{visible.filter((f) => fieldSection(f.key) === section).map(fieldControl)}</div>
            </div>
          ))}
          {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
          {details.length > 0 && (
            <ul className="text-danger small">
              {details.map((d) => <li key={d}>{d}</li>)}
            </ul>
          )}
          {outcome && <FormAlert tone="success">{t(`admin.providers.outcomes.${outcome}`)}</FormAlert>}
          {canManage && (
            <div className="d-flex gap-2 flex-wrap mt-2">
              <button type="submit" className="btn btn-primary" disabled={busy || Object.keys(draft).length === 0}>{t('admin.providers.saveConfiguration')}</button>
              <button type="button" className="btn btn-outline-secondary" disabled={busy || Object.keys(draft).length === 0} onClick={() => setDraft({})}>
                {t('conversations.cancel')}
              </button>
            </div>
          )}
        </form>
        {testable && (
          <aside className="cx-provider-test" aria-labelledby={`provider-test-${group.name}`}>
            <h4 id={`provider-test-${group.name}`}>{t(group.name === 'email' ? 'admin.providers.testEmailTitle' : 'admin.providers.testTitle')}</h4>
            <div className="cx-provider-test-state">
              <NavIcon name={providerIcons[group.name]} size={44} />
              <span className={`cx-admin-pill ${lastTest && !['failed', 'unavailable'].includes(lastTest.outcome) ? 'is-accent' : 'is-muted'}`}>
                {lastTest ? t(`admin.providers.outcomes.${lastTest.outcome}`) : t('admin.providers.notTested')}
              </span>
              <span className="cx-muted small">{t(group.name === 'email' ? 'admin.providers.testEmailHint' : 'admin.providers.testHint')}</span>
            </div>
            {group.name === 'email' && (
              <>
                <label className="form-label" htmlFor="provider-test-recipient">{t('admin.providers.recipient')}</label>
                <input id="provider-test-recipient" className="form-control mb-1" value={user?.email ?? ''} readOnly disabled />
                <p className="form-text">{t('admin.providers.recipientHint')}</p>
              </>
            )}
            {canManage && (
              <button type="button" className="btn cx-admin-outline w-100" disabled={busy}
                onClick={() => void run(async () => onTested((await api.testProvider(group.name, draft)).outcome))}>
                {t(group.name === 'email' ? 'admin.providers.sendTest' : 'admin.providers.test')}
              </button>
            )}
            <p className="cx-provider-last">
              {t('admin.providers.lastTest')} {lastTest ? lastTest.at.toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' }) : '—'}
            </p>
            {group.name === 'email' && (
              <div className="cx-admin-note mb-0">
                <NavIcon name="info" size={18} />
                <span><strong className="d-block">{t('admin.providers.deliveryTitle')}</strong>{t('admin.providers.deliveryText')}</span>
              </div>
            )}
          </aside>
        )}
      </div>
      <p className="cx-admin-footnote cx-muted mb-0"><NavIcon name="info" size={18} />{t('admin.providers.intro')}</p>
    </section>
  );
}

// ---- Audit -----------------------------------------------------------------------------------

const auditCategories = ['', 'auth.', 'contacts.', 'admin.user.', 'admin.organization.', 'organization.', 'admin.settings.', 'admin.provider.', 'admin.demo_data.', 'admin.search.', 'retention.'] as const;

/** "admin.user.disabled" → "User disabled"; translated where a text exists. */
function auditTitle(action: string, t: TFunction): string {
  const parts = action.split('.');
  const words = (parts[0] === 'admin' || parts[0] === 'auth' ? parts.slice(1) : parts).join(' ').replace(/_/g, ' ');
  const fallback = words.charAt(0).toUpperCase() + words.slice(1);
  return t(`admin.audit.actions.${action.replace(/\./g, '_')}`, { defaultValue: fallback });
}

function auditIcon(action: string): NavIconName {
  if (action.startsWith('auth.sign')) return 'door';
  if (action.startsWith('auth.') || action.startsWith('admin.user.')) return 'user';
  if (action.includes('demo_data')) return 'database';
  if (action.includes('organization')) return 'building';
  if (action.startsWith('admin.settings.')) return 'settings';
  if (action.startsWith('admin.provider.')) return 'layers';
  if (action.startsWith('admin.search.')) return 'search';
  if (action.startsWith('retention.')) return 'clock';
  if (action.startsWith('contacts.')) return 'people';
  return 'list';
}

function Audit() {
  const { t, i18n } = useTranslation();
  const [text, setText] = useState('');
  const [search, setSearch] = useState('');
  const [action, setAction] = useState<string>('');
  const [outcome, setOutcome] = useState<'all' | 'success' | 'failure'>('all');
  const [from, setFrom] = useState<string | undefined>(undefined);
  const [to, setTo] = useState<string | undefined>(undefined);
  const [order, setOrder] = useState<'newest' | 'oldest'>('newest');
  const [skip, setSkip] = useState(0);
  const [take, setTake] = useState<number>(pageSizes[0]);
  const [open, setOpen] = useState<string | null>(null);
  const [retention, setRetention] = useState<number | null>(null);
  const { data, error, reload } = useLoad(
    () => api.getAuditPage({
      q: search, action, succeeded: outcome === 'all' ? null : outcome === 'success',
      from: from ? new Date(`${from}T00:00:00`).toISOString() : null, to: to ? new Date(`${to}T23:59:59.999`).toISOString() : null,
      order, skip, take,
    }),
    [search, action, outcome, from, to, order, skip, take],
  );

  useEffect(() => {
    api.getSettings().then((all) => {
      const days = all.find((s) => s.key === 'Audit:RetentionDays')?.value;
      setRetention(typeof days === 'number' ? days : null);
    }, () => setRetention(null));
  }, []);

  const change = <T,>(set: (value: T) => void) => (value: T) => {
    setSkip(0);
    set(value);
  };
  const filtered = search !== '' || action !== '' || outcome !== 'all' || !!from || !!to;
  const clear = () => {
    setText('');
    setSearch('');
    setAction('');
    setOutcome('all');
    setFrom(undefined);
    setTo(undefined);
    setSkip(0);
  };
  const range = from || to ? dateRangeText(from, to, i18n.language, t) : t('admin.audit.anyTime');

  return (
    <section className="cx-card cx-admin-panel" aria-labelledby="admin-audit-title">
      <SectionHeading id="admin-audit-title" title={t('admin.tabs.audit')} subtitle={t('admin.audit.subtitle')} />
      <p className="cx-admin-note"><NavIcon name="info" size={18} />{t('admin.audit.intro')}</p>
      <form className="cx-admin-audit-search" role="search" onSubmit={(e) => { e.preventDefault(); setSkip(0); setSearch(text.trim()); }}>
        <label className="cx-search-field">
          <NavIcon name="search" size={18} />
          <span className="visually-hidden">{t('admin.audit.searchLabel')}</span>
          <input type="search" className="form-control" placeholder={t('admin.audit.searchPlaceholder')} value={text} onChange={(e) => setText(e.target.value)} />
        </label>
        <button type="submit" className="btn btn-primary">{t('admin.search')}</button>
      </form>
      <div className="cx-admin-audit-filters">
        <label className="visually-hidden" htmlFor="audit-action">{t('admin.audit.action')}</label>
        <select id="audit-action" className="form-select" value={action} onChange={(e) => change(setAction)(e.target.value)}>
          {auditCategories.map((c) => <option key={c} value={c}>{t(`admin.audit.categories.${c ? c.replace(/\./g, '_').replace(/_$/, '') : 'all'}`)}</option>)}
        </select>
        <label className="visually-hidden" htmlFor="audit-outcome">{t('admin.audit.outcome')}</label>
        <select id="audit-outcome" className="form-select" value={outcome} onChange={(e) => change(setOutcome)(e.target.value as typeof outcome)}>
          {(['all', 'success', 'failure'] as const).map((o) => <option key={o} value={o}>{t(`admin.audit.outcomes.${o}`)}</option>)}
        </select>
        <details className="cx-admin-range">
          <summary aria-label={t('admin.audit.dates')}>
            <NavIcon name="calendar" size={18} />
            <span>{range}</span>
            <NavIcon name="chevron-down" size={16} />
          </summary>
          <div className="cx-admin-range-panel">
            <DateFilter label={t('search.after')} value={from} onChange={change(setFrom)} />
            <DateFilter label={t('search.before')} value={to} onChange={change(setTo)} />
          </div>
        </details>
        {filtered && <button type="button" className="btn btn-link cx-admin-link" onClick={clear}>{t('search.clearFilters')}</button>}
        <span className="cx-admin-audit-tools">
          <button type="button" className="cx-tool" title={t('admin.audit.refresh')} onClick={reload}>
            <NavIcon name="refresh" size={20} />
            <span className="visually-hidden">{t('admin.audit.refresh')}</span>
          </button>
          <label className="visually-hidden" htmlFor="audit-order">{t('admin.sortLabel')}</label>
          <select id="audit-order" className="form-select cx-admin-sort" value={order} onChange={(e) => change(setOrder)(e.target.value as typeof order)}>
            <option value="newest">{t('activity.order.newest')}</option>
            <option value="oldest">{t('activity.order.oldest')}</option>
          </select>
        </span>
      </div>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {!data ? (
        error === null && <Loading />
      ) : data.items.length === 0 ? (
        <p className="cx-muted cx-table-empty">{t('admin.none')}</p>
      ) : (
        <>
          <table className="cx-table cx-admin-table cx-audit-table">
            <thead>
              <tr>
                <th scope="col">{t('admin.audit.when')}</th>
                <th scope="col">{t('admin.audit.event')}</th>
                <th scope="col">{t('admin.audit.actor')}</th>
                <th scope="col">{t('admin.audit.target')}</th>
                <th scope="col"><span className="visually-hidden">{t('admin.audit.details')}</span></th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((e) => {
                const expanded = open === e.id;
                const actor = e.actorName ?? (e.actorUserId ? t('admin.audit.deletedUser') : t('admin.audit.anonymous'));
                const when = new Date(e.occurredAtUtc);
                return (
                  <Fragment key={e.id}>
                    <tr className={`${expanded ? 'is-open' : ''}${e.succeeded ? '' : ' is-failure'}`}>
                      <td data-label={t('admin.audit.when')}>
                        <span className="d-block">{when.toLocaleDateString(i18n.language, { day: 'numeric', month: 'short', year: 'numeric' })}</span>
                        <span className="cx-muted small">{when.toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' })}</span>
                      </td>
                      <td data-label={t('admin.audit.event')}>
                        <span className="cx-audit-event">
                          <NavIcon name={auditIcon(e.action)} size={22} />
                          <span className="min-w-0">
                            <strong className="d-block">
                              {auditTitle(e.action, t)}
                              {!e.succeeded && <span className="cx-admin-pill is-danger ms-2">{t('admin.audit.failed')}</span>}
                            </strong>
                            <code className="cx-muted small">{e.action}</code>
                          </span>
                        </span>
                      </td>
                      <td data-label={t('admin.audit.actor')}>
                        <span className="cx-admin-person">
                          <Avatar user={{ id: e.actorUserId ?? 'anonymous', avatar: { initials: initialsOf(actor), photoUrl: e.actorPhotoUrl ?? null } }} size="sm" />
                          <span className="text-truncate">{actor}</span>
                        </span>
                      </td>
                      <td data-label={t('admin.audit.target')} className="text-truncate">
                        {e.targetType ? `${e.targetType}${e.targetId ? ` · ${e.targetId.slice(0, 8)}…` : ''}` : t('admin.audit.installation')}
                      </td>
                      <td className="cx-table-actions">
                        <button type="button" className="cx-tool" aria-expanded={expanded} onClick={() => setOpen(expanded ? null : e.id)}
                          title={t(expanded ? 'admin.audit.hideDetails' : 'admin.audit.showDetails')}>
                          <NavIcon name={expanded ? 'chevron-down' : 'chevron-right'} size={18} />
                          <span className="visually-hidden">{t(expanded ? 'admin.audit.hideDetails' : 'admin.audit.showDetails')}</span>
                        </button>
                      </td>
                    </tr>
                    {expanded && (
                      <tr className="cx-audit-details-row">
                        <td colSpan={5}>
                          <AuditDetails entry={e} actor={actor} />
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
          {data.total !== null && (
            <AuditFooter skip={skip} take={take} total={data.total} onSkip={setSkip} onTake={(size) => { setSkip(0); setTake(size); }} />
          )}
        </>
      )}
      {retention !== null && (
        <p className="cx-admin-retention cx-muted">
          {t('admin.audit.retention', { count: retention })}
          <Link to="/admin?tab=settings&group=audit" className="cx-admin-link">{t('admin.audit.manageRetention')}<NavIcon name="arrow-right" size={16} /></Link>
        </p>
      )}
    </section>
  );
}

function AuditFooter({ skip, take, total, onSkip, onTake }: { skip: number; take: number; total: number; onSkip: (skip: number) => void; onTake: (take: number) => void }) {
  const { t } = useTranslation();
  const pages = Math.max(1, Math.ceil(total / take));
  return (
    <div className="cx-table-footer">
      <span className="cx-muted">{t('admin.showing.events', { from: total === 0 ? 0 : skip + 1, to: Math.min(skip + take, total), count: total })}</span>
      <div className="cx-table-paging">
        <label className="visually-hidden" htmlFor="admin-audit-page-size">{t('contacts.perPageLabel')}</label>
        <select id="admin-audit-page-size" className="form-select form-select-sm" value={take} onChange={(e) => onTake(Number(e.target.value))}>
          {pageSizes.map((size) => <option key={size} value={size}>{t('contacts.perPage', { count: size })}</option>)}
        </select>
        {pages > 1 && <Pagination page={Math.floor(skip / take) + 1} pages={pages} onPage={(page) => onSkip((page - 1) * take)} />}
      </div>
    </div>
  );
}

function AuditDetails({ entry, actor }: { entry: api.AuditEntry; actor: string }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  let details: string | null = entry.details;
  try {
    if (details) details = Object.entries(JSON.parse(details) as Record<string, unknown>).map(([k, v]) => `${k}: ${typeof v === 'object' ? JSON.stringify(v) : String(v)}`).join(' · ');
  } catch {
    // Not JSON: shown as recorded.
  }
  return (
    <dl className="cx-audit-details">
      <div>
        <dt>{t('admin.audit.action')}</dt>
        <dd><code>{entry.action}</code></dd>
        <dt>{t('admin.audit.targetType')}</dt>
        <dd><code>{entry.targetType ?? '—'}</code></dd>
      </div>
      <div>
        <dt>{t('admin.audit.actor')}</dt>
        <dd><code>{actor}</code></dd>
        <dt>{t('admin.audit.targetId')}</dt>
        <dd>
          <code className="text-break">{entry.targetId ?? '—'}</code>
          {entry.targetId && navigator.clipboard && (
            <button type="button" className="cx-tool ms-1" title={t(copied ? 'admin.audit.copied' : 'admin.audit.copy')}
              onClick={() => void navigator.clipboard.writeText(entry.targetId!).then(() => setCopied(true), () => undefined)}>
              <NavIcon name={copied ? 'check' : 'copy'} size={16} />
              <span className="visually-hidden">{t(copied ? 'admin.audit.copied' : 'admin.audit.copy')}</span>
            </button>
          )}
        </dd>
      </div>
      <div>
        <dt>{t('admin.audit.source')}</dt>
        <dd><code>{entry.ipAddress ?? '—'}</code></dd>
        <dt>{t('admin.audit.additional')}</dt>
        <dd>{details || t('admin.audit.noDetails')}</dd>
      </div>
    </dl>
  );
}

/** "23–24 Nov 2026", "From 1 Nov 2026" or "Until 24 Nov 2026". */
function dateRangeText(from: string | undefined, to: string | undefined, language: string, t: TFunction): string {
  const format = new Intl.DateTimeFormat(language, { day: 'numeric', month: 'short', year: 'numeric' });
  const day = (value: string) => new Date(`${value}T00:00:00`);
  if (from && to) return format.formatRange(day(from), day(to));
  if (from) return t('search.sinceDate', { date: format.format(day(from)) });
  return t('search.untilDate', { date: format.format(day(to!)) });
}

/** A date field that applies its value only once the year is complete. */
function DateFilter({ label, value, onChange }: { label: string; value: string | undefined; onChange: (value: string | undefined) => void }) {
  const [draft, setDraft] = useState(value ?? '');
  useEffect(() => setDraft(value ?? ''), [value]);
  return (
    <label className="cx-search-filter">
      <span>{label}</span>
      <input className="form-control" type="date" value={draft} onChange={(e) => {
        const next = e.target.value;
        setDraft(next);
        if (next === '') onChange(undefined);
        else if (Number(next.slice(0, 4)) >= 1900) onChange(next);
      }} />
    </label>
  );
}
