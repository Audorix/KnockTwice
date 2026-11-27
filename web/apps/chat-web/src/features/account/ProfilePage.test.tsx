import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { AccountPage } from './AccountPage';

const ana = (photo: boolean) => ({
  id: 'u-ana', displayName: 'Ana Lopez', avatar: { initials: 'AL', photoUrl: photo ? '/api/v1/users/u-ana/photo?v=3' : null }, email: 'ana@example.org',
  emailConfirmed: true, hasPassword: true, externalProviders: [], roles: [], permissions: [], preferences: { uiLanguage: 'en', theme: 'dark' },
});

const session = (id: string, current: boolean) => ({
  id, description: current ? 'Chrome on Windows' : 'Safari on iPhone', current, persistent: true,
  createdAtUtc: '2026-11-20T10:00:00Z', lastSeenAtUtc: '2026-11-24T10:00:00Z',
});

describe('My profile (redesign 2026-11)', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  it('shows how you appear, the photo and the session count, and switches back to initials', async () => {
    window.history.replaceState(null, '', '/account');
    let photo = true;
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, ana(photo)),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/auth/sessions': () => json(200, [session('s1', true), session('s2', false)]),
      'DELETE /api/v1/users/me/photo': () => {
        photo = false;
        return json(200, ana(false));
      },
    });
    const actor = userEvent.setup();
    render(<AuthProvider><AccountPage /></AuthProvider>);

    expect(await screen.findByRole('heading', { name: 'My profile' })).toBeInTheDocument();
    const hero = screen.getByRole('region', { name: 'Your profile' });
    expect(within(hero).getByRole('heading', { name: 'Ana Lopez' })).toBeInTheDocument();
    expect(within(hero).getByText('Email verified')).toBeInTheDocument();
    expect(hero.querySelector('img')?.getAttribute('src')).toBe('/api/v1/users/u-ana/photo?v=3');
    expect(await screen.findByRole('tab', { name: 'Sessions 2' })).toBeInTheDocument();
    expect(screen.getByText("You're signed in on 2 devices.")).toBeInTheDocument();

    await actor.click(within(hero).getByRole('button', { name: 'Use initials' }));
    expect(await within(hero).findByRole('button', { name: 'Use initials' })).toBeDisabled();
    expect(hero.querySelector('img')).toBeNull();
  });

  it('saves a locally generated avatar style and stable seed', async () => {
    window.history.replaceState(null, '', '/account');
    let saved: { style: string | null; seed: string | null } | undefined;
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, ana(false)),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/auth/sessions': () => json(200, [session('s1', true)]),
      'PUT /api/v1/users/me/avatar': (init) => {
        saved = JSON.parse(String(init?.body)) as { style: string | null; seed: string | null };
        return json(200, { ...ana(false), avatar: { initials: 'AL', generatedStyle: saved.style, generatedSeed: saved.seed } });
      },
    });
    const actor = userEvent.setup();
    render(<AuthProvider><AccountPage /></AuthProvider>);

    const profileHero = await screen.findByRole('region', { name: 'Your profile' });
    await actor.click(within(profileHero).getAllByRole('button', { name: 'Change photo' })[1]!);
    await actor.click(await screen.findByRole('tab', { name: 'Illustrated avatar' }));
    await actor.click(screen.getByRole('option', { name: 'Pixel Art' }));
    await actor.click(screen.getByRole('button', { name: 'Use this avatar' }));

    expect(saved?.style).toBe('pixel-art');
    expect(saved?.seed).toBe('u-ana');
    const hero = await screen.findByRole('region', { name: 'Your profile' });
    await waitFor(() => expect(hero.querySelector('img')).toHaveAttribute('src', expect.stringContaining('data:image/svg+xml')));
  });

  it('picks up a sign-in from another browser when you come back or open Sessions', async () => {
    window.history.replaceState(null, '', '/account');
    let sessions = [session('s1', true)];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, ana(false)),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/auth/sessions': () => json(200, sessions),
    });
    const actor = userEvent.setup();
    render(<AuthProvider><AccountPage /></AuthProvider>);
    expect(await screen.findByRole('tab', { name: 'Sessions 1' })).toBeInTheDocument();

    // Signed in with Firefox meanwhile; coming back to this window shows it.
    sessions = [session('s1', true), { ...session('s3', false), description: 'Firefox on Windows' }];
    window.dispatchEvent(new Event('focus'));
    expect(await screen.findByRole('tab', { name: 'Sessions 2' })).toBeInTheDocument();

    // Opening the tab reloads too.
    sessions = [...sessions, { ...session('s4', false), description: 'Edge on Windows' }];
    await actor.click(screen.getByRole('tab', { name: 'Sessions 2' }));
    expect(await screen.findByText('Edge on Windows')).toBeInTheDocument();
    expect(screen.getByText('Firefox on Windows')).toBeInTheDocument();
  });

  it('edits links: add, reorder, choose who sees them, report a wrong address, then save', async () => {
    window.history.replaceState(null, '', '/account?tab=links');
    const saves: unknown[] = [];
    let stored = [{ platform: 'github', url: 'https://github.com/ana', visibility: 'contacts' }];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, ana(false)),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/auth/sessions': () => json(200, [session('s1', true)]),
      'GET /api/v1/users/me/links': () => json(200, stored),
      'PUT /api/v1/users/me/links': (init) => {
        const body = JSON.parse(String(init?.body)) as { links: typeof stored };
        saves.push(body.links);
        if (body.links.some((l) => l.platform === 'linkedin' && !l.url.includes('linkedin.com'))) {
          return json(400, { code: 'validation_failed', errors: { 'links[1].url': ['link_url_platform'] } });
        }
        stored = body.links;
        return json(200, stored);
      },
    });
    const actor = userEvent.setup();
    render(<AuthProvider><AccountPage /></AuthProvider>);

    // The saved link and the preview of what contacts see.
    const preview = await screen.findByRole('complementary', { name: 'Profile preview' });
    expect(await within(preview).findByRole('link', { name: /GitHub/ })).toHaveAttribute('href', 'https://github.com/ana');
    expect(screen.getByRole('button', { name: 'Save links' })).toBeDisabled();

    // A new row picks the next platform; a wrong address is reported at its row.
    await actor.click(screen.getByRole('button', { name: 'Add link' }));
    expect(screen.getByLabelText('Platform', { selector: '#link-platform-1' })).toHaveValue('linkedin');
    await actor.type(screen.getByLabelText('LinkedIn address'), 'https://example.com/ana');
    expect(screen.getByText('Pending edits')).toBeInTheDocument();
    await actor.click(screen.getByRole('button', { name: 'Save links' }));
    expect(await screen.findByText('This address does not belong to the chosen platform.')).toBeInTheDocument();

    // Fix it, keep it to yourself, move it first, and save.
    await actor.clear(screen.getByLabelText('LinkedIn address'));
    await actor.type(screen.getByLabelText('LinkedIn address'), 'https://linkedin.com/in/ana');
    await actor.selectOptions(screen.getByLabelText('Visible to', { selector: '#link-visibility-1' }), 'only_me');
    screen.getByRole('button', { name: /Link 2 of 2/ }).focus();
    await actor.keyboard('{ArrowUp}');
    await actor.click(screen.getByRole('button', { name: 'Save links' }));
    await vi.waitFor(() => expect(saves).toHaveLength(2));
    expect(saves[1]).toEqual([
      { platform: 'linkedin', url: 'https://linkedin.com/in/ana', visibility: 'only_me' },
      { platform: 'github', url: 'https://github.com/ana', visibility: 'contacts' },
    ]);
    expect(await screen.findByText('Your links are saved.')).toBeInTheDocument();
    // Only links for contacts show in the preview.
    expect(within(preview).queryByRole('link', { name: /LinkedIn/ })).not.toBeInTheDocument();
  });
});
