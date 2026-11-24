import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
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

  it('opens the organization and workspace of your most recent channel, not simply the first organization', async () => {
    window.history.replaceState(null, '', '/channels');
    const own = { ...acme, id: 'o-0', name: 'Audorix', myRole: 'owner' };
    mockFetch({
      ...signedIn,
      'GET /api/v1/organizations': () => json(200, [own, acme]),
      'GET /api/v1/conversations': () => json(200, [
        { id: 'c-old', type: 'channel', organizationId: 'o-1', workspaceId: 'w-1', lastActivityAtUtc: '2026-11-20T10:00:00Z' },
        { id: 'c-new', type: 'channel', organizationId: 'o-1', workspaceId: 'w-2', lastActivityAtUtc: '2026-11-23T10:00:00Z' },
        { id: 'g-1', type: 'group', organizationId: null, workspaceId: null, lastActivityAtUtc: '2026-11-24T10:00:00Z' },
      ]),
      'GET /api/v1/organizations/o-1/workspaces': () => json(200, [
        { id: 'w-1', organizationId: 'o-1', name: 'General', myRole: 'member', createdAtUtc: '2026-11-23T10:00:00Z' },
        { id: 'w-2', organizationId: 'o-1', name: 'Sales', myRole: 'member', createdAtUtc: '2026-11-23T10:00:00Z' },
      ]),
      'GET /api/v1/organizations/o-1/workspaces/w-2/channels': () => json(200, [channel('c-new', 'deals', true)]),
      'GET /api/v1/guest-invitations': () => json(200, []),
    });
    render(
      <AuthProvider>
        <ChannelsPage />
      </AuthProvider>,
    );

    expect(await screen.findByText('deals')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Organization' })).toHaveValue('o-1');
    expect(screen.getByRole('combobox', { name: 'Workspace' })).toHaveValue('w-2');
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

  it('works the support inbox: tabs, search, notes, replies and status', async () => {
    window.history.replaceState(null, '', '/support');
    const calls: string[] = [];
    let status = 'open';
    const ana = { id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' } };
    const emma = { id: 'v-emma', displayName: 'Emma Miller', avatar: { initials: 'EM' } };
    const conversation = (id: string, name: string, extra: Record<string, unknown> = {}) => ({
      conversationId: id, visitorName: name, widgetId: 'wg-1', widgetName: 'Website chat', status, assignedTo: null, participating: false,
      unreadCount: 0, createdAtUtc: new Date(Date.now() - 20 * 60_000).toISOString(), lastActivityAtUtc: new Date().toISOString(), subject: null, ...extra,
    });
    const items = () => [
      { conversation: conversation('cv-1', 'Emma Miller', { subject: 'Unable to upload a document', unreadCount: 1 }), title: 'Unable to upload a document',
        lastMessage: { text: 'It stops at 80% every time.', fromCustomer: true, sentAtUtc: new Date(Date.now() - 12 * 60_000).toISOString() } },
      { conversation: conversation('cv-2', 'James Wilson', { assignedTo: ana, widgetName: 'In-app' }), title: 'Invite link has expired',
        lastMessage: { text: 'Could you send me a new invitation?', fromCustomer: true, sentAtUtc: new Date(Date.now() - 18 * 60_000).toISOString() } },
    ];
    const page = () => json(200, { items: items(), total: 2, counts: { open: 2, pending: 3, closed: 10, all: 15 } });
    const entry = (id: string, sequence: number, actor: typeof ana, body: string) => ({
      id, sequence, kind: 'message', actor, subject: null, text: null, body, createdAtUtc: new Date(Date.now() - (30 - sequence) * 60_000).toISOString(), attachments: [],
    });
    mockFetch({
      ...signedIn,
      'GET /api/v1/organizations': () => json(200, [acme]),
      'GET /api/v1/organizations/o-1/members': () =>
        json(200, { members: [{ user: ana, role: 'member', joinedAtUtc: '2026-11-23T10:00:00Z', supportAgent: true }], pendingInvitations: null }),
      'GET /api/v1/organizations/o-1/support/inbox?status=open&skip=0&take=20': () => page(),
      'GET /api/v1/organizations/o-1/support/inbox?status=open&skip=0&take=20&q=upload': () => {
        calls.push('search');
        return page();
      },
      'GET /api/v1/support/conversations/cv-1/history': () =>
        json(200, { items: [entry('e-1', 2, emma, 'Hi! The upload stops at 80%.'), entry('e-2', 3, ana, 'Which browser are you using?')], nextCursor: null, hasMore: false }),
      'GET /api/v1/support/conversations/cv-1/notes': () =>
        json(200, [{ id: 'n-1', conversationId: 'cv-1', author: ana, body: 'Checking the upload logs.', createdAtUtc: new Date(Date.now() - 26 * 60_000).toISOString() }]),
      'GET /api/v1/attachments/limits': () => json(200, { maxFileSize: 10485760, maxPerMessage: 10, allowedExtensions: ['pdf', 'png'] }),
      'POST /api/v1/support/conversations/cv-1/notes': (init) => {
        calls.push(`note ${String(init?.body)}`);
        return json(201, { id: 'n-2', conversationId: 'cv-1', author: ana, body: 'Asked engineering.', createdAtUtc: new Date().toISOString() });
      },
      'POST /api/v1/support/conversations/cv-1/join': () => {
        calls.push('join');
        return json(200, conversation('cv-1', 'Emma Miller', { participating: true }));
      },
      'POST /api/v1/conversations/cv-1/messages': (init) => {
        calls.push(`reply ${JSON.parse(String(init?.body)).body as string}`);
        return json(201, entry('e-3', 4, ana, 'Try a smaller file.'));
      },
      'PUT /api/v1/support/conversations/cv-1/status': (init) => {
        calls.push(`status ${String(init?.body)}`);
        status = 'closed';
        return json(200, conversation('cv-1', 'Emma Miller'));
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <SupportPage />
      </AuthProvider>,
    );

    // The list: counts on the tabs, subject, preview, channel and assignment.
    expect(await screen.findByRole('heading', { name: '2 open conversations' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Open 2' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Pending 3' })).toBeInTheDocument();
    const james = screen.getByText('James Wilson').closest('button')!;
    expect(within(james).getByText('Invite link has expired')).toBeInTheDocument();
    expect(within(james).getByText('In-app')).toBeInTheDocument();
    expect(within(james).getByText('Assigned to you')).toBeInTheDocument();
    const emmaRow = screen.getByText('Emma Miller').closest('button')!;
    expect(within(emmaRow).getByText('Unassigned')).toBeInTheDocument();
    expect(within(emmaRow).getByText('12 min')).toBeInTheDocument();
    expect(screen.getByText('Showing 2 of 2')).toBeInTheDocument();

    await user.type(screen.getByRole('searchbox', { name: 'Search support conversations' }), 'upload{Enter}');
    await vi.waitFor(() => expect(calls).toContain('search'));

    // The conversation: customer and team messages with the internal note between them.
    await user.click(emmaRow);
    expect(window.location.search).toBe('?org=o-1&id=cv-1');
    expect(await screen.findByRole('heading', { name: 'Unable to upload a document' })).toBeInTheDocument();
    expect(await screen.findByText('Hi! The upload stops at 80%.')).toBeInTheDocument();
    expect(screen.getByText('Checking the upload logs.')).toBeInTheDocument();
    expect(screen.getByText('Emma Miller · Website chat')).toBeInTheDocument();

    // An internal note goes to the team only; a reply joins the conversation first.
    await user.click(screen.getByRole('tab', { name: 'Internal note' }));
    expect(screen.getByText('Notes are only visible to your team.')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Note for your team'), 'Asked engineering.');
    await user.click(screen.getByRole('button', { name: 'Add note' }));
    await vi.waitFor(() => expect(calls).toContain('note {"body":"Asked engineering."}'));
    await user.click(screen.getByRole('tab', { name: 'Reply' }));
    await user.type(screen.getByLabelText('Reply to Emma'), 'Try a smaller file.');
    await user.click(screen.getByRole('button', { name: 'Send reply' }));
    await vi.waitFor(() => expect(calls).toContain('reply Try a smaller file.'));
    expect(calls.indexOf('join')).toBeLessThan(calls.indexOf('reply Try a smaller file.'));

    await user.click(screen.getByRole('button', { name: 'Mark resolved' }));
    await vi.waitFor(() => expect(calls).toContain('status {"status":"closed"}'));

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
