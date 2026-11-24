import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { AdminPage } from './AdminPage';

const allPermissions = [
  'installation.read', 'installation.manage', 'installation.demo_data', 'users.read', 'users.manage', 'organizations.read', 'organizations.manage', 'audit.read',
];
const me = (permissions: string[], roles: string[]) => ({
  id: 'u-admin', displayName: 'Admin', avatar: { initials: 'A' }, email: 'admin@example.org', emailConfirmed: true, hasPassword: true,
  externalProviders: [], roles, permissions, preferences: { uiLanguage: 'en' },
});
const ana = (disabled: boolean) => ({
  id: 'u-ana', email: 'ana@example.org', displayName: 'Ana', initials: 'A', emailConfirmed: true, disabled, lockedOut: false, roles: [],
  createdAtUtc: '2026-11-20T10:00:00Z', lastSignInAtUtc: '2026-11-22T10:00:00Z', activeSessions: disabled ? 0 : 2, organizations: 1,
});

const settings = [
  { key: 'PrivateChats:Enabled', group: 'features', kind: 'boolean', value: true, source: 'appsettings', locked: false, min: null, max: null, choices: null },
  { key: 'Conversations:MaxGroupParticipants', group: 'limits', kind: 'integer', value: 300, source: 'environment', locked: true, min: 2, max: 10000, choices: null },
];

