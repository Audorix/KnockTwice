import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
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
const acme = { id: 'org-1', name: 'Acme' };

const person = (n: number) => ({ id: `0193a1b2-0000-7000-8000-${String(100 + n).padStart(12, '0')}`, displayName: `Person ${String(n).padStart(2, '0')}`, avatar: { initials: 'P' } });

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
        contacts = [{ user: ben, sinceUtc: '2026-11-22T10:05:00Z', sharedOrganizations: [acme] }];
        return new Response(null, { status: 204 });
      },
    });
    const u = userEvent.setup();
    render(<AuthProvider><ContactsPage /></AuthProvider>);

    // Requests live in their own tab, with a count.
    await u.click(await screen.findByRole('tab', { name: /Requests 1/ }));
    const incoming = await screen.findByRole('region', { name: 'Requests for you' });
    await u.click(within(incoming).getByRole('button', { name: 'Accept' }));

    await u.click(screen.getByRole('tab', { name: /All contacts/ }));
    const row = (await screen.findByText('Ben Brown')).closest('tr')!;
    expect(within(row).getByText('Acme')).toBeInTheDocument();
    expect(within(row).getByText('Offline')).toBeInTheDocument();

    await u.type(screen.getByRole('searchbox', { name: 'Email address or name' }), 'ben@example.org');
    await u.click(screen.getByRole('button', { name: 'Search' }));
    const results = await screen.findByRole('status');
    await u.click(within(results).getByRole('button', { name: 'Add' }));

    expect(await screen.findByText('Contact request sent to Ben Brown.')).toBeInTheDocument();
    expect(sent).toEqual([{ userId: ben.id }]);
  });

  it('filters, sorts and pages the contacts, and opens a profile from the row menu', async () => {
    const contacts = Array.from({ length: 10 }, (_, i) => ({
      user: person(i + 1),
      sinceUtc: `2026-11-${String(10 + i).padStart(2, '0')}T10:00:00Z`,
      sharedOrganizations: [] as { id: string; name: string }[],
    }));
    contacts.push({ user: ben, sinceUtc: '2026-11-01T10:00:00Z', sharedOrganizations: [acme] });
    let removed = '';
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/contacts': () => json(200, contacts.filter((c) => c.user.id !== removed)),
      'GET /api/v1/contacts/requests': () => json(200, []),
      'GET /api/v1/contacts/blocked': () => json(200, []),
      [`DELETE /api/v1/contacts/${ben.id}`]: () => {
        removed = ben.id;
        return new Response(null, { status: 204 });
      },
      [`GET /api/v1/contacts/${ben.id}/details`]: () => json(200, { handle: 'benbrown', email: 'ben@example.org', emailShared: true }),
      [`GET /api/v1/users/${ben.id}/links`]: () => json(200, []),
      [`GET /api/v1/contacts/${person(1).id}/details`]: () => json(200, { handle: 'person01', email: null, emailShared: false }),
      [`GET /api/v1/users/${person(1).id}/links`]: () => json(200, []),
    });
    const u = userEvent.setup();
    render(<AuthProvider><ContactsPage /></AuthProvider>);

    // Eight per page, sorted by name: Ben first, then the page ends.
    expect(await screen.findByText('Showing 1–8 of 11 contacts')).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(9); // header + 8
    await u.click(screen.getByRole('button', { name: 'Page 2' }));
    expect(screen.getByText('Showing 9–11 of 11 contacts')).toBeInTheDocument();

    // Filtering by organization name and by status.
    await u.type(screen.getByRole('searchbox', { name: 'Filter your contacts' }), 'acme');
    expect(screen.getAllByRole('row')).toHaveLength(2);
    await u.clear(screen.getByRole('searchbox', { name: 'Filter your contacts' }));
    await u.selectOptions(screen.getByLabelText('Status'), 'online');
    expect(screen.getByText('No contacts match the filter.')).toBeInTheDocument();
    await u.selectOptions(screen.getByLabelText('Status'), 'all');

    // Newest first.
    await u.selectOptions(screen.getByLabelText('Sort'), 'recent');
    expect(within(screen.getAllByRole('row')[1]!).getByText('Person 10')).toBeInTheDocument();

    // A contact who keeps their email private: "Email not shared", and no copy button for it.
    await u.selectOptions(screen.getByLabelText('Sort'), 'nameAsc');
    await u.click(screen.getByRole('button', { name: 'Person 01' }));
    let profile = screen.getByRole('dialog', { name: 'Contact profile' });
    expect(await within(profile).findByText('@person01')).toBeInTheDocument();
    expect(within(profile).getByText('Email not shared')).toBeInTheDocument();
    expect(within(profile).queryByRole('button', { name: 'Copy email address' })).not.toBeInTheDocument();
    await u.keyboard('{Escape}');

    // The row menu opens the profile (owner mockup): photo and status, Message and View full
    // profile, details with one-click copy, shared organizations and the contact date.
    await u.click(screen.getByRole('button', { name: 'More actions for Ben Brown' }));
    await u.click(screen.getByRole('menuitem', { name: 'View profile' }));
    profile = screen.getByRole('dialog', { name: 'Contact profile' });
    expect(within(profile).getByText('Ben Brown')).toBeInTheDocument();
    expect(within(profile).getByText('Offline')).toBeInTheDocument();
    expect(within(profile).getByRole('button', { name: 'Message' })).toBeInTheDocument();
    expect(within(profile).getByRole('link', { name: 'View full profile' })).toHaveAttribute('href', `/person?id=${ben.id}`);
    expect(within(profile).getByText('Acme')).toBeInTheDocument();
    expect(within(profile).getByText(/Contact since/)).toBeInTheDocument();
    expect(await within(profile).findByText('ben@example.org')).toBeInTheDocument();
    await u.click(within(profile).getByRole('button', { name: 'Copy email address' }));
    expect(await navigator.clipboard.readText()).toBe('ben@example.org');
    expect(within(profile).getAllByRole('status').some((s) => s.textContent === 'Copied')).toBe(true);
    await u.click(within(profile).getByRole('button', { name: 'Copy Display ID' }));
    expect(await navigator.clipboard.readText()).toBe('@benbrown');

    // Remove and Block live under "Manage contact"; removing closes the drawer.
    expect(within(profile).queryByRole('button', { name: 'Remove contact' })).not.toBeInTheDocument();
    await u.click(within(profile).getByRole('button', { name: 'Manage contact' }));
    expect(within(profile).getByRole('menuitem', { name: 'Block contact' })).toBeInTheDocument();
    await u.click(within(profile).getByRole('menuitem', { name: 'Remove contact' }));
    expect(await screen.findByText('Showing 1–8 of 10 contacts')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('starts a private chat with a contact from the row menu', async () => {
    window.history.replaceState(null, '', '/contacts');
    const invited: string[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/contacts': () => json(200, [{ user: ben, sinceUtc: '2026-11-01T10:00:00Z', sharedOrganizations: [] }]),
      'GET /api/v1/contacts/requests': () => json(200, []),
      'GET /api/v1/contacts/blocked': () => json(200, []),
      'POST /api/v1/private-chats': (init) => {
        invited.push(String(init?.body));
        return json(201, { id: 'pc-1', status: 'invited', isInitiator: true, counterpart: ben, initiator: me, createdAtUtc: '2026-11-24T10:00:00Z' });
      },
    });
    const u = userEvent.setup();
    render(<AuthProvider><ContactsPage /></AuthProvider>);

    await u.click(await screen.findByRole('button', { name: 'More actions for Ben Brown' }));
    await u.click(screen.getByRole('menuitem', { name: 'Start private chat' }));
    await vi.waitFor(() => expect(window.location.search).toBe('?id=pc-1'));
    expect(window.location.pathname).toBe('/private');
    expect(invited).toEqual([JSON.stringify({ userId: ben.id })]);
  });
});
