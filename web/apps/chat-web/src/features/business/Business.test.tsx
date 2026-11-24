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
const channel = (id: string, name: string, joined: boolean, visibility = 'public', extra: Record<string, unknown> = {}) => ({
  id, organizationId: 'o-1', workspaceId: 'w-1', workspaceName: 'General', name, description: null, visibility, memberCount: 2, joined,
  myRole: joined ? 'member' : null, canManage: false, teams: [], createdAtUtc: '2026-11-23T10:00:00Z', lastActivityAtUtc: '2026-11-23T10:00:00Z', ...extra,
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

  it('browses a workspace in tabs, filters, restores an archived channel, joins a public channel and accepts a guest invitation', async () => {
    window.history.replaceState(null, '', '/channels');
    const calls: string[] = [];
    let archived = true;
    mockFetch({
      ...signedIn,
      'GET /api/v1/organizations': () => json(200, [acme]),
      'GET /api/v1/organizations/o-1/workspaces': () => json(200, [{ id: 'w-1', organizationId: 'o-1', name: 'General', myRole: 'member', createdAtUtc: '2026-11-23T10:00:00Z' }]),
      'GET /api/v1/organizations/o-1/workspaces/w-1/channels': () => json(200, [
        channel('c-1', 'sales', false, 'public', { description: 'Deals and leads', memberCount: 12 }),
        channel('c-2', 'leads', true, 'private', { lastActivityAtUtc: new Date(Date.now() - 5 * 60_000).toISOString() }),
        channel('c-3', 'old-launch', true, 'public', { canManage: true, myRole: 'manager', archivedAtUtc: archived ? '2026-11-23T12:00:00Z' : null }),
      ]),
      'GET /api/v1/guest-invitations': () =>
        json(200, [{ id: 'gi-1', organizationId: 'o-9', organizationName: 'Partner Co', channelId: 'c-9', channelName: 'shared', invitedBy: { id: 'u-x', displayName: 'Xavi', avatar: { initials: 'X' } }, expiresAtUtc: '2026-12-01T00:00:00Z' }]),
      'PUT /api/v1/channels/c-3/archived': (init) => {
        calls.push(`archived ${String(init?.body)}`);
        archived = false;
        return json(200, { channel: channel('c-3', 'old-launch', true), members: [] });
      },
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
    const sales = (await screen.findByText('sales')).closest('tr')!;
    expect(screen.getByRole('tab', { name: 'All channels 2' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Joined 1' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Discover 1' })).toBeInTheDocument();
    expect(within(sales).getByText('Not joined')).toBeInTheDocument();
    expect(within(sales).getByText('Deals and leads')).toBeInTheDocument();
    expect(within(sales).getByText('12 members')).toBeInTheDocument();
    const leads = screen.getByText('leads').closest('tr')!;
    expect(within(leads).getByText('Private')).toBeInTheDocument();
    expect(within(leads).getByText('5 minutes ago')).toBeInTheDocument();
    expect(within(leads).getByRole('button', { name: 'Open leads' })).toBeInTheDocument();
    expect(screen.queryByText('old-launch')).not.toBeInTheDocument();
    expect(screen.getByText('Showing 2 channels')).toBeInTheDocument();

    // Search and the visibility filter.
    await user.type(screen.getByRole('searchbox', { name: 'Find a channel' }), 'deal');
    expect(screen.getByText('Showing 1 channel')).toBeInTheDocument();
    expect(screen.queryByText('leads')).not.toBeInTheDocument();
    await user.clear(screen.getByRole('searchbox', { name: 'Find a channel' }));
    await user.selectOptions(screen.getByRole('combobox', { name: 'Visibility' }), 'private');
    expect(screen.queryByText('sales')).not.toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Visibility' }), 'all');

    // Archived channels have their own tab; a manager restores one from the "…" menu.
    await user.click(screen.getByRole('tab', { name: 'Archived 1' }));
    const old = screen.getByText('old-launch').closest('tr')!;
    expect(within(old).getByText('Archived')).toBeInTheDocument();
    await user.click(within(old).getByRole('button', { name: 'More actions for old-launch' }));
    await user.click(screen.getByRole('menuitem', { name: 'Restore channel' }));
    expect(calls).toEqual(['archived {"archived":false}']);
    expect(await screen.findByText('No archived channels.')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'All channels 3' }));
    calls.length = 0;

    await user.click(within(screen.getByText('sales').closest('tr')!).getByRole('button', { name: 'Join sales' }));
    expect(calls).toEqual(['join']);
    expect(window.location.pathname + window.location.search).toBe('/conversations?id=c-1');

    window.history.replaceState(null, '', '/channels');
    await user.click(screen.getByRole('button', { name: 'Accept' }));
    expect(calls).toEqual(['join', 'accept']);
    expect(window.location.search).toBe('?id=c-9');
  });

  it('creates a channel from the side panel', async () => {
    window.history.replaceState(null, '', '/channels');
    const created: unknown[] = [];
    mockFetch({
      ...signedIn,
      'GET /api/v1/organizations': () => json(200, [acme]),
      'GET /api/v1/organizations/o-1/workspaces': () => json(200, [{ id: 'w-1', organizationId: 'o-1', name: 'General', myRole: 'member', createdAtUtc: '2026-11-23T10:00:00Z' }]),
      'GET /api/v1/organizations/o-1/workspaces/w-1/channels': () => json(200, []),
      'GET /api/v1/organizations/o-1/workspaces/w-1/teams': () => json(200, []),
      'GET /api/v1/guest-invitations': () => json(200, []),
      'POST /api/v1/organizations/o-1/workspaces/w-1/channels': (init) => {
        created.push(JSON.parse(String(init?.body)));
        return json(201, { channel: channel('c-7', 'design', true, 'private'), members: [] });
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <ChannelsPage />
      </AuthProvider>,
    );

    expect(await screen.findByText('No channels you can see in this workspace yet.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'New channel' }));
    const panel = screen.getByRole('dialog', { name: 'New channel' });
    expect(within(panel).getByText('In the General workspace.')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'New channel' }));
    const again = screen.getByRole('dialog', { name: 'New channel' });
    await user.type(within(again).getByLabelText('Channel name'), 'design');
    await user.click(within(again).getByRole('radio', { name: /Private/ }));
    await user.click(within(again).getByRole('button', { name: 'Create channel' }));
    expect(created).toEqual([{ name: 'design', description: '', visibility: 'private', teamIds: [] }]);
    expect(window.location.search).toBe('?id=c-7');
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
