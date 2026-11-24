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

  it('shows totals per tab, highlighted results, the message context, and applies tabs, order and paging', async () => {
    window.history.replaceState(null, '', '/search?q=presup');
    const requests: string[] = [];
    const entry = (id: string, sequence: number, body: string, extra: Record<string, unknown> = {}) => ({
      id, sequence, kind: 'message', actor: ben, subject: null, text: null, createdAtUtc: '2026-11-23T09:00:00Z', body, ...extra,
    });
    const found = entry('m1', 3, 'El presupuesto del viaje', { attachments: [{ id: 'f1', fileName: 'Presupuesto.pdf', contentType: 'application/pdf', kind: 'file', size: 2048 }] });
    const counts = { total: 2, withFiles: 1 };
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, ana),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/contacts': () => json(200, []),
      'GET /api/v1/conversations': () => json(200, [conversation('c1', 'Presupuestos 2026'), conversation('c2', 'Trip')]),
      'GET /api/v1/search?q=presup': () => {
        requests.push('first');
        return json(200, { items: [{ conversation: { ...conversation('c2', 'Trip'), organizationName: 'Iron Motors' }, message: found }], nextCursor: 'next', counts });
      },
      'GET /api/v1/search?q=presup&cursor=next': () => {
        requests.push('more');
        return json(200, { items: [], nextCursor: null, counts: null });
      },
      'GET /api/v1/search?q=presup&hasFiles=true': () => {
        requests.push('files');
        return json(200, { items: [], nextCursor: null, counts });
      },
      'GET /api/v1/search?q=presup&hasFiles=true&order=oldest': () => {
        requests.push('files-oldest');
        return json(200, { items: [], nextCursor: null, counts });
      },
      'GET /api/v1/conversations/c2/messages/m1/context?size=3': () =>
        json(200, { items: [entry('m0', 2, 'Hola'), found, entry('m2', 4, 'Gracias')], hasOlder: false, hasNewer: true }),
      'GET /api/v1/conversations/c2': () => json(200, { conversation: conversation('c2', 'Trip'), description: null, participants: [{ user: ana }, { user: ben }] }),
      'PUT /api/v1/conversations/c2/messages/m1/saved': (init) => {
        requests.push(`saved ${String(init?.body)}`);
        return new Response(null, { status: 204 });
      },
    });
    const actor = userEvent.setup();
    render(<AuthProvider><SearchPage /></AuthProvider>);

    // Quick jump to a conversation whose name matches.
    expect(await screen.findByRole('link', { name: 'Presupuestos 2026' })).toHaveAttribute('href', '/conversations?id=c1');
    // Totals per tab and the summary line.
    expect(await screen.findByText('2 results for "presup"')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'All results 2' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Messages 1' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Files 1' })).toBeInTheDocument();
    expect(screen.getByText('Showing 1–1 of 2')).toBeInTheDocument();

    // The result: highlighted words, its file, place, and a link to the message (Phase 18).
    const card = screen.getByRole('button', { name: /^Ben/ });
    expect(within(card).getByText('presupuesto').tagName).toBe('MARK');
    expect(within(card).getByText('Trip · Iron Motors')).toBeInTheDocument();
    expect(screen.getByText('Presupuesto', { selector: '.cx-file-chip mark' })).toBeInTheDocument(); // file names match too
    expect(screen.getByRole('link', { name: 'View in conversation' })).toHaveAttribute('href', '/conversations?id=c2&message=m1');

    // Selecting it shows the messages around it.
    await actor.click(card);
    const context = await screen.findByRole('complementary', { name: 'Message context' });
    expect(await within(context).findByText('Hola')).toBeInTheDocument();
    expect(within(context).getByText('Gracias')).toBeInTheDocument();
    expect(await within(context).findByText('Iron Motors · 2 participants')).toBeInTheDocument();
    expect(within(context).getByRole('link', { name: 'Open conversation' })).toHaveAttribute('href', '/conversations?id=c2&message=m1');
    expect(within(context).getByRole('link', { name: /Download Presupuesto.pdf/ })).toHaveAttribute('href', expect.stringContaining('/attachments/f1/content?download=true'));

    // Save from the result card.
    await actor.click(screen.getByRole('button', { name: 'Save' }));
    await vi.waitFor(() => expect(requests).toContain('saved {"saved":true}'));
    expect(screen.getByRole('button', { name: 'Remove from saved' })).toHaveAttribute('aria-pressed', 'true');

    await actor.click(screen.getByRole('button', { name: 'Load more' }));
    await vi.waitFor(() => expect(requests).toContain('more'));

    // "With files" is the Files tab; the order applies on top.
    await actor.click(screen.getByRole('checkbox', { name: 'With files' }));
    expect(await screen.findByText('No messages match "presup".')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Files 1' })).toHaveAttribute('aria-selected', 'true');
    await actor.selectOptions(screen.getByRole('combobox', { name: 'Order' }), 'oldest');
    await vi.waitFor(() => expect(requests).toContain('files-oldest'));
    expect(requests.filter((r) => !r.startsWith('saved'))).toEqual(['first', 'more', 'files', 'files-oldest']);
  });

  it('starts filtered to a conversation from the link in a conversation, and clears filters', async () => {
    window.history.replaceState(null, '', '/search?conversation=c2');
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, ana),
      'GET /api/v1/conversations': () => json(200, [conversation('c1', 'Presupuestos 2026'), { ...conversation('c2', 'customers'), type: 'channel' }]),
      'GET /api/v1/conversations/c2': () => json(200, { conversation: conversation('c2', 'customers'), description: null, participants: [{ user: ana }, { user: ben }] }),
      'GET /api/v1/contacts': () => json(200, []),
    });
    const actor = userEvent.setup();
    render(<AuthProvider><SearchPage /></AuthProvider>);

    expect(await screen.findByText('# customers', { selector: '.cx-filter-chip' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Conversation' })).toHaveValue('c2');
    await actor.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(screen.getByRole('combobox', { name: 'Conversation' })).toHaveValue('');
    expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
    window.history.replaceState(null, '', '/');
  });
});
