import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from '../../app/router';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
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

/** Whether the user may open the administration at all (used by the navigation). */
export function canAdminister(permissions: readonly string[] | undefined): boolean {
  return Object.values(tabPermission).some((p) => permissions?.includes(p));
}

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
      <div className="cx-admin-header">
        <h1 className="cx-page-title mb-0">{t('admin.title')}</h1>
        {!can('installation.manage') && !can('users.manage') && <span className="badge text-bg-secondary">{t('admin.readOnly')}</span>}
      </div>
      <nav className="cx-admin-tabs" aria-label={t('admin.sections')}>
        {available.map((name) => (
          <Link key={name} to={`/admin?tab=${name}`} className="cx-admin-tab" aria-current={name === tab ? 'page' : undefined}>
            {t(`admin.tabs.${name}`)}
          </Link>
        ))}
      </nav>
      {tab === 'overview' && <Overview />}
      {tab === 'users' && <Users canManage={can('users.manage')} currentUserId={user!.id} />}
      {tab === 'organizations' && <Organizations canManage={can('organizations.manage')} />}
      {tab === 'roles' && <RolesView />}
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

function Overview() {
  const { t } = useTranslation();
  const { data, error } = useLoad(api.getOverview, []);
  return (
    <Section title={t('admin.tabs.overview')}>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {!data ? (
        <Loading />
      ) : (
        <>
          {data.restartRequiredFor.length > 0 && (
            <FormAlert tone="info">{t('admin.restartPending', { groups: data.restartRequiredFor.map((g) => t(`admin.providers.${g}`)).join(', ') })}</FormAlert>
          )}
          <dl className="cx-admin-stats">
            <Stat label={t('admin.overview.users')} value={String(data.users)} hint={data.disabledUsers ? t('admin.overview.disabled', { count: data.disabledUsers }) : undefined} />
            <Stat label={t('admin.overview.organizations')} value={String(data.organizations)} />
            <Stat label={t('admin.overview.registration')} value={t(`admin.registrationModes.${data.registrationMode}`)} />
            <Stat label={t('admin.overview.database')} value={data.databaseProvider} />
            <Stat label={t('admin.overview.storage')} value={data.storageProvider} />
            <Stat label={t('admin.overview.scanning')} value={data.scanningEnabled ? t('admin.on') : t('admin.off')} />
            <Stat label={t('admin.overview.environment')} value={data.environment} />
            <Stat label={t('admin.overview.version')} value={data.version} />
          </dl>
        </>
      )}
    </Section>
  );
}

function Stat({ label, value, hint }: { label: string; value: string; hint?: string | undefined }) {
  return (
    <div className="cx-admin-stat">
      <dt>{label}</dt>
      <dd>
        {value}
        {hint && <span className="cx-meta d-block">{hint}</span>}
      </dd>
    </div>
  );
}

// ---- Users -----------------------------------------------------------------------------------

