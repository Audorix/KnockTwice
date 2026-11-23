import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
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

describe('ConversationsPage', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  it('lists conversations with unread counts, shows history and marks the open conversation read', async () => {
    window.history.replaceState(null, '', '/conversations?id=c-team');
    const reads: unknown[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': csrf,
      'GET /api/v1/conversations': () => json(200, [group]),
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

  it('creates a group from contacts', async () => {
    window.history.replaceState(null, '', '/conversations');
    let created: unknown = null;
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': csrf,
      'GET /api/v1/conversations': () => json(200, []),
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
