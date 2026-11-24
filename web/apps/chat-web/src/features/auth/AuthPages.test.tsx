import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from './AuthContext';
import { RegisterPage, SignInPage, safeReturnUrl } from './AuthPages';

const user = {
  id: '0193a1b2-0000-7000-8000-000000000001',
  email: 'ada@example.org',
  emailConfirmed: true,
  displayName: 'Ada Lovelace',
  avatar: { initials: 'AL' },
  preferences: { uiLanguage: 'en' },
  hasPassword: true,
  externalProviders: [],
  roles: [],
  permissions: [],
};

const base = {
  'GET /api/v1/auth/config': () => json(200, authConfig),
  'GET /api/v1/users/me': () => json(401, { code: 'unauthorized' }),
  'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
};

describe('auth pages', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    window.history.replaceState(null, '', '/sign-in');
    await i18n.changeLanguage('en');
  });

  it('shows a localized error for wrong credentials and clears the password', async () => {
    mockFetch({ ...base, 'POST /api/v1/auth/login': () => json(401, { code: 'invalid_credentials' }) });
    const u = userEvent.setup();
    render(<AuthProvider><SignInPage /></AuthProvider>);

    await u.type(screen.getByLabelText('Email address'), 'ada@example.org');
    await u.type(screen.getByLabelText('Password'), 'wrong password');
    await u.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The email address or password is incorrect.');
    expect(screen.getByLabelText('Password')).toHaveValue('');
  });

  it('welcomes back and can show the password while typing it', async () => {
    mockFetch(base);
    const u = userEvent.setup();
    render(<AuthProvider><SignInPage /></AuthProvider>);

    expect(screen.getByRole('heading', { level: 1, name: 'Welcome back' })).toBeInTheDocument();
    expect(screen.getByText('Sign in to continue to ChatX.')).toBeInTheDocument();
    const password = screen.getByLabelText('Password');
    await u.type(password, 'secret words');
    expect(password).toHaveAttribute('type', 'password');
    await u.click(screen.getByRole('button', { name: 'Show password' }));
    expect(password).toHaveAttribute('type', 'text');
    await u.click(screen.getByRole('button', { name: 'Hide password' }));
    expect(password).toHaveAttribute('type', 'password');
    expect(screen.getByRole('link', { name: 'Forgot your password?' })).toHaveAttribute('href', '/forgot-password');
  });

  it('signs in, applies the account language and goes to the requested page', async () => {
    window.history.replaceState(null, '', '/sign-in?returnUrl=%2Faccount');
    mockFetch({ ...base, 'POST /api/v1/auth/login': () => json(200, { ...user, preferences: { uiLanguage: 'es' } }) });
    const u = userEvent.setup();
    render(<AuthProvider><SignInPage /></AuthProvider>);

    await u.type(screen.getByLabelText('Email address'), 'ada@example.org');
    await u.type(screen.getByLabelText('Password'), 'correct horse battery');
    await u.keyboard('{Enter}');

    await vi.waitFor(() => expect(window.location.pathname).toBe('/account'));
    // Spanish texts are loaded on demand, so the switch completes a moment after sign-in.
    await vi.waitFor(() => expect(i18n.resolvedLanguage).toBe('es'));
  });

  it('offers to resend the confirmation link when the email is not confirmed', async () => {
    mockFetch({ ...base, 'POST /api/v1/auth/login': () => json(403, { code: 'email_not_confirmed' }) });
    const u = userEvent.setup();
    render(<AuthProvider><SignInPage /></AuthProvider>);

    await u.type(screen.getByLabelText('Email address'), 'ada@example.org');
    await u.type(screen.getByLabelText('Password'), 'correct horse battery');
    await u.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('link', { name: 'Send a new confirmation link' })).toHaveAttribute('href', '/resend-confirmation');
  });

  it('shows field-level validation errors from the API next to the field', async () => {
    window.history.replaceState(null, '', '/register');
    mockFetch({
      ...base,
      'POST /api/v1/auth/register': () => json(400, { code: 'validation_failed', errors: { password: ['password_too_short'] } }),
    });
    const u = userEvent.setup();
    render(<AuthProvider><RegisterPage /></AuthProvider>);

    await u.type(await screen.findByLabelText('Display name'), 'Ada');
    await u.type(screen.getByLabelText('Email address'), 'ada@example.org');
    await u.type(screen.getByLabelText('Password'), 'short');
    await u.click(screen.getByRole('button', { name: 'Create account' }));

    const password = screen.getByLabelText('Password');
    expect(await screen.findByText('The password is too short.')).toBeInTheDocument();
    expect(password).toHaveAttribute('aria-invalid', 'true');
  });

  it('accepts only same-app return paths', () => {
    expect(safeReturnUrl('/account')).toBe('/account');
    expect(safeReturnUrl('//evil.example')).toBe('/account');
    expect(safeReturnUrl('https://evil.example')).toBe('/account');
    expect(safeReturnUrl(null)).toBe('/account');
  });
});
