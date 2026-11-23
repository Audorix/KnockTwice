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
      'GET /api/v1/admin/users?filter=all&skip=0&take=25': () => json(200, { items: [ana(false)], total: 1 }),
      'POST /api/v1/admin/users/u-ana/disable': () => {
        calls.push('disable');
        return json(200, ana(true));
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <AdminPage />
      </AuthProvider>,
    );

    const row = (await screen.findByText('ana@example.org')).closest('li')!;
    expect(within(row).getByText(/2 active sessions/)).toBeInTheDocument();
    await user.click(within(row).getByRole('button', { name: 'Disable' }));
    expect(await within(row).findByText('Disabled')).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: 'Enable' })).toBeInTheDocument();
    expect(calls).toEqual(['disable']);
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

  it('gives auditors a read-only view', async () => {
    window.history.replaceState(null, '', '/admin?tab=users');
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me(['installation.read', 'users.read', 'organizations.read', 'audit.read'], ['Auditor'])),
      'GET /api/v1/admin/users?filter=all&skip=0&take=25': () => json(200, { items: [ana(false)], total: 1 }),
    });
    render(
      <AuthProvider>
        <AdminPage />
      </AuthProvider>,
    );

    const row = (await screen.findByText('ana@example.org')).closest('li')!;
    expect(within(row).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByText('Read only')).toBeInTheDocument();
  });
});
