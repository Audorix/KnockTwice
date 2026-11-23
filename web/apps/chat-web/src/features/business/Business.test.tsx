import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { ChannelsPage } from './ChannelsPage';
import { SupportPage, supportOrganizations } from './SupportPage';
import { OrganizationSettingsSection } from './OrganizationBusiness';

const me = { id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' }, email: 'ana@example.org', emailConfirmed: true, hasPassword: true,
  externalProviders: [], roles: [], permissions: [], preferences: { uiLanguage: 'en' } };
const acme = { id: 'o-1', name: 'Acme', myRole: 'member', createdAtUtc: '2026-11-23T10:00:00Z', myPermissions: [], supportAgent: true, guestsEnabled: true, channelCreation: 'members' };
const channel = (id: string, name: string, joined: boolean, visibility = 'public') => ({
  id, organizationId: 'o-1', workspaceId: 'w-1', workspaceName: 'General', name, description: null, visibility, memberCount: 2, joined,
  myRole: joined ? 'member' : null, canManage: false, teams: [], createdAtUtc: '2026-11-23T10:00:00Z', lastActivityAtUtc: '2026-11-23T10:00:00Z',
});
const signedIn = { 'GET /api/v1/auth/config': () => json(200, authConfig), 'GET /api/v1/users/me': () => json(200, me), 'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }) };

describe('business communication', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  it('browses a workspace, joins a public channel and accepts a guest invitation', async () => {
    window.history.replaceState(null, '', '/channels');
    const calls: string[] = [];
    mockFetch({
      ...signedIn,
      'GET /api/v1/organizations': () => json(200, [acme]),
      'GET /api/v1/organizations/o-1/workspaces': () => json(200, [{ id: 'w-1', organizationId: 'o-1', name: 'General', myRole: 'member', createdAtUtc: '2026-11-23T10:00:00Z' }]),
      'GET /api/v1/organizations/o-1/workspaces/w-1/channels': () => json(200, [channel('c-1', 'sales', false), channel('c-2', 'leads', true, 'private')]),
      'GET /api/v1/guest-invitations': () =>
        json(200, [{ id: 'gi-1', organizationId: 'o-9', organizationName: 'Partner Co', channelId: 'c-9', channelName: 'shared', invitedBy: { id: 'u-x', displayName: 'Xavi', avatar: { initials: 'X' } }, expiresAtUtc: '2026-12-01T00:00:00Z' }]),
      'POST /api/v1/channels/c-1/join': () => {
        calls.push('join');
        return json(200, { channel: channel('c-1', 'sales', true), members: [] });
      },
      'POST /api/v1/guest-invitations/gi-1/accept': () => {
        calls.push('accept');
        return json(200, { conversationId: 'c-9' });
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <ChannelsPage />
      </AuthProvider>,
    );

    expect(await screen.findByText('Xavi invited you to #shared in Partner Co as a guest.')).toBeInTheDocument();
    const sales = (await screen.findByText('sales')).closest('li')!;
    expect(within(sales).getByText(/Public · 2 members/)).toBeInTheDocument();
    const leads = screen.getByText('leads').closest('li')!;
    expect(within(leads).getByRole('button', { name: 'Open' })).toBeInTheDocument();

    await user.click(within(sales).getByRole('button', { name: 'Join sales' }));
    expect(calls).toEqual(['join']);
    expect(window.location.pathname + window.location.search).toBe('/conversations?id=c-1');

    window.history.replaceState(null, '', '/channels');
    await user.click(screen.getByRole('button', { name: 'Accept' }));
    expect(calls).toEqual(['join', 'accept']);
    expect(window.location.search).toBe('?id=c-9');
  });

  it('lets a support agent take a website conversation', async () => {
    window.history.replaceState(null, '', '/support');
    let assigned = false;
    const item = () => ({
      conversationId: 'cv-1', visitorName: 'Visitor 1A2B', widgetId: 'wg-1', widgetName: 'Shop', status: 'open',
      assignedTo: assigned ? { id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' } } : null, participating: assigned, unreadCount: 0,
      createdAtUtc: '2026-11-23T10:00:00Z', lastActivityAtUtc: '2026-11-23T10:00:00Z',
    });
    mockFetch({
      ...signedIn,
      'GET /api/v1/organizations': () => json(200, [acme]),
      'GET /api/v1/organizations/o-1/support?status=open': () => json(200, [item()]),
      'POST /api/v1/support/conversations/cv-1/claim': () => {
        assigned = true;
        return json(200, item());
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <SupportPage />
      </AuthProvider>,
    );

    const row = (await screen.findByText('Visitor 1A2B')).closest('li')!;
    expect(within(row).getByText(/Shop · .* · unassigned/)).toBeInTheDocument();
    await user.click(within(row).getByRole('button', { name: 'Take the conversation with Visitor 1A2B' }));
    expect(await within(row).findByText(/assigned to Ana/)).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: 'Open' })).toBeInTheDocument();
    expect(within(row).queryByRole('button', { name: /Take the conversation/ })).not.toBeInTheDocument();

    // Only agents and administrators work an inbox.
    expect(supportOrganizations([{ ...acme, supportAgent: false } as never, { ...acme, id: 'o-2', supportAgent: false, myPermissions: ['organization.manage'] } as never])
      .map((o) => o.id)).toEqual(['o-2']);
  });

  it('changes organization-specific settings', async () => {
    const saved: unknown[] = [];
    mockFetch({
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'PUT /api/v1/organizations/o-1/settings': (init) => {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        saved.push(body);
        return json(200, { ...acme, ...body });
      },
    });
    let current = { ...acme, myPermissions: ['organization.manage'] } as never as Parameters<typeof OrganizationSettingsSection>[0]['organization'];
    const user = userEvent.setup();
    const { rerender } = render(<OrganizationSettingsSection organization={current} onSaved={(o) => (current = o)} />);

    await user.click(screen.getByRole('switch', { name: 'Allow guests' }));
    rerender(<OrganizationSettingsSection organization={current} onSaved={(o) => (current = o)} />);
    expect(screen.getByRole('switch', { name: 'Allow guests' })).not.toBeChecked();
    await user.selectOptions(screen.getByLabelText('Who can create channels'), 'administrators');
    expect(saved).toEqual([{ guestsEnabled: false }, { channelCreation: 'administrators' }]);
  });
});
