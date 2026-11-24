import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { OrganizationsPage } from './OrganizationsPage';

const me = { id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' }, email: 'ana@example.org', emailConfirmed: true, hasPassword: true,
  externalProviders: [], roles: [], permissions: [], preferences: { uiLanguage: 'en' } };
const organization = (id: string, name: string, myRole: string) => ({ id, name, myRole, createdAtUtc: '2026-11-20T10:00:00Z', myPermissions: [] });
const many = [
  organization('o-0', 'Audorix', 'owner'),
  organization('o-1', 'Contoso Labs', 'administrator'),
  ...['Falcon Ventures', 'Iron Motors', 'Meridian Labs', 'Northwind Traders', 'Orbit Media', 'Orbit Travel', 'Quantum Software', 'Vertex Designs']
    .map((name, i) => organization(`o-${i + 2}`, name, 'member')),
];

describe('OrganizationsPage (redesign)', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
    window.history.replaceState(null, '', '/organizations');
  });

  it('lists organizations in tabs with roles, search, sorting and pages, and accepts an invitation', async () => {
    const calls: string[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/organizations': () => json(200, many),
      'GET /api/v1/organizations/invitations': () =>
        json(200, calls.includes('accept') ? [] : [{ id: 'i-1', organizationId: 'o-9', organizationName: 'Blue Bakery', role: 'member', invitedBy: { id: 'u-x', displayName: 'Xavi', avatar: { initials: 'X' } }, createdAtUtc: '2026-11-23T10:00:00Z', expiresAtUtc: '2026-12-01T00:00:00Z' }]),
      'POST /api/v1/organizations/invitations/i-1/accept': () => {
        calls.push('accept');
        return new Response(null, { status: 204 });
      },
    });
    const user = userEvent.setup();
    render(<AuthProvider><OrganizationsPage /></AuthProvider>);

    const audorix = (await screen.findByText('Audorix')).closest('tr')!;
    expect(screen.getByRole('tab', { name: 'All organizations 10' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Owned by me 1' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Member 9' })).toBeInTheDocument();
    expect(within(audorix).getByText('Owner')).toBeInTheDocument();
    expect(within(audorix).getByText('Organization')).toBeInTheDocument();
    expect(audorix).toHaveClass('is-owned');
    expect(within(audorix).getByRole('button', { name: 'Manage Audorix' })).toBeInTheDocument();
    expect(within(screen.getByText('Contoso Labs').closest('tr')!).getByRole('button', { name: 'Manage Contoso Labs' })).toBeInTheDocument();
    expect(within(screen.getByText('Iron Motors').closest('tr')!).getByRole('button', { name: 'Open Iron Motors' })).toBeInTheDocument();

    // Eight per page.
    expect(screen.getByText('Showing 1–8 of 10 organizations')).toBeInTheDocument();
    expect(screen.queryByText('Vertex Designs')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Page 2' }));
    expect(screen.getByText('Vertex Designs')).toBeInTheDocument();
    expect(screen.getByText('Showing 9–10 of 10 organizations')).toBeInTheDocument();

    // Sorting, searching and the tabs.
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sort' }), 'nameDesc');
    expect(screen.getAllByRole('row')[1]).toHaveTextContent('Vertex Designs');
    await user.type(screen.getByRole('searchbox', { name: 'Find an organization' }), 'orbit');
    expect(screen.getByText('Showing 1–2 of 2 organizations')).toBeInTheDocument();
    await user.clear(screen.getByRole('searchbox', { name: 'Find an organization' }));
    await user.click(screen.getByRole('tab', { name: 'Owned by me 1' }));
    expect(screen.getAllByRole('row')).toHaveLength(2);
    expect(screen.getByText('Audorix')).toBeInTheDocument();

    // Opening goes to the organization.
    await user.click(screen.getByRole('button', { name: 'Manage Audorix' }));
    expect(window.location.search).toBe('?id=o-0');
    window.history.replaceState(null, '', '/organizations');

    await user.click(screen.getByRole('button', { name: 'Join' }));
    expect(calls).toEqual(['accept']);
    expect(await screen.findByText('Audorix')).toBeInTheDocument();
    expect(screen.queryByText('Blue Bakery')).not.toBeInTheDocument();
  });

  it('opens the create panel with nothing to list, and creates an organization', async () => {
    const created: unknown[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/organizations': () => json(200, []),
      'GET /api/v1/organizations/invitations': () => json(200, []),
      'POST /api/v1/organizations': (init) => {
        created.push(JSON.parse(String(init?.body)));
        return json(201, organization('o-new', 'Acme Studio', 'owner'));
      },
    });
    const user = userEvent.setup();
    render(<AuthProvider><OrganizationsPage /></AuthProvider>);

    const panel = await screen.findByRole('region', { name: 'Create an organization' });
    expect(screen.getByText('You are not a member of any organization yet.')).toBeInTheDocument();
    expect(within(panel).getByText('You’ll become the owner.')).toBeInTheDocument();
    expect(within(panel).getByText('A General workspace is created automatically.')).toBeInTheDocument();
    expect(screen.getByText('Looking for an organization?')).toBeInTheDocument();

    await user.click(within(panel).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('region', { name: 'Create an organization' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'New organization' }));
    const again = screen.getByRole('region', { name: 'Create an organization' });
    expect(within(again).getByRole('button', { name: 'Create organization' })).toBeDisabled();
    await user.type(within(again).getByLabelText('Organization name'), 'Acme Studio');
    await user.click(within(again).getByRole('button', { name: 'Create organization' }));
    expect(created).toEqual([{ name: 'Acme Studio' }]);
    expect(window.location.search).toBe('?id=o-new');
  });
});