function Users({ canManage, currentUserId }: { canManage: boolean; currentUserId: string }) {
  const { t, i18n } = useTranslation();
  const [query, setQuery] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [filter, setFilter] = useState('all');
  const [skip, setSkip] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const { data, setData, error, setError } = useLoad(() => api.searchUsers(submitted, filter, skip), [submitted, filter, skip]);

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

  const search = (event: FormEvent) => {
    event.preventDefault();
    setSkip(0);
    setSubmitted(query.trim());
  };

  return (
    <Section title={t('admin.tabs.users')}>
      <form className="cx-admin-filters" onSubmit={search} role="search">
        <label className="visually-hidden" htmlFor="admin-user-search">
          {t('admin.users.search')}
        </label>
        <input id="admin-user-search" className="form-control" type="search" placeholder={t('admin.users.search')} value={query} onChange={(e) => setQuery(e.target.value)} />
        <label className="visually-hidden" htmlFor="admin-user-filter">
          {t('admin.users.filter')}
        </label>
        <select id="admin-user-filter" className="form-select" value={filter} onChange={(e) => { setSkip(0); setFilter(e.target.value); }}>
          {['all', 'disabled', 'unconfirmed', 'InstallationAdministrator', 'Auditor'].map((f) => (
            <option key={f} value={f}>
              {t(`admin.users.filters.${f}`)}
            </option>
          ))}
        </select>
        <button type="submit" className="btn btn-primary">
          {t('admin.search')}
        </button>
      </form>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {notice && <FormAlert tone="success">{notice}</FormAlert>}
      {!data ? (
        <Loading />
      ) : data.items.length === 0 ? (
        <p className="cx-muted mb-0">{t('admin.none')}</p>
      ) : (
        <>
          <p className="cx-meta">{t('admin.results', { count: data.total })}</p>
          <ul className="list-unstyled cx-admin-list">
            {data.items.map((u) => (
              <li key={u.id} className={`cx-admin-row${u.disabled ? ' is-disabled' : ''}`}>
                <Avatar user={{ id: u.id, avatar: { initials: u.initials } }} size="sm" />
                <div className="cx-admin-row-main">
                  <strong>{u.displayName}</strong>
                  <span className="cx-meta d-block text-truncate">{u.email}</span>
                  <span className="cx-admin-badges">
                    {u.disabled && <span className="badge text-bg-danger">{t('admin.users.disabled')}</span>}
                    {u.lockedOut && <span className="badge text-bg-warning">{t('admin.users.lockedOut')}</span>}
                    {!u.emailConfirmed && <span className="badge text-bg-secondary">{t('admin.users.unconfirmed')}</span>}
                    {u.roles.map((r) => (
                      <span key={r} className="badge text-bg-primary">
                        {t(`admin.roles.names.${r}`)}
                      </span>
                    ))}
                  </span>
                  <span className="cx-meta d-block">
                    {t('admin.users.lastSignIn')}: {u.lastSignInAtUtc ? formatDateTime(u.lastSignInAtUtc, i18n.language) : t('admin.never')} ·{' '}
                    {t('admin.users.sessions', { count: u.activeSessions })} · {t('admin.users.organizations', { count: u.organizations })}
                  </span>
                </div>
                {canManage && (
                  <div className="cx-admin-actions">
                    {u.disabled ? (
                      <button type="button" className="btn btn-sm btn-outline-primary" onClick={() => void act(() => api.enableUser(u.id))}>
                        {t('admin.users.enable')}
                      </button>
                    ) : (
                      u.id !== currentUserId && (
                        <button
                          type="button"
                          className="btn btn-sm btn-outline-danger"
                          onClick={() => {
                            if (window.confirm(t('admin.users.disableConfirm', { name: u.displayName }))) void act(() => api.disableUser(u.id));
                          }}
                        >
                          {t('admin.users.disable')}
                        </button>
                      )
                    )}
                    {u.activeSessions > 0 && (
                      <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(async () => {
                        await api.revokeUserSessions(u.id);
                        return { ...u, activeSessions: 0 };
                      }, t('admin.users.signedOut', { name: u.displayName }))}>
                        {t('admin.users.signOut')}
                      </button>
                    )}
                    {!u.emailConfirmed && (
                      <>
                        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.confirmUserEmail(u.id))}>
                          {t('admin.users.confirm')}
                        </button>
                        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.resendConfirmation(u.id), t('admin.users.resent'))}>
                          {t('admin.users.resend')}
                        </button>
                      </>
                    )}
                    {u.id !== currentUserId && (
                      <button
                        type="button"
                        className="btn btn-sm btn-danger"
                        onClick={() => {
                          if (window.confirm(t('admin.users.deleteConfirm', { name: u.displayName }))) {
                            void act(async () => {
                              await api.deleteUser(u.id);
                              setData((page) => page && { ...page, total: page.total - 1, items: page.items.filter((x) => x.id !== u.id) });
                            }, t('admin.users.deleted', { name: u.displayName }));
                          }
                        }}
                      >
                        {t('admin.users.delete')}
                      </button>
                    )}
                    {(['InstallationAdministrator', 'Auditor'] as const).map((role) =>
                      u.roles.includes(role) ? (
                        <button key={role} type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.revokeRole(u.id, role))}>
                          {t('admin.users.revokeRole', { role: t(`admin.roles.names.${role}`) })}
                        </button>
                      ) : (
                        <button key={role} type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.grantRole(u.id, role))}>
                          {t('admin.users.grantRole', { role: t(`admin.roles.names.${role}`) })}
                        </button>
                      ),
                    )}
                  </div>
                )}
              </li>
            ))}
          </ul>
          <Pager skip={skip} take={25} total={data.total} onChange={setSkip} />
        </>
      )}
    </Section>
  );
}

function Pager({ skip, take, total, onChange }: { skip: number; take: number; total: number; onChange: (skip: number) => void }) {
  const { t } = useTranslation();
  if (total <= take) return null;
  return (
    <div className="d-flex gap-2 align-items-center mt-2">
      <button type="button" className="btn btn-sm btn-outline-secondary" disabled={skip === 0} onClick={() => onChange(Math.max(0, skip - take))}>
        {t('admin.previous')}
      </button>
      <span className="cx-meta">{t('admin.range', { from: skip + 1, to: Math.min(skip + take, total), total })}</span>
      <button type="button" className="btn btn-sm btn-outline-secondary" disabled={skip + take >= total} onClick={() => onChange(skip + take)}>
        {t('admin.next')}
      </button>
    </div>
  );
}

// ---- Organizations ---------------------------------------------------------------------------

