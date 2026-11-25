import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { DemoDataCard } from './DemoDataCard';

const me = {
  id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A', photoUrl: null }, email: 'ana@example.org', emailConfirmed: true, hasPassword: true,
  externalProviders: [], roles: [], permissions: ['installation.demo_data'], preferences: { uiLanguage: 'en' },
};
const counts = (n: number) => ({ users: n * 300, organizations: n * 50, contacts: n * 900, conversations: n * 975, messages: n * 24000, activity: n * 100, support: n * 500, photos: n * 300 });
const steps = (fraction: number) => [
  ['people', 300], ['companies', 50], ['contacts', 900], ['chats', 175], ['channels', 800], ['messages', 24000], ['activity', 100], ['support', 500], ['photos', 300],
].map(([key, total]) => ({ key, total, done: Math.round((total as number) * fraction) }));
const job = (state: string, fraction: number) => ({
  id: 'j-1', state, startedAtUtc: new Date().toISOString(), finishedAtUtc: state === 'running' ? null : new Date().toISOString(),
  steps: steps(fraction),
  result: state === 'completed' ? { created: counts(1), total: counts(1), userEmails: ['amelia.adams@demo.chatx.test'], password: 'Demo123!' } : null,
  errorCode: null,
});

describe('Demo data loading with progress (2026-11-25)', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  it('shows progress per part while loading, then a confirmation; OK hides the progress', async () => {
    const polls = [job('running', 0.5), job('completed', 1)];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, { ...authConfig, demoDataEnabled: true }),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/admin/demo-data': () => json(200, counts(0)),
      'GET /api/v1/admin/demo-data/jobs/current': () => (polls.length === 2 && !started ? new Response(null, { status: 204 }) : json(200, polls.shift() ?? job('completed', 1))),
      'POST /api/v1/admin/demo-data/jobs': () => {
        started = true;
        return json(202, job('running', 0));
      },
    });
    let started = false;
    const user = userEvent.setup();
    render(<AuthProvider><DemoDataCard /></AuthProvider>);

    await user.click(await screen.findByRole('button', { name: 'Populate demo data' }));
    expect(await screen.findByRole('button', { name: 'Loading demo data…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Clear demo data' })).toBeDisabled();
    const people = screen.getByRole('progressbar', { name: 'People' });
    expect(people).toHaveAttribute('aria-valuenow', '0');
    expect(screen.getAllByRole('progressbar')).toHaveLength(9);

    // Halfway, then everything at 100% and the confirmation.
    expect(await screen.findByText('50% complete', undefined, { timeout: 4000 })).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Messages' })).toHaveAttribute('aria-valuenow', '50');
    const dialog = await screen.findByRole('dialog', { name: 'Demo data is ready' }, { timeout: 4000 });
    expect(screen.getByRole('progressbar', { name: 'Messages' })).toHaveAttribute('aria-valuenow', '100');
    expect(within(dialog).getByText('Everything is loaded. This is the demo data you now have:')).toBeInTheDocument();
    expect(within(dialog).getByText('Every demo account uses the password Demo123!. For example:')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'OK' })).toHaveFocus();

    await user.click(within(dialog).getByRole('button', { name: 'OK' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(screen.getByText(/^Added 300 people, 50 companies/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Populate demo data' })).toBeEnabled();
  });

  it('clears after confirmation with progress per part, then confirms what was removed', async () => {
    const clearSteps = (fraction: number) => [['files', 302], ['messages', 25817], ['conversations', 1776], ['companies', 50], ['people', 300]]
      .map(([key, total]) => ({ key, total, done: fraction === 1 ? total : key === 'files' ? Math.round((total as number) * fraction) : 0 }));
    const clearJob = (state: string, fraction: number) => ({
      id: 'j-2', kind: 'clear', state, startedAtUtc: new Date().toISOString(), finishedAtUtc: null, steps: clearSteps(fraction),
      result: null, removed: state === 'completed' ? counts(1) : null, errorCode: null,
    });
    const polls = [clearJob('running', 0.5), clearJob('completed', 1)];
    let started = false;
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, { ...authConfig, demoDataEnabled: true }),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/admin/demo-data': () => json(200, counts(1)),
      'GET /api/v1/admin/demo-data/jobs/current': () => (!started ? new Response(null, { status: 204 }) : json(200, polls.shift() ?? clearJob('completed', 1))),
      'POST /api/v1/admin/demo-data/jobs?action=clear': () => {
        started = true;
        return json(202, clearJob('running', 0));
      },
    });
    const user = userEvent.setup();
    render(<AuthProvider><DemoDataCard /></AuthProvider>);

    await user.click(await screen.findByRole('button', { name: 'Clear demo data' }));
    const ask = screen.getByRole('alertdialog', { name: 'Remove all demo data?' });
    await user.click(within(ask).getByRole('button', { name: 'Remove demo data' }));

    expect(await screen.findByText('Clearing demo data')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Clearing demo data…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Populate demo data' })).toBeDisabled();
    expect(screen.getByRole('progressbar', { name: 'Files and profile photos' })).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'People and their contacts' })).toHaveAttribute('aria-valuenow', '0');

    expect(await screen.findByText('10% complete', undefined, { timeout: 4000 })).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Files and profile photos' })).toHaveAttribute('aria-valuenow', '50');
    const dialog = await screen.findByRole('dialog', { name: 'Demo data removed' }, { timeout: 4000 });
    expect(within(dialog).getByText('All demo data is gone. This is what was removed:')).toBeInTheDocument();
    expect(within(dialog).getByText('Conversations and data that real people created were kept.')).toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'OK' }));
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(screen.getByText(/^Removed 300 demo people, 50 companies/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Clear demo data' })).toBeDisabled();
  });
});
