import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { PrivateChatPage } from './PrivateChatPage';

const ana = { id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' } };
const ben = { id: 'u-ben', displayName: 'Ben', avatar: { initials: 'B' } };
const me = { ...ben, email: 'ben@example.org', emailConfirmed: true, preferences: { uiLanguage: 'en' }, hasPassword: true, externalProviders: [], roles: [], permissions: [] };

const chat = (status: 'invited' | 'active') => ({
  id: 'p1',
  status,
  isInitiator: false,
  counterpart: ana,
  initiator: ana,
  createdAtUtc: '2026-11-22T10:00:00Z',
  acceptedAtUtc: status === 'active' ? '2026-11-22T10:01:00Z' : null,
  invitationExpiresAtUtc: '2026-11-22T10:10:00Z',
  usedBytes: 0,
  maxBytes: 50 * 1024 * 1024,
  maxFileSize: 10 * 1024 * 1024,
});

describe('PrivateChatPage', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
    window.history.replaceState(null, '', '/private?id=p1');
  });

  it('explains the rules to the invitee, then chats until the chat is gone and forgets everything', async () => {
    let status: 'invited' | 'active' = 'invited';
    let gone = false;
    const messages: unknown[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/attachments/limits': () =>
        json(200, { maxFileSize: 1024, maxPerMessage: 10, userQuota: 0, allowedExtensions: ['png'], scanningEnabled: false }),
      'GET /api/v1/private-chats/p1': () => json(200, { chat: chat(status), messages }),
      'POST /api/v1/private-chats/p1/accept': () => {
        status = 'active';
        return json(200, chat('active'));
      },
      'POST /api/v1/private-chats/p1/messages': (init) => {
        if (gone) return json(404, { code: 'private_chat_not_found' });
        const body = JSON.parse(String(init?.body)) as { clientMessageId: string; body: string };
        const message = { id: 'pm1', sequence: 1, author: ben, authorId: ben.id, clientMessageId: body.clientMessageId, body: body.body, attachments: [], createdAtUtc: new Date().toISOString() };
        messages.push(message);
        return json(201, message);
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <PrivateChatPage />
      </AuthProvider>,
    );

    // The invitation states the lifecycle before anything can be said.
    expect(await screen.findByText(/Ana invites you to a private chat/)).toBeInTheDocument();
    const rules = screen.getByRole('list');
    expect(within(rules).getByText(/Nothing is saved/)).toBeInTheDocument();
    expect(within(rules).getByText(/as long as Ana’s sign-in session/)).toBeInTheDocument();
    expect(within(rules).getByText(/erased when the chat ends/)).toBeInTheDocument();
    expect(within(rules).getByText(/screenshots/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Message')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Accept and join' }));
    expect(await screen.findByRole('note')).toHaveTextContent(/kept only in server memory/);
    expect(screen.getByText(/Ends when Ana ends it/)).toBeInTheDocument();
    // Only the initiator can end an active chat.
    expect(screen.queryByRole('button', { name: 'End and erase' })).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('Message'), 'Just between us{Enter}');
    expect(await screen.findByText('Just between us')).toBeInTheDocument();

    // The chat ended on the server: the next request reveals it, and nothing stays on screen.
    gone = true;
    await user.type(screen.getByLabelText('Message'), 'Still there?{Enter}');
    expect(await screen.findByText('This private chat has ended')).toBeInTheDocument();
    expect(screen.getByText(/All of its messages and files have been erased/)).toBeInTheDocument();
    expect(screen.queryByText('Just between us')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Message')).not.toBeInTheDocument();
  });

  it('lets the initiator send an invitation after reading the rules', async () => {
    window.history.replaceState(null, '', '/private?with=u-ben&name=Ben');
    const started: unknown[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, { ...me, ...ana, email: 'ana@example.org' }),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'POST /api/v1/private-chats': (init) => {
        started.push(JSON.parse(String(init?.body)));
        return json(201, { ...chat('invited'), id: 'p2', isInitiator: true, counterpart: ben });
      },
      'GET /api/v1/attachments/limits': () => json(200, { maxFileSize: 1024, maxPerMessage: 10, userQuota: 0, allowedExtensions: ['png'], scanningEnabled: false }),
      'GET /api/v1/private-chats/p2': () => json(200, { chat: { ...chat('invited'), id: 'p2', isInitiator: true, counterpart: ben }, messages: [] }),
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <PrivateChatPage />
      </AuthProvider>,
    );

    expect(await screen.findByRole('heading', { name: 'Private chat with Ben' })).toBeInTheDocument();
    expect(screen.getByText(/as long as your current sign-in session/)).toBeInTheDocument();
    expect(screen.getByText(/Only this browser session can see it/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Send invitation' }));
    expect(await screen.findByText(/Waiting for Ben to accept/)).toBeInTheDocument();
    expect(started).toEqual([{ userId: 'u-ben' }]);
    expect(screen.getByRole('button', { name: 'Cancel invitation' })).toBeInTheDocument();
  });
});