describe('AdminPage', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  afterEach(() => vi.unstubAllGlobals());

  it('lets an administrator disable a user after confirming', async () => {
    window.history.replaceState(null, '', '/admin?tab=users');
    vi.stubGlobal('confirm', () => true);
    const calls: string[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me(allPermissions, ['InstallationAdministrator'])),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/admin/users?filter=all&skip=0&take=8': () => json(200, { items: [ana(false)], total: 1 }),
      'GET /api/v1/admin/users?filter=disabled&role=Auditor&skip=0&take=8': () => {
        calls.push('filtered');
        return json(200, { items: [], total: 0 });
      },
      'POST /api/v1/admin/users/u-ana/disable': () => {
        calls.push('disable');
        return json(200, ana(true));
      },
      'POST /api/v1/admin/users/u-ana/roles': (init) => {
        calls.push(`grant ${String(init?.body)}`);
        return json(200, { ...ana(true), roles: ['Auditor'] });
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <AdminPage />
      </AuthProvider>,
    );

    const row = (await screen.findByText('ana@example.org')).closest('tr')!;
    expect(screen.getByRole('heading', { name: 'Users 1' })).toBeInTheDocument();
    expect(within(row).getByText('Enabled')).toBeInTheDocument();
    expect(within(row).getByText('User')).toBeInTheDocument();
    expect(within(row).getByTitle('2 active sessions')).toHaveTextContent('2');
    expect(screen.getByText('Showing 1–1 of 1 user')).toBeInTheDocument();

    // The "…" menu: disable after confirming.
    await user.click(within(row).getByRole('button', { name: 'More actions for Ana' }));
    expect(screen.getByRole('menuitem', { name: 'View details' })).toBeInTheDocument();
    await user.click(screen.getByRole('menuitem', { name: 'Disable account' }));
    expect(await within(row).findByText('Disabled')).toBeInTheDocument();
    await user.click(within(row).getByRole('button', { name: 'More actions for Ana' }));
    expect(screen.getByRole('menuitem', { name: 'Enable account' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(calls).toEqual(['disable']);

    // Manage opens the details panel, where roles are switches.
    await user.click(within(row).getByRole('button', { name: 'Manage Ana' }));
    const panel = screen.getByRole('dialog', { name: 'Details of Ana' });
    await user.click(within(panel).getByRole('switch', { name: 'Auditor' }));
    expect(calls).toContain('grant {"role":"Auditor"}');
    expect(await within(row).findByText('Auditor')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // Status and role are separate filters.
    await user.selectOptions(screen.getByRole('combobox', { name: 'Status' }), 'disabled');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Role' }), 'Auditor');
    expect(await screen.findByText('Nothing found.')).toBeInTheDocument();
    expect(calls).toContain('filtered');
    // Every section the administrator may use is offered.
    expect(screen.getAllByRole('link').map((l) => l.textContent)).toEqual(['Overview', 'Users', 'Organizations', 'Roles', 'Settings', 'Providers', 'Audit log']);
  });

  it('saves live settings and shows locked ones as read-only', async () => {
    window.history.replaceState(null, '', '/admin?tab=settings');
    const saved: unknown[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me(allPermissions, ['InstallationAdministrator'])),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/admin/settings': () => json(200, settings),
      'PUT /api/v1/admin/settings': (init) => {
        saved.push(JSON.parse(String(init?.body)));
        return json(200, [{ ...settings[0], value: false, source: 'database' }, settings[1]]);
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <AdminPage />
      </AuthProvider>,
    );

    const privateChats = await screen.findByRole('switch', { name: 'Private chats' });
    expect(screen.getByLabelText('Largest group')).toBeDisabled();
    expect(screen.getByText(/Set by the environment/)).toBeInTheDocument();
    await user.click(privateChats);
    await user.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText(/The new settings apply now/)).toBeInTheDocument();
    expect(saved).toEqual([{ values: { 'PrivateChats:Enabled': false } }]);
    expect(screen.getByRole('button', { name: 'Reset to default' })).toBeInTheDocument();
  });

  it('shows the installation overview with counts, providers, details and shortcuts', async () => {
    window.history.replaceState(null, '', '/admin');
    const overview = {
      installationId: null, version: '0.1.0', environment: 'Development', users: 301, disabledUsers: 0, organizations: 51,
      databaseProvider: 'SQLite', storageProvider: 'Local', scanningEnabled: false, registrationMode: 'open', restartRequiredFor: [],
    };
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me(allPermissions, ['InstallationAdministrator'])),
      'GET /api/v1/admin/overview': () => json(200, overview),
    });
    render(
      <AuthProvider>
        <AdminPage />
      </AuthProvider>,
    );

    expect(await screen.findByRole('heading', { name: 'Installation overview' })).toBeInTheDocument();
    expect(await screen.findByText('301')).toBeInTheDocument();
    expect(screen.getByText('51')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Manage users/ })).toHaveAttribute('href', '/admin?tab=users');
    expect(screen.getByRole('link', { name: /Manage organizations/ })).toHaveAttribute('href', '/admin?tab=organizations');
    expect(screen.getByRole('link', { name: /Review registration settings/ })).toHaveAttribute('href', '/admin?tab=settings');
    expect(screen.getByText('SQLite')).toBeInTheDocument();
    expect(screen.getByText('Local')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'View provider: Database' })).toHaveAttribute('href', '/admin?tab=providers');
    expect(screen.getByRole('note')).toHaveTextContent('Malware scanning is off');
    expect(screen.getAllByText('Development').length).toBeGreaterThanOrEqual(2); // header badge and details
    expect(screen.getByText('v0.1.0')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Roles & permissions/ })).toHaveAttribute('href', '/admin?tab=roles');
    expect(screen.getByRole('link', { name: /Review administrative actions/ })).toHaveAttribute('href', '/admin?tab=audit');
  });

  it('only links to sections the administrator may open', async () => {
    window.history.replaceState(null, '', '/admin');
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me(['installation.read'], ['Operator'])),
      'GET /api/v1/admin/overview': () => json(200, {
        installationId: null, version: '0.1.0', environment: 'Production', users: 3, disabledUsers: 1, organizations: 1,
        databaseProvider: 'PostgreSql', storageProvider: 'Local', scanningEnabled: true, registrationMode: 'closed', restartRequiredFor: [],
      }),
    });
    render(
      <AuthProvider>
        <AdminPage />
      </AuthProvider>,
    );

    expect(await screen.findByText('1 disabled')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Manage users/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Audit log/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('note')).not.toBeInTheDocument(); // scanning is on
    expect(screen.getByRole('link', { name: /Application settings/ })).toBeInTheDocument();
  });

  it('gives auditors a read-only view', async () => {
    window.history.replaceState(null, '', '/admin?tab=users');
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me(['installation.read', 'users.read', 'organizations.read', 'audit.read'], ['Auditor'])),
      'GET /api/v1/admin/users?filter=all&skip=0&take=8': () => json(200, { items: [ana(false)], total: 1 }),
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <AdminPage />
      </AuthProvider>,
    );

    const row = (await screen.findByText('ana@example.org')).closest('tr')!;
    await user.click(within(row).getByRole('button', { name: 'More actions for Ana' }));
    expect(screen.getAllByRole('menuitem').map((m) => m.textContent)).toEqual(['View details']);
    await user.keyboard('{Escape}');
    await user.click(within(row).getByRole('button', { name: 'Manage Ana' }));
    const panel = screen.getByRole('dialog', { name: 'Details of Ana' });
    expect(within(panel).getByRole('switch', { name: 'Auditor' })).toBeDisabled();
    expect(within(panel).queryByRole('button', { name: 'Delete account' })).not.toBeInTheDocument();
    expect(screen.getByText('Read only')).toBeInTheDocument();
  });

  it('lists organizations with members and shows them in a side panel', async () => {
    window.history.replaceState(null, '', '/admin?tab=organizations');
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me(['installation.read', 'organizations.read'], ['Auditor'])),
      'GET /api/v1/admin/organizations?skip=0&take=8': () =>
        json(200, { items: [{ id: 'o-1', name: 'Blue Bakery', createdAtUtc: '2026-10-23T12:50:00Z', members: 13, owners: 1 }], total: 51 }),
      'GET /api/v1/admin/organizations?sort=members&skip=0&take=8': () => json(200, { items: [], total: 51 }),
      'GET /api/v1/admin/organizations/o-1': () => json(200, {
        organization: { id: 'o-1', name: 'Blue Bakery', createdAtUtc: '2026-10-23T12:50:00Z', members: 1, owners: 1 },
        members: [{ userId: 'u-ana', displayName: 'Ana', initials: 'A', email: 'ana@example.org', role: 'owner', joinedAtUtc: '2026-10-23T12:50:00Z' }],
      }),
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <AdminPage />
      </AuthProvider>,
    );

    const row = (await screen.findByText('Blue Bakery')).closest('tr')!;
    expect(screen.getByRole('heading', { name: 'Organizations 51' })).toBeInTheDocument();
    expect(within(row).getByText('13 members')).toBeInTheDocument();
    expect(screen.getByText('Showing 1–8 of 51 organizations')).toBeInTheDocument();
    await user.click(within(row).getByRole('button', { name: 'View members of Blue Bakery' }));
    const panel = screen.getByRole('dialog', { name: 'Members of Blue Bakery' });
    expect(await within(panel).findByText('ana@example.org')).toBeInTheDocument();
    expect(within(panel).getByText('Owner')).toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Rename' })).not.toBeInTheDocument(); // read-only
    await user.keyboard('{Escape}');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sort' }), 'members');
    expect(await screen.findByText('Nothing found.')).toBeInTheDocument();
  });

  it('compares the installation roles and says that none grants conversation access', async () => {
    window.history.replaceState(null, '', '/admin?tab=roles');
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me(allPermissions, ['InstallationAdministrator'])),
      'GET /api/v1/admin/roles': () => json(200, [
        { name: 'InstallationAdministrator', permissions: allPermissions, members: 1 },
        { name: 'Auditor', permissions: ['installation.read', 'users.read', 'organizations.read', 'audit.read'], members: 0 },
      ]),
    });
    render(
      <AuthProvider>
        <AdminPage />
      </AuthProvider>,
    );

    expect(await screen.findByText('1 person')).toBeInTheDocument();
    expect(screen.getByText('0 people')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Manage assignments' })).toHaveAttribute('href', '/admin?tab=users');
    const change = screen.getByRole('row', { name: /Change settings and providers/ });
    expect(within(change).getAllByRole('cell').map((c) => c.textContent)).toEqual(['Allowed', 'Not granted']);
    const demo = screen.getByRole('row', { name: /Populate demo data/ });
    expect(within(demo).getByText('Development only')).toBeInTheDocument();
    const conversations = screen.getByRole('row', { name: /Access conversations or message content/ });
    expect(within(conversations).getAllByRole('cell').map((c) => c.textContent)).toEqual(['Not granted', 'Not granted']);
    expect(screen.getByText('Administration does not grant conversation access')).toBeInTheDocument();
  });
});
