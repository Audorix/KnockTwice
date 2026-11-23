import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { SearchPage } from './SearchPage';
import { highlight, queryTerms } from './searchApi';

const ana = {
  id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' }, email: 'ana@example.org', emailConfirmed: true, hasPassword: true,
  externalProviders: [], roles: [], permissions: [], preferences: { uiLanguage: 'en', theme: 'system' },
};
const ben = { id: 'u-ben', displayName: 'Ben', avatar: { initials: 'B' } };
const conversation = (id: string, title: string) => ({
  id, type: 'group', scope: 'personal', organizationId: null, organizationName: null, title, counterpart: null, myRole: 'member', muted: false,
  lastSequence: 5, lastReadSequence: 5, unreadCount: 0, lastActivityAtUtc: '2026-11-23T09:00:00Z',
});

describe('Search (Phase 14)', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('marks words that start with a query term, ignoring accents and case', () => {
    const terms = queryTerms('PRESUP cafe');
    expect(terms).toEqual(['presup', 'cafe']);
    expect(highlight('El presupuesto del Café, ya.', terms)).toEqual([
      { text: 'El ', match: false },
      { text: 'presupuesto', match: true },
      { text: ' del ', match: false },
      { text: 'Café', match: true },
      { text: ', ya.', match: false },
    ]);
  });

  it('shows matching conversations and messages, applies filters and loads more', async () => {
    window.history.replaceState(null, '', '/search?q=presup');
    const requests: string[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, ana),
      'GET /api/v1/conversations': () => json(200, [conversation('c1', 'Presupuestos 2026'), conversation('c2', 'Trip')]),
      'GET /api/v1/search?q=presup': () => {
        requests.push('first');
        return json(200, {
          items: [{
            conversation: conversation('c2', 'Trip'),
            message: { id: 'm1', sequence: 3, kind: 'message', actor: ben, subject: null, text: null, createdAtUtc: '2026-11-23T09:00:00Z', body: 'El presupuesto del viaje' },
          }],
          nextCursor: 'next',
        });
      },
      'GET /api/v1/search?q=presup&cursor=next': () => {
        requests.push('more');
        return json(200, { items: [], nextCursor: null });
      },
      'GET /api/v1/search?q=presup&hasFiles=true': () => {
        requests.push('files');
        return json(200, { items: [], nextCursor: null });
      },
    });
    const actor = userEvent.setup();
    render(<AuthProvider><SearchPage /></AuthProvider>);

    // Quick jump to a conversation whose name matches.
    expect(await screen.findByRole('link', { name: 'Presupuestos 2026' })).toHaveAttribute('href', '/conversations?id=c1');
    const result = await screen.findByRole('link', { name: /Ben.*in Trip/ });
    // Phase 18: results open the conversation at the message.
    expect(result).toHaveAttribute('href', '/conversations?id=c2&message=m1');
    expect(within(result).getByText('presupuesto').tagName).toBe('MARK');

    await actor.click(screen.getByRole('button', { name: 'More results' }));
    await vi.waitFor(() => expect(requests).toContain('more'));

    await actor.click(screen.getByRole('checkbox', { name: 'With files' }));
    expect(await screen.findByText('No messages match "presup".')).toBeInTheDocument();
    expect(requests).toEqual(['first', 'more', 'files']);
  });
});
