import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { ContactsPage } from './ContactsPage';

const me = {
  id: '0193a1b2-0000-7000-8000-000000000001',
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
const ben = { id: '0193a1b2-0000-7000-8000-000000000002', displayName: 'Ben Brown', avatar: { initials: 'BB' } };

describe('ContactsPage', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    window.history.replaceState(null, '', '/contacts');
    await i18n.changeLanguage('en');
  });

  it('finds a person, sends a request and answers an incoming one', async () => {
    let requests: unknown[] = [{ id: 'r1', direction: 'incoming', user: ben, createdAtUtc: '2026-11-22T10:00:00Z' }];
    let contacts: unknown[] = [];
    const sent: unknown[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/contacts': () => json(200, contacts),
      'GET /api/v1/contacts/requests': () => json(200, requests),
      'GET /api/v1/contacts/blocked': () => json(200, []),
      'GET /api/v1/users/search?q=ben%40example.org': () => json(200, [ben]),
      'POST /api/v1/contacts/requests': (init) => {
        sent.push(JSON.parse(String(init?.body)));
        return json(200, { status: 'pending', request: null });
      },
      'POST /api/v1/contacts/requests/r1/accept': () => {
        requests = [];
        contacts = [{ user: ben, sinceUtc: '2026-11-22T10:05:00Z' }];
        return new Response(null, { status: 204 });
      },
    });
    const u = userEvent.setup();
    render(<AuthProvider><ContactsPage /></AuthProvider>);

    const incoming = await screen.findByRole('region', { name: 'Requests for you' });
    await u.click(within(incoming).getByRole('button', { name: 'Accept' }));
    const list = await screen.findByRole('region', { name: '1 contact' });
    expect(within(list).getByText('Ben Brown')).toBeInTheDocument();

    await u.type(screen.getByRole('searchbox', { name: 'Email address or name' }), 'ben@example.org');
    await u.click(screen.getByRole('button', { name: 'Search' }));
    const results = await screen.findByRole('status');
    await u.click(within(results).getByRole('button', { name: 'Add' }));

    expect(await screen.findByText('Contact request sent to Ben Brown.')).toBeInTheDocument();
    expect(sent).toEqual([{ userId: ben.id }]);
  });
});
