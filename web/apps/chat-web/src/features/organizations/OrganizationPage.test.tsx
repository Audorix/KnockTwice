import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { OrganizationPage } from './OrganizationPage';

const ana = { id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' } };
const me = { ...ana, email: 'ana@example.org', emailConfirmed: true, preferences: { uiLanguage: 'en' }, hasPassword: true, externalProviders: [], roles: [], permissions: [] };
const organization = (role: 'owner' | 'member', photo: boolean) => ({
  id: 'o-1', name: 'Audorix', myRole: role, createdAtUtc: '2026-11-20T10:00:00Z',
  myPermissions: role === 'owner' ? ['organization.manage', 'organization.members.manage', 'organization.roles.manage', 'organization.workspaces.manage'] : [],
  photoUrl: photo ? '/api/v1/organizations/o-1/photo?v=1' : null, bannerUrl: null,
});

const routes = (role: 'owner' | 'member') => ({
  'GET /api/v1/auth/config': () => json(200, authConfig),
  'GET /api/v1/users/me': () => json(200, me),
  'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
  'GET /api/v1/organizations/o-1': () => json(200, organization(role, true)),
  'GET /api/v1/organizations/o-1/members': () => json(200, { members: [{ user: ana, role, joinedAtUtc: '2026-11-20T10:00:00Z' }], invitations: role === 'owner' ? [] : null }),
  'GET /api/v1/organizations/o-1/workspaces': () => json(200, [{ id: 'w-1', organizationId: 'o-1', name: 'General', myRole: 'administrator', createdAtUtc: '2026-11-20T10:00:00Z' }]),
  'GET /api/v1/organizations/o-1/guests': () => json(200, []),
});

describe('Organization page (redesign 2026-11-24)', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
    window.history.replaceState(null, '', '/organization?id=o-1');
  });

  it('shows the header with the picture and counts, and the profile tab for an owner', async () => {
    mockFetch(routes('owner'));
    const user = userEvent.setup();
    render(<AuthProvider><OrganizationPage /></AuthProvider>);

    expect(await screen.findByRole('heading', { name: 'Audorix', level: 1 })).toBeInTheDocument();
    expect(screen.getByText('Owner', { selector: '.cx-org-role' })).toBeInTheDocument();
    expect(screen.getByText(/1 member/)).toHaveTextContent('1 member · 1 workspace');
    expect(document.querySelector('.cx-org-hero img')?.getAttribute('src')).toBe('/api/v1/organizations/o-1/photo?v=1');

    const tabs = screen.getByRole('tablist', { name: 'Organization sections' });
    expect(within(tabs).getAllByRole('tab').map((t) => t.textContent)).toEqual(['Profile', 'Members', 'Workspaces', 'Channels & guests', 'Website chat', 'Settings']);
    const glance = screen.getByRole('region', { name: 'At a glance' });
    expect(await within(glance).findByText('Guests')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Invite people' })).toBeInTheDocument();

    // The camera on the picture opens the crop dialog for a square picture.
    // Two ways in: the camera on the header picture and the profile card's button.
    expect(screen.getAllByRole('button', { name: 'Change picture' })).toHaveLength(2);
    await user.click(screen.getAllByRole('button', { name: 'Change picture' })[0]!);
    expect(screen.getByRole('dialog', { name: 'Change organization picture' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Remove picture/ })).toBeInTheDocument();
    await user.keyboard('{Escape}');

    // Tabs are addressable and switch the panel.
    await user.click(within(tabs).getByRole('tab', { name: 'Settings' }));
    expect(window.location.search).toBe('?id=o-1&tab=settings');
    expect(screen.getByRole('heading', { name: 'Delete organization' })).toBeInTheDocument();
  });

  it('shows members only what they may use', async () => {
    mockFetch(routes('member'));
    render(<AuthProvider><OrganizationPage /></AuthProvider>);

    expect(await screen.findByRole('heading', { name: 'Audorix', level: 1 })).toBeInTheDocument();
    const tabs = screen.getByRole('tablist', { name: 'Organization sections' });
    expect(within(tabs).getAllByRole('tab').map((t) => t.textContent)).toEqual(['Profile', 'Members', 'Workspaces', 'Settings']);
    expect(screen.queryByRole('button', { name: 'Change picture' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Change background' })).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Organization name' })).not.toBeInTheDocument();
    expect(screen.queryByText('Guests')).not.toBeInTheDocument();
  });
});
