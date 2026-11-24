import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { ConversationsPage, describeEntry } from './ConversationsPage';

const me = {
  id: 'u-ana',
  email: 'ana@example.org',
  emailConfirmed: true,
  displayName: 'Ana',
  avatar: { initials: 'A' },
  preferences: { uiLanguage: 'en' },
  hasPassword: true,
  externalProviders: [],
  roles: [],
  permissions: [],
};
const ben = { id: 'u-ben', displayName: 'Ben Brown', avatar: { initials: 'BB' } };
const dana = { id: 'u-dana', displayName: 'Dana Diaz', avatar: { initials: 'DD' } };

const group = {
  id: 'c-team',
  type: 'group',
  scope: 'personal',
  organizationId: null,
  organizationName: null,
  title: 'Team',
  counterpart: null,
  myRole: 'owner',
  muted: false,
  lastSequence: 3,
  lastReadSequence: 1,
  unreadCount: 2,
  lastActivityAtUtc: '2026-11-22T10:00:00Z',
};

const csrf = () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' });
const page = (items: unknown[], nextCursor: string | null = null) => ({ items, nextCursor, hasMore: nextCursor !== null });

describe('ConversationsPage', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
    window.localStorage.clear();
    vi.unstubAllGlobals();
  });

  it('comes back to the last conversation: opened on wide screens, selected in the list on phones', async () => {
    const routes = {
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': csrf,
      'GET /api/v1/conversations/page?limit=50': () => json(200, page([group])),
      'GET /api/v1/conversations/c-team': () =>
        json(200, { conversation: group, description: null, participants: [{ user: me, role: 'owner', joinedAtUtc: '2026-11-22T09:00:00Z' }] }),
      'GET /api/v1/conversations/c-team/history': () => json(200, { items: [], nextCursor: null, hasMore: false }),
    };
    window.localStorage.setItem('chatx.lastConversation.u-ana', 'c-team');

    // Phone: the list shows it selected, without opening it over the list.
    vi.stubGlobal('matchMedia', (q: string) => ({ matches: !q.includes('min-width'), media: q, addEventListener: () => undefined, removeEventListener: () => undefined }));
    window.history.replaceState(null, '', '/conversations');
    mockFetch(routes);
    const { unmount } = render(<AuthProvider><ConversationsPage /></AuthProvider>);
    const item = await screen.findByRole('button', { name: /Team/ });
    expect(item).toHaveAttribute('aria-current', 'true');
    expect(window.location.search).toBe('');
    unmount();

    // Wide screen: it opens, with its messages.
    vi.stubGlobal('matchMedia', (q: string) => ({ matches: true, media: q, addEventListener: () => undefined, removeEventListener: () => undefined }));
    window.history.replaceState(null, '', '/conversations');
    mockFetch(routes);
    render(<AuthProvider><ConversationsPage /></AuthProvider>);
    await waitFor(() => expect(window.location.search).toBe('?id=c-team'));
  });

  it('lists conversations with unread counts, shows history and marks the open conversation read', async () => {
    window.history.replaceState(null, '', '/conversations?id=c-team');
    const reads: unknown[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': csrf,
      'GET /api/v1/conversations/page?limit=50': () => json(200, page([group])),
      'GET /api/v1/conversations/c-team': () =>
        json(200, { conversation: group, description: null, participants: [{ user: { ...me, avatar: me.avatar }, role: 'owner', joinedAtUtc: '2026-11-22T09:00:00Z' }] }),
      'GET /api/v1/conversations/c-team/history': () =>
        json(200, {
          items: [
            { id: 'e1', sequence: 1, kind: 'created', actor: me, subject: null, text: 'Team', createdAtUtc: '2026-11-22T09:00:00Z' },
            { id: 'e2', sequence: 2, kind: 'member_added', actor: me, subject: ben, text: null, createdAtUtc: '2026-11-22T09:01:00Z' },
            { id: 'e3', sequence: 3, kind: 'renamed', actor: ben, subject: null, text: 'Team', createdAtUtc: '2026-11-22T09:02:00Z' },
          ],
          nextCursor: null,
          hasMore: false,
        }),
      'POST /api/v1/conversations/c-team/read': (init) => {
        reads.push(JSON.parse(String(init?.body)));
        return json(200, { lastReadSequence: 3, unreadCount: 0 });
      },
    });

    render(
      <AuthProvider>
        <ConversationsPage />
      </AuthProvider>,
    );

    const list = await screen.findByRole('button', { name: /Team/ });
    expect(within(list).getByLabelText('2 unread')).toBeInTheDocument();
    expect(await screen.findByText('Ana created the group “Team”')).toBeInTheDocument();
    expect(screen.getByText('Ana added Ben Brown')).toBeInTheDocument();
    expect(screen.getByText('Ben Brown renamed the group to “Team”')).toBeInTheDocument();
    expect(reads).toEqual([{ sequence: 3 }]);
  });

  it('shows pinned conversations first, last-message previews, and filters by chip and search', async () => {
    window.history.replaceState(null, '', '/conversations');
    const ben = { id: 'u-ben', displayName: 'Ben Brown', avatar: { initials: 'BB' } };
    const last = (author: unknown, excerpt: string | null, deleted = false) => ({ id: `m-${excerpt}`, author, excerpt, deleted, createdAtUtc: '2026-11-22T09:00:00Z' });
    const list = [
      { ...group, id: 'c-1', title: 'Team', pinned: true, unreadCount: 0, lastMessage: last(ben, 'Ready for the review.') },
      { ...group, id: 'c-2', title: 'Ben Brown', type: 'direct', counterpart: ben, unreadCount: 3, lastMessage: last(me, 'See you') },
      { ...group, id: 'c-3', title: 'Old news', unreadCount: 0, lastMessage: last(ben, null, true) },
      { ...group, id: 'c-4', title: 'Files only', unreadCount: 0, lastMessage: last(ben, '') },
    ];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': csrf,
      'GET /api/v1/drafts': () => json(200, []),
      'GET /api/v1/conversations/page?limit=50': () => json(200, page(list)),
    });
    const u = userEvent.setup();
    render(
      <AuthProvider>
        <ConversationsPage />
      </AuthProvider>,
    );

    const pinned = await screen.findByRole('list', { name: 'Pinned' });
    expect(within(pinned).getByRole('button', { name: /Team/ })).toHaveTextContent('Ben: Ready for the review.');
    const recent = screen.getByRole('list', { name: 'Recent' });
    expect(within(recent).getByRole('button', { name: /Ben Brown/ })).toHaveTextContent('You: See you');
    expect(within(recent).getByRole('button', { name: /Old news/ })).toHaveTextContent('Ben: Message deleted');
    expect(within(recent).getByRole('button', { name: /Files only/ })).toHaveTextContent('Ben: Sent a file');

    await u.click(screen.getByRole('button', { name: /Unread/ }));
    expect(screen.getAllByRole('button', { name: /Ben Brown|Team|Old news|Files only/ }).map((b) => b.textContent)).toEqual([expect.stringContaining('Ben Brown')]);
    await u.click(screen.getByRole('button', { name: 'Groups' }));
    expect(screen.queryByRole('button', { name: /Ben Brown/ })).not.toBeInTheDocument();
    await u.click(screen.getByRole('button', { name: 'All' }));
    await u.type(screen.getByRole('searchbox', { name: 'Search conversations' }), 'files');
    expect(screen.getByRole('button', { name: /Files only/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Team/ })).not.toBeInTheDocument();
  });

  it('loads the list a page at a time', async () => {
    window.history.replaceState(null, '', '/conversations');
    const older = { ...group, id: 'c-old', title: 'Old friends', unreadCount: 0 };
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': csrf,
      'GET /api/v1/drafts': () => json(200, []),
      'GET /api/v1/conversations/page?limit=50': () => json(200, page([group], 'next-1')),
      'GET /api/v1/conversations/page?limit=50&after=next-1': () => json(200, page([older])),
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <ConversationsPage />
      </AuthProvider>,
    );

    expect(await screen.findByRole('button', { name: /Team/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Old friends/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Show more conversations' }));
    expect(await screen.findByRole('button', { name: /Old friends/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Show more conversations' })).not.toBeInTheDocument();
  });

  it('creates a group from contacts', async () => {
    window.history.replaceState(null, '', '/conversations');
    let created: unknown = null;
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': csrf,
      'GET /api/v1/conversations/page?limit=50': () => json(200, page([])),
      'GET /api/v1/organizations': () => json(200, []),
      'GET /api/v1/contacts': () => json(200, [{ user: ben, sinceUtc: '2026-11-01T00:00:00Z' }, { user: dana, sinceUtc: '2026-11-01T00:00:00Z' }]),
      'POST /api/v1/conversations/groups': (init) => {
        created = JSON.parse(String(init?.body));
        return json(201, { ...group, id: 'c-new', title: 'Planning', unreadCount: 0 });
      },
      'GET /api/v1/conversations/c-new': () => json(200, { conversation: { ...group, id: 'c-new', title: 'Planning' }, description: null, participants: [] }),
      'GET /api/v1/conversations/c-new/history': () => json(200, { items: [], nextCursor: null, hasMore: false }),
      'POST /api/v1/conversations/c-new/read': () => json(200, { lastReadSequence: 3, unreadCount: 0 }),
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <ConversationsPage />
      </AuthProvider>,
    );

    expect(await screen.findByText(/No conversations yet/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'New conversation' }));
    await user.click(screen.getByRole('button', { name: 'Group' }));
    await user.type(screen.getByLabelText('Group name'), 'Planning');
    await user.click(await screen.findByRole('checkbox', { name: /Ben Brown/ }));
    await user.click(screen.getByRole('checkbox', { name: /Dana Diaz/ }));
    await user.click(screen.getByRole('button', { name: 'Create group' }));

    expect(await screen.findByRole('heading', { name: 'Planning' })).toBeInTheDocument();
    expect(created).toEqual({ title: 'Planning', userIds: ['u-ben', 'u-dana'], organizationId: null });
    expect(window.location.search).toBe('?id=c-new');
  });

  it('describes every history entry kind in Spanish too', async () => {
    await i18n.changeLanguage('es');
    const entry = { id: 'x', sequence: 4, actor: ben, subject: dana, text: 'administrator', createdAtUtc: '2026-11-22T00:00:00Z' };
    expect(describeEntry({ ...entry, kind: 'role_changed' }, 'group', i18n.t)).toBe('Ben Brown hizo a Dana Diaz Administrador');
    expect(describeEntry({ ...entry, kind: 'created', text: null }, 'direct', i18n.t)).toBe('Ben Brown inició la conversación');
    expect(describeEntry({ ...entry, kind: 'member_left', actor: null }, 'group', i18n.t)).toBe('Alguien salió');
  });
});
