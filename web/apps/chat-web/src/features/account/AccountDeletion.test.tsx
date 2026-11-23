import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { labelDeletedUsers, resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { AccountPage } from './AccountPage';

const ana = {
  id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' }, email: 'ana@example.org', emailConfirmed: true, hasPassword: true,
  externalProviders: [], roles: [], permissions: [], preferences: { uiLanguage: 'en', theme: 'system' },
};

const routes = (check: Record<string, unknown>, onDelete?: (body: unknown) => Response) => ({
  'GET /api/v1/auth/config': () => json(200, authConfig),
  'GET /api/v1/users/me': () => json(200, ana),
  'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
  'GET /api/v1/auth/sessions': () => json(200, []),
  'GET /api/v1/users/me/deletion': () => json(200, check),
  'POST /api/v1/users/me/deletion': (init?: RequestInit) => (onDelete ? onDelete(JSON.parse(String(init?.body))) : json(204, undefined)),
});

describe('Account deletion (Phase 13)', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
    window.history.replaceState(null, '', '/account');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('lists the organizations to hand over before the account can be deleted', async () => {
    mockFetch(routes({ canDelete: false, blockingOrganizations: [{ organizationId: 'o1', name: 'Acme' }], lastAdministrator: false, requiresPassword: true }));
    const actor = userEvent.setup();
    render(<AuthProvider><AccountPage /></AuthProvider>);

    await actor.click(await screen.findByRole('button', { name: 'Delete my account…' }));
    expect(await screen.findByRole('link', { name: 'Acme' })).toHaveAttribute('href', '/organization?id=o1');
    expect(screen.queryByRole('button', { name: 'Delete my account permanently' })).not.toBeInTheDocument();
  });

  it('confirms with the password, shows a wrong password at the field, then signs out', async () => {
    const sent: unknown[] = [];
    let attempts = 0;
    mockFetch(routes({ canDelete: true, blockingOrganizations: [], lastAdministrator: false, requiresPassword: true }, (body) => {
      sent.push(body);
      attempts += 1;
      return attempts === 1
        ? json(400, { code: 'validation_failed', errors: { password: ['password_incorrect'] } })
        : json(204, undefined);
    }));
    const actor = userEvent.setup();
    render(<AuthProvider><AccountPage /></AuthProvider>);

    await actor.click(await screen.findByRole('button', { name: 'Delete my account…' }));
    const card = within(screen.getByRole('region', { name: 'Delete account' }));
    await actor.type(await card.findByLabelText('Your password'), 'wrong');
    await actor.click(card.getByRole('button', { name: 'Delete my account permanently' }));
    expect(await card.findByText('The current password is incorrect.')).toBeInTheDocument();

    await actor.type(card.getByLabelText('Your password'), 'right');
    await actor.click(screen.getByRole('button', { name: 'Delete my account permanently' }));
    await vi.waitFor(() => expect(window.location.pathname + window.location.search).toBe('/sign-in?accountDeleted=1'));
    expect(sent).toEqual([{ password: 'wrong' }, { password: 'right' }]);
  });

  it('shows deleted people with a localized name', async () => {
    await i18n.changeLanguage('es');
    const conversation = {
      type: 'direct', title: 'Deleted user',
      counterpart: { id: 'u1', displayName: 'Deleted user', avatar: { initials: 'DU' }, deleted: true },
      items: [{ actor: { id: 'u1', displayName: 'Deleted user', avatar: { initials: 'DU' }, deleted: true } }, { actor: { id: 'u2', displayName: 'Ben', avatar: { initials: 'B' } } }],
    };
    labelDeletedUsers(conversation);
    expect(conversation.title).toBe('Usuario eliminado');
    expect(conversation.counterpart.displayName).toBe('Usuario eliminado');
    expect(conversation.items.map((i) => i.actor.displayName)).toEqual(['Usuario eliminado', 'Ben']);
  });
});