function Organizations({ canManage }: { canManage: boolean }) {
  const { t, i18n } = useTranslation();
  const [query, setQuery] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [skip, setSkip] = useState(0);
  const [selected, setSelected] = useState<string | null>(null);
  const { data, error, reload } = useLoad(() => api.searchOrganizations(submitted, skip), [submitted, skip]);

  return (
    <Section title={t('admin.tabs.organizations')}>
      <form className="cx-admin-filters" role="search" onSubmit={(e) => { e.preventDefault(); setSkip(0); setSubmitted(query.trim()); }}>
        <label className="visually-hidden" htmlFor="admin-org-search">
          {t('admin.organizations.search')}
        </label>
        <input id="admin-org-search" className="form-control" type="search" placeholder={t('admin.organizations.search')} value={query} onChange={(e) => setQuery(e.target.value)} />
        <button type="submit" className="btn btn-primary">
          {t('admin.search')}
        </button>
      </form>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {!data ? (
        <Loading />
      ) : data.items.length === 0 ? (
        <p className="cx-muted mb-0">{t('admin.none')}</p>
      ) : (
        <>
          <ul className="list-unstyled cx-admin-list">
            {data.items.map((o) => (
              <li key={o.id} className="cx-admin-row">
                <div className="cx-admin-row-main">
                  <strong>{o.name}</strong>
                  <span className="cx-meta d-block">
                    {t('admin.organizations.members', { count: o.members })} · {t('admin.organizations.since', { date: formatDateTime(o.createdAtUtc, i18n.language) })}
                  </span>
                </div>
                <button type="button" className="btn btn-sm btn-outline-primary" aria-expanded={selected === o.id} onClick={() => setSelected(selected === o.id ? null : o.id)}>
                  {t('admin.organizations.open')}
                </button>
                {selected === o.id && (
                  <OrganizationDetail
                    id={o.id}
                    canManage={canManage}
                    onChanged={reload}
                    onDeleted={() => {
                      setSelected(null);
                      reload();
                    }}
                  />
                )}
              </li>
            ))}
          </ul>
          <Pager skip={skip} take={25} total={data.total} onChange={setSkip} />
        </>
      )}
    </Section>
  );
}

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
            <form className="cx-admin-filters" onSubmit={(e) => { e.preventDefault(); void act(() => api.renameOrganization(id, name)); }}>
              <label className="visually-hidden" htmlFor={`org-name-${id}`}>
                {t('admin.organizations.name')}
              </label>
              <input id={`org-name-${id}`} className="form-control" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
              <button type="submit" className="btn btn-outline-primary" disabled={!name.trim() || name.trim() === data.organization.name}>
                {t('admin.organizations.rename')}
              </button>
            </form>
          )}
          <ul className="list-unstyled mb-0">
            {data.members.map((m) => (
              <li key={m.userId} className="cx-admin-member">
                <span className="flex-grow-1 min-w-0">
                  <strong>{m.displayName}</strong> <span className="cx-meta">{m.email}</span>
                </span>
                {canManage ? (
                  <>
                    <label className="visually-hidden" htmlFor={`role-${id}-${m.userId}`}>
                      {t('admin.organizations.role', { name: m.displayName })}
                    </label>
                    <select id={`role-${id}-${m.userId}`} className="form-select form-select-sm w-auto" value={m.role} onChange={(e) => void act(() => api.setMemberRole(id, m.userId, e.target.value))}>
                      {(['owner', 'administrator', 'member'] as const).map((r) => (
                        <option key={r} value={r}>
                          {t(`organizations.roles.${r}`)}
                        </option>
                      ))}
                    </select>
                    <button
                      type="button"
                      className="btn btn-sm btn-outline-danger"
                      onClick={() => {
                        if (window.confirm(t('admin.organizations.removeConfirm', { name: m.displayName }))) void act(() => api.removeMember(id, m.userId));
                      }}
                    >
                      {t('admin.organizations.remove')}
                    </button>
                  </>
                ) : (
                  <span className="badge text-bg-secondary">{t(`organizations.roles.${m.role}`)}</span>
                )}
              </li>
            ))}
          </ul>
          {canManage && (
            <div className="mt-3">
              <button
                type="button"
                className="btn btn-sm btn-danger"
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

function RolesView() {
  const { t } = useTranslation();
  const { data, error } = useLoad(api.getRoles, []);
  return (
    <Section title={t('admin.tabs.roles')}>
      <p className="cx-muted">{t('admin.roles.intro')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {!data ? (
        <Loading />
      ) : (
        <div className="cx-admin-roles">
          {data.map((role) => (
            <div key={role.name} className="cx-subpanel">
              <h3 className="fs-6 fw-semibold mb-1">{t(`admin.roles.names.${role.name}`)}</h3>
              <p className="cx-meta">{t('admin.roles.members', { count: role.members })}</p>
              <ul className="mb-0">
                {role.permissions.map((p) => (
                  <li key={p}>{t(`admin.permissions.${p.replace('.', '_')}`)}</li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}
    </Section>
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
