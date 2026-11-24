import { act } from 'react';
import { afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import i18n, { initI18n } from '../i18n/i18n';
import { resetCsrfToken } from '../shared/api/httpClient';
import { setThemePreference } from '../shared/theme';
import { authConfig, json, mockFetch } from '../test/fetchRoutes';
import { FakeHub } from '../test/fakeHub';
import { AuthProvider } from '../features/auth/AuthContext';
import { RealtimeProvider } from '../features/realtime/RealtimeContext';
import { NotificationsProvider } from '../features/notifications/NotificationsContext';
import { AppShell } from './AppShell';

const ana = { id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' } };
const ben = { id: 'u-ben', displayName: 'Ben', avatar: { initials: 'B' } };
const me = (theme: string) => ({
  ...ana, email: 'ana@example.org', emailConfirmed: true, hasPassword: true, externalProviders: [], roles: [], permissions: [],
  preferences: { uiLanguage: 'en', theme, desktopNotifications: false, notificationPreviews: true },
});

const summary = (id: string, title: string, unread: number, muted = false, type = 'direct') => ({
  id, type, scope: 'personal', organizationId: null, organizationName: null, title, counterpart: type === 'direct' ? ben : null, myRole: 'member',
  muted, lastSequence: 5, lastReadSequence: 5 - unread, unreadCount: unread, lastActivityAtUtc: '2026-11-22T10:00:00Z',
});

const message = (conversationId: string, eventId: string, body: string) => ({
  version: 1, eventId, conversationId,
  entry: { id: `m-${eventId}`, sequence: 6, kind: 'message', actor: ben, subject: null, text: null, body, createdAtUtc: new Date().toISOString(), version: 1 },
});

describe('AppShell (signed in)', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
    document.title = 'Knock Twice';
    window.history.replaceState(null, '', '/contacts');
  });

  afterEach(() => setThemePreference('system'));

  it('shows unread counts in the navigation and the tab title, notifies outside the open chat and applies the saved theme', async () => {
    const hub = new FakeHub();
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me('dark')),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/conversations': () => json(200, [summary('c1', 'Ben', 2), summary('c2', 'Noisy group', 7, true, 'group')]),
      'GET /api/v1/private-chats': () => json(200, []),
      'GET /api/v1/contacts': () => json(200, []),
      'GET /api/v1/contacts/requests': () => json(200, []),
      'GET /api/v1/contacts/blocked': () => json(200, []),
      'POST /api/v1/conversations/c1/delivered': () => json(200, { lastDeliveredSequence: 6 }),
      'POST /api/v1/conversations/c2/delivered': () => json(200, { lastDeliveredSequence: 6 }),
    });
    render(
      <AuthProvider>
        <RealtimeProvider factory={() => hub.asHub()}>
          <NotificationsProvider>
            <AppShell />
          </NotificationsProvider>
        </RealtimeProvider>
      </AuthProvider>,
    );

    // Chat-first navigation with the unread count of conversations that are not muted.
    const chats = await screen.findByRole('link', { name: /Chats/ });
    const nav = screen.getByRole('navigation', { name: 'Main navigation' });
    expect(await within(chats).findByLabelText('2 unread messages')).toHaveTextContent('2');
    expect(within(nav).getByRole('link', { name: /Profile/ })).toHaveAttribute('href', '/account');
    expect(document.title).toBe('(2) Knock Twice');

    // The account's saved theme wins over the system setting.
    expect(document.documentElement.getAttribute('data-bs-theme')).toBe('dark');

    // A message in a conversation that is not open: an in-app toast.
    await act(async () => hub.emit('MessageCreated', message('c1', 'e1', 'Lunch at noon?')));
    const toasts = screen.getByRole('region', { name: 'Notifications' });
    expect(await within(toasts).findByText('Lunch at noon?')).toBeInTheDocument();
    expect(within(toasts).getByText('Ben')).toBeInTheDocument();

    // Muted conversations never notify.
    await act(async () => hub.emit('MessageCreated', message('c2', 'e2', 'Spam spam spam')));
    expect(within(toasts).queryByText('Spam spam spam')).not.toBeInTheDocument();

    // Private messages never show their text outside the private chat.
    await act(async () =>
      hub.emit('PrivateMessageCreated', {
        version: 1, chatId: 'p1',
        message: { id: 'pm1', sequence: 1, author: ben, authorId: ben.id, clientMessageId: 'x', body: 'top secret', attachments: [], createdAtUtc: new Date().toISOString() },
      }),
    );
    expect(await within(toasts).findByText('Private message from Ben')).toBeInTheDocument();
    expect(screen.queryByText('top secret')).not.toBeInTheDocument();
  });
});
