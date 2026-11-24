import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { setThemePreference } from '../../shared/theme';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { AccountPage } from './AccountPage';

const user = (preferences: Record<string, unknown>) => ({
  id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' }, email: 'ana@example.org', emailConfirmed: true, hasPassword: true,
  externalProviders: [], roles: [], permissions: [], preferences: { uiLanguage: 'en', theme: 'system', desktopNotifications: false, notificationPreviews: true, ...preferences },
});

describe('Account preferences', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  afterEach(() => {
    setThemePreference('system');
    vi.unstubAllGlobals();
  });

  it('saves the theme to the account and asks the browser before enabling desktop notifications', async () => {
    const saved: unknown[] = [];
    let current = user({});
    const requestPermission = vi.fn(async () => 'granted' as NotificationPermission);
    vi.stubGlobal('Notification', Object.assign(function Notification() {}, { permission: 'default', requestPermission }));
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, current),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/auth/sessions': () => json(200, []),
      'PUT /api/v1/users/me/preferences': (init) => {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        saved.push(body);
        current = user({ ...current.preferences, ...body });
        return json(200, current);
      },
    });
    const actor = userEvent.setup();
    render(
      <AuthProvider>
        <AccountPage />
      </AuthProvider>,
    );

    await actor.click(await screen.findByRole('button', { name: 'Dark' }));
    expect(await screen.findByRole('button', { name: 'Dark', pressed: true })).toBeInTheDocument();
    expect(document.documentElement.getAttribute('data-bs-theme')).toBe('dark');

    await actor.click(screen.getByRole('tab', { name: 'Notifications' }));
    await actor.click(await screen.findByRole('switch', { name: /Desktop notifications/ }));
    expect(requestPermission).toHaveBeenCalledOnce();
    expect(await screen.findByRole('switch', { name: /Desktop notifications/ })).toBeChecked();
    expect(screen.getByRole('switch', { name: /Show message text/ })).toBeEnabled();
    expect(saved).toEqual([{ theme: 'dark' }, { desktopNotifications: true }]);
  });

  it('saves how often to email about unread messages, and explains when push is unavailable', async () => {
    const saved: unknown[] = [];
    let current = user({ emailNotifications: 'batched' });
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, current),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/auth/sessions': () => json(200, []),
      'PUT /api/v1/users/me/preferences': (init) => {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        saved.push(body);
        current = user({ ...current.preferences, ...body });
        return json(200, current);
      },
    });
    const actor = userEvent.setup();
    render(
      <AuthProvider>
        <AccountPage />
      </AuthProvider>,
    );

    await actor.click(await screen.findByRole('tab', { name: 'Notifications' }));
    await actor.selectOptions(await screen.findByLabelText('Email about unread messages'), 'daily');
    await vi.waitFor(() => expect(saved).toEqual([{ emailNotifications: 'daily' }]));
    expect(screen.getByLabelText('Email about unread messages')).toHaveValue('daily');
    // jsdom has no service workers: push is explained, not offered.
    expect(screen.getByText(/not available in this browser/)).toBeInTheDocument();
  });
});
