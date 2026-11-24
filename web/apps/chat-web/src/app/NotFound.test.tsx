import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import i18n, { initI18n } from '../i18n/i18n';
import { resetCsrfToken } from '../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../test/fetchRoutes';
import { AuthProvider } from '../features/auth/AuthContext';
import { AppShell } from './AppShell';

describe('Unknown addresses (1.0)', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(401, { code: 'unauthorized' }),
      'GET /health/ready': () => json(200, { status: 'healthy', checks: [] }),
    });
  });

  it('says the page was not found instead of showing the home page', async () => {
    window.history.replaceState(null, '', '/no-such-page');
    render(<AuthProvider><AppShell /></AuthProvider>);
    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to the home page' })).toHaveAttribute('href', '/');
  });

  it('corrects an address with extra leading and trailing slashes instead of leaving the site', async () => {
    // The full address: on its own, "//sign-in/" would name another host.
    window.history.replaceState(null, '', `${window.location.origin}//sign-in/?returnUrl=%2Fcontacts`);
    expect(window.location.pathname).toBe('//sign-in/');
    render(<AuthProvider><AppShell /></AuthProvider>);
    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument();
    expect(window.location.pathname + window.location.search).toBe('/sign-in?returnUrl=%2Fcontacts');
  });

  it('ignores a trailing slash on a known address', async () => {
    window.history.replaceState(null, '', '/sign-in/');
    render(<AuthProvider><AppShell /></AuthProvider>);
    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Page not found' })).not.toBeInTheDocument();
  });
});
