import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch, type Route } from '../../test/fetchRoutes';
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
  'GET /api/v1/organizations/o-1/domains': () => json(200, []),
  'GET /api/v1/organizations/o-1/domains/automatic-enrollment': () => json(200, { enabled: false }),
  'GET /api/v1/organizations/o-1/sso': () => json(200, { configured: false, issuer: null, clientId: null, clientSecretConfigured: false }),
  'GET /api/v1/organizations/o-1/guests': () => json(200, []),
  'GET /api/v1/organizations/o-1/access-control/roles': () => json(200, []),
  'GET /api/v1/organizations/o-1/access-control/groups': () => json(200, []),
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
    expect(within(tabs).getAllByRole('tab').map((t) => t.textContent)).toEqual(['Profile', 'Members', 'Workspaces', 'Channels & guests', 'Website chat', 'Access', 'Settings']);
    const glance = screen.getByRole('region', { name: 'At a glance' });
    expect(await within(glance).findByText('Guests')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Invite people' })).toBeInTheDocument();
    const setupGuide = screen.getByRole('region', { name: 'Set up your organization' });
    expect(within(setupGuide).getByText('2 of 3 setup steps complete')).toBeInTheDocument();
    expect(within(setupGuide).getByText('Review your first workspace')).toBeInTheDocument();
    await user.click(within(setupGuide).getByRole('button', { name: 'Set up team' }));
    expect(window.location.search).toBe('?id=o-1&tab=members');
    await user.click(within(tabs).getByRole('tab', { name: 'Access' }));
    expect(await screen.findByRole('heading', { name: 'Organization roles and groups' })).toBeInTheDocument();
    expect(screen.getByText(/Unconfigured permissions keep the existing policy/)).toBeInTheDocument();
    await user.click(within(screen.getByRole('tablist', { name: 'Organization sections' })).getByRole('tab', { name: 'Profile' }));

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
    expect(await screen.findByRole('heading', { name: 'Verified domains' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'Organization single sign-on' })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Automatically add confirmed company accounts' })).toBeDisabled();
  });

  it('lets owners create a role with support access, read, and manage decisions', async () => {
    let savedPermissions: unknown;
    const configuredRoutes: Record<string, Route> = routes('owner');
    configuredRoutes['POST /api/v1/organizations/o-1/access-control/roles'] = async (init) => {
      savedPermissions = JSON.parse(String(init?.body)).permissions;
      return json(201, { id: 'r-support' });
    };
    mockFetch(configuredRoutes);
    const user = userEvent.setup();
    render(<AuthProvider><OrganizationPage /></AuthProvider>);
    const accessTab = await screen.findByRole('tab', { name: 'Access' });
    await user.click(accessTab);
    await user.type(screen.getByRole('textbox', { name: 'Role name' }), 'Support operators');
    await user.selectOptions(screen.getByLabelText('Support workspace access'), 'allow');
    await user.selectOptions(screen.getByLabelText('Read support cases'), 'allow');
    await user.selectOptions(screen.getByLabelText('Manage support cases'), 'deny');
    await user.selectOptions(screen.getByLabelText('Manage organization settings'), 'allow');
    await user.click(screen.getByRole('button', { name: 'Create role' }));

    await waitFor(() => expect(savedPermissions).toEqual([
      { key: 'private_chats.use', effect: 'allow' },
      { key: 'support.access', effect: 'allow' },
      { key: 'support.cases.read', effect: 'allow' },
      { key: 'support.cases.manage', effect: 'deny' },
      { key: 'organization.manage', effect: 'allow' },
    ]));
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
    expect(screen.queryByRole('region', { name: 'Set up your organization' })).not.toBeInTheDocument();
  });

  it('shows the provider callback and organization sign-in URLs when SSO is configured', async () => {
    mockFetch({
      ...routes('owner'),
      'GET /api/v1/organizations/o-1/sso': () => json(200, { configured: true, issuer: 'https://id.example', clientId: 'chatx', clientSecretConfigured: true }),
    });
    window.history.replaceState(null, '', '/organization?id=o-1&tab=settings');
    render(<AuthProvider><OrganizationPage /></AuthProvider>);

    expect(await screen.findByRole('heading', { name: 'Organization single sign-on' })).toBeInTheDocument();
    expect(await screen.findByLabelText('Identity provider redirect URI')).toHaveValue(`${window.location.origin}/api/v1/auth/organization-sso/callback/o-1`);
    expect(screen.getByLabelText('Organization sign-in link')).toHaveValue(`${window.location.origin}/api/v1/auth/organization-sso/o-1`);
    expect(screen.getByRole('link', { name: 'Open sign-in' })).toHaveAttribute('href', `${window.location.origin}/api/v1/auth/organization-sso/o-1`);
  });
});
