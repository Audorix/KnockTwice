import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { HomePage } from './HomePage';

const me = (permissions: string[]) => ({
  id: 'u-ana', displayName: 'ikemyle', avatar: { initials: 'I', photoUrl: null }, email: 'ana@example.org', emailConfirmed: true, hasPassword: true,
  externalProviders: [], roles: [], permissions, preferences: { uiLanguage: 'en' },
});
const person = (id: string, name: string, initials: string) => ({ id, displayName: name, avatar: { initials } });
const now = new Date();
const minutesAgo = (m: number) => new Date(now.getTime() - m * 60_000).toISOString();

const routes = (permissions: string[], demo = false) => ({
  'GET /api/v1/auth/config': () => json(200, { ...authConfig, demoDataEnabled: demo }),
  'GET /api/v1/users/me': () => json(200, me(permissions)),
  'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
  'GET /api/v1/conversations/page?limit=3': () => json(200, {
    items: [
      { id: 'c-1', type: 'direct', scope: 'personal', organizationId: null, organizationName: null, title: 'Beatriz Jensen', counterpart: person('u-bj', 'Beatriz Jensen', 'BJ'),
        myRole: 'member', muted: false, lastSequence: 9, lastReadSequence: 7, unreadCount: 2, lastActivityAtUtc: minutesAgo(5),
        lastMessage: { id: 'm1', author: person('u-bj', 'Beatriz Jensen', 'BJ'), excerpt: "Thanks, I'll take a look.", deleted: false, createdAtUtc: minutesAgo(5) } },
      { id: 'c-2', type: 'channel', scope: 'organization', organizationId: 'o-2', organizationName: 'Iron Motors', title: 'customers', counterpart: null,
        myRole: 'member', muted: false, lastSequence: 4, lastReadSequence: 1, unreadCount: 3, lastActivityAtUtc: minutesAgo(9),
        lastMessage: { id: 'm2', author: person('u-sf', 'Sofia Ruiz', 'SR'), excerpt: 'The updated slides are ready.', deleted: false, createdAtUtc: minutesAgo(9) } },
    ],
    nextCursor: null, hasMore: false,
  }),
  'GET /api/v1/contacts/requests': () => json(200, [
    { id: 'r1', direction: 'incoming', user: person('u-x', 'Xavi', 'X'), createdAtUtc: minutesAgo(30) },
    { id: 'r2', direction: 'incoming', user: person('u-y', 'Yara', 'Y'), createdAtUtc: minutesAgo(40) },
    { id: 'r3', direction: 'outgoing', user: person('u-z', 'Zoe', 'Z'), createdAtUtc: minutesAgo(50) },
  ]),
  'GET /api/v1/reminders': () => json(200, [{
    id: 'rm1', conversationId: 'c-2', conversationType: 'channel', conversationTitle: 'customers', status: 'pending',
    remindAtUtc: new Date(now.getTime() + 3 * 3600_000).toISOString(),
    entry: { id: 'm2', sequence: 4, kind: 'message', actor: null, subject: null, text: null, body: 'Release review', createdAtUtc: minutesAgo(9) },
  }]),
  'GET /api/v1/users/me/links': () => json(200, []),
  'GET /api/v1/organizations': () => json(200, [{ id: 'o-2', name: 'Iron Motors', myRole: 'member', createdAtUtc: minutesAgo(999), myPermissions: [] }]),
  'GET /api/v1/organizations/o-2/workspaces': () => json(200, [
    { id: 'w-1', organizationId: 'o-2', name: 'General', myRole: 'member', createdAtUtc: minutesAgo(999) },
    { id: 'w-2', organizationId: 'o-2', name: 'Secret', myRole: null, createdAtUtc: minutesAgo(999) },
  ]),
  'GET /health/ready': () => json(200, { status: 'healthy', checks: [] }),
  'GET /api/v1/admin/demo-data': () => json(200, { users: 300, organizations: 50, contacts: 900, conversations: 975, messages: 12495, activity: 100, support: 500 }),
});

describe('Home page (redesign 2026-11)', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
    window.history.replaceState(null, '', '/');
  });

  it('welcomes back with recent conversations, what needs attention, workspaces and a profile nudge', async () => {
    mockFetch(routes([]));
    render(<AuthProvider><HomePage /></AuthProvider>);

    expect(await screen.findByRole('heading', { name: 'Welcome back, ikemyle' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /New conversation/ })).toHaveAttribute('href', '/conversations?new=1');

    const recent = screen.getByRole('region', { name: 'Recent conversations' });
    const direct = await within(recent).findByRole('link', { name: /Beatriz Jensen/ });
    expect(direct).toHaveAttribute('href', '/conversations?id=c-1');
    expect(within(direct).getByText('Direct')).toBeInTheDocument();
    const channel = within(recent).getByRole('link', { name: /# customers/ });
    expect(within(channel).getByText('Iron Motors')).toBeInTheDocument();
    expect(within(channel).getByText('Sofia: The updated slides are ready.')).toBeInTheDocument();

    const attention = screen.getByRole('region', { name: 'Needs your attention' });
    expect(await within(attention).findByText('2 contact requests')).toBeInTheDocument();
    expect(await within(attention).findByText('Release review')).toBeInTheDocument();
    expect(within(attention).getByText(/Today, .* · # customers/)).toBeInTheDocument();

    const workspaces = screen.getByRole('region', { name: 'Your workspaces' });
    expect(await within(workspaces).findByRole('link', { name: 'Open General in Iron Motors' })).toHaveAttribute('href', '/channels?org=o-2&ws=w-1');
    expect(within(workspaces).queryByText('Secret')).not.toBeInTheDocument();

    expect(await screen.findByRole('region', { name: 'Make it yours' })).toBeInTheDocument();
    expect(screen.queryByText('Administration & development')).not.toBeInTheDocument();
  });

  it('introduces the product to signed-out visitors, with the status and what is included', async () => {
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(401, { code: 'unauthorized' }),
      'GET /health/ready': () => json(200, { status: 'healthy', checks: [] }),
    });
    const user = userEvent.setup();
    render(<AuthProvider><HomePage /></AuthProvider>);

    expect(await screen.findByRole('heading', { level: 1, name: /Your conversations\. Your team\. Your space\./ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Create account' })).toHaveAttribute('href', '/register');
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/sign-in');
    const features = screen.getByRole('list', { name: 'Features' });
    expect(within(features).getAllByRole('listitem')).toHaveLength(4);
    expect(await screen.findByText('API ready')).toBeInTheDocument();
    await user.click(screen.getByText("What's included in Preview 0.1"));
    expect(screen.getByText('Liveness and readiness checks', { exact: false })).toBeVisible();
  });

  it('opens the development tools for administrators of a development installation', async () => {
    mockFetch(routes(['installation.read', 'installation.demo_data'], true));
    const user = userEvent.setup();
    render(<AuthProvider><HomePage /></AuthProvider>);

    await user.click(await screen.findByRole('button', { name: /Administration & development/ }));
    const tools = screen.getByRole('dialog', { name: /Development tools/ });
    expect(await within(tools).findByText('API ready')).toBeInTheDocument();
    expect(await within(tools).findByText('12,495')).toBeInTheDocument();
    expect(within(tools).getByText('Liveness and readiness checks', { exact: false })).toBeInTheDocument();
    expect(within(tools).getByText('Available only in Development to authorized administrators.')).toBeInTheDocument();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Development tools/ })).toBeInTheDocument();
  });
});
