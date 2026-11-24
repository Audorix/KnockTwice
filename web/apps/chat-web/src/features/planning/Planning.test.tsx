import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { AuthProvider } from '../auth/AuthContext';
import { ConversationView } from '../conversations/ConversationView';
import { MentionsPage } from '../mentions/MentionsPage';
import { presetTimes } from './TimeMenu';

const ana = { id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' } };
const ben = { id: 'u-ben', displayName: 'Ben', avatar: { initials: 'B' } };
const me = { ...ana, email: 'ana@example.org', emailConfirmed: true, preferences: { uiLanguage: 'en' }, hasPassword: true, externalProviders: [], roles: [], permissions: [] };
const conversation = {
  id: 'c1', type: 'direct', scope: 'personal', organizationId: null, organizationName: null, title: 'Ben', counterpart: ben, myRole: 'member', muted: false,
  lastSequence: 2, lastReadSequence: 2, unreadCount: 0, lastActivityAtUtc: '2026-11-22T10:00:00Z',
};
const detail = {
  conversation,
  description: null,
  participants: [
    { user: ana, role: 'member', joinedAtUtc: '2026-11-22T09:00:00Z', lastDeliveredSequence: 2, lastReadSequence: 2 },
    { user: ben, role: 'member', joinedAtUtc: '2026-11-22T09:00:00Z', lastDeliveredSequence: 2, lastReadSequence: 2 },
  ],
};
const message = { id: 'm1', sequence: 2, kind: 'message', actor: ben, subject: null, text: null, body: 'Lunch?', createdAtUtc: '2026-11-22T09:01:00Z', version: 1 };
const history = { items: [message], nextCursor: null, hasMore: false };

describe('Planning (Phase 20)', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  it('restores and saves the draft, schedules a message and sets a reminder', async () => {
    const calls: { route: string; body: Record<string, unknown> }[] = [];
    const record = (route: string) => (init: RequestInit | undefined) => {
      calls.push({ route, body: JSON.parse(String(init?.body)) as Record<string, unknown> });
      return route === 'draft' ? new Response(null, { status: 204 }) : json(201, { id: 'x' });
    };
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/conversations/c1': () => json(200, detail),
      'GET /api/v1/conversations/c1/history': () => json(200, history),
      'GET /api/v1/drafts': () => json(200, [{ conversationId: 'c1', threadRootId: null, body: 'Sure, at', replyToId: 'm1', updatedAtUtc: '2026-11-23T08:00:00Z' }]),
      'PUT /api/v1/conversations/c1/draft': record('draft'),
      'POST /api/v1/conversations/c1/scheduled': record('scheduled'),
      'POST /api/v1/conversations/c1/messages/m1/reminder': record('reminder'),
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <ConversationView conversationId="c1" onChanged={() => undefined} />
      </AuthProvider>,
    );

    // The saved draft and its reply target come back.
    const box = await screen.findByLabelText('Message');
    await waitFor(() => expect(box).toHaveValue('Sure, at'));
    expect(screen.getByText(/Replying to Ben/)).toBeInTheDocument();

    // Typing saves it (debounced) with the reply target.
    await user.type(box, ' noon');
    await waitFor(() => expect(calls.some((c) => c.route === 'draft')).toBe(true), { timeout: 3000 });
    expect(calls.find((c) => c.route === 'draft')!.body).toEqual({ body: 'Sure, at noon', replyToId: 'm1', threadRootId: null });

    // Send later: tomorrow at 9:00.
    await user.click(screen.getByRole('button', { name: 'Send later…' }));
    await user.click(screen.getByRole('menuitem', { name: /Tomorrow/ }));
    expect(await screen.findByText(/Scheduled for/)).toBeInTheDocument();
    const scheduled = calls.find((c) => c.route === 'scheduled')!.body;
    expect(scheduled).toMatchObject({ body: 'Sure, at noon', replyToId: 'm1' });
    expect(new Date(String(scheduled.sendAtUtc)).getHours()).toBe(9);
    expect(box).toHaveValue('');

    // Remind me about Ben's message in an hour.
    await user.click(screen.getByRole('button', { name: 'More' }));
    await user.click(screen.getByRole('menuitem', { name: 'Remind me…' }));
    await user.click(screen.getByRole('menuitem', { name: /In 1 hour/ }));
    expect(await screen.findByText(/Reminder set for/)).toBeInTheDocument();
    const remindAt = new Date(String(calls.find((c) => c.route === 'reminder')!.body.remindAtUtc)).getTime();
    expect(Math.abs(remindAt - (Date.now() + 3_600_000))).toBeLessThan(120_000);
  });

  it('lists scheduled messages with failures, and reminders to mark done', async () => {
    const deleted: string[] = [];
    mockFetch({
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/scheduled': () =>
        json(200, [
          { id: 's1', conversationId: 'c1', conversationType: 'group', conversationTitle: 'Team', body: 'Standup **notes**', sendAtUtc: '2026-11-24T09:00:00Z', status: 'pending', failureCode: null, fileCount: 0, threadRootId: null, replyToId: null },
          { id: 's2', conversationId: 'c2', conversationType: 'group', conversationTitle: 'Old team', body: 'Bye all', sendAtUtc: '2026-11-23T09:00:00Z', status: 'failed', failureCode: 'conversation_not_found', fileCount: 0, threadRootId: null, replyToId: null },
        ]),
      'DELETE /api/v1/scheduled/s2': () => {
        deleted.push('s2');
        return new Response(null, { status: 204 });
      },
      'GET /api/v1/reminders': () =>
        json(200, [{ id: 'r1', conversationId: 'c1', conversationType: 'direct', conversationTitle: null, entry: message, remindAtUtc: '2026-11-23T09:00:00Z', status: 'due' }]),
      'DELETE /api/v1/reminders/r1': () => {
        deleted.push('r1');
        return new Response(null, { status: 204 });
      },
    });
    const user = userEvent.setup();

    window.history.replaceState(null, '', '/mentions?tab=scheduled');
    const { unmount } = render(<MentionsPage />);
    expect(await screen.findByText('Standup notes')).toBeInTheDocument();
    expect(screen.getByText('Not sent')).toBeInTheDocument();
    expect(screen.getByText(/Not sent: /)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Remove' }));
    expect(deleted).toContain('s2');
    unmount();

    window.history.replaceState(null, '', '/mentions?tab=reminders');
    render(<MentionsPage />);
    // The reminders tab (the "Coming up" column shows the next reminder too).
    const reminders = await screen.findByRole('region', { name: 'Reminders' });
    expect(await within(reminders).findByText('Lunch?')).toBeInTheDocument();
    await user.click(within(reminders).getByRole('button', { name: 'Done' }));
    expect(deleted).toContain('r1');
    expect(await within(reminders).findByText(/No reminders/)).toBeInTheDocument();
    window.history.replaceState(null, '', '/');
  });

  it('offers quick times in the future', () => {
    const now = new Date(2026, 10, 23, 14, 30); // a Monday
    const times = presetTimes(now);
    expect(times.map((x) => x.key)).toEqual(['in20', 'in60', 'in180', 'tomorrow', 'monday']);
    expect(times.find((x) => x.key === 'tomorrow')!.at).toEqual(new Date(2026, 10, 24, 9, 0));
    expect(times.find((x) => x.key === 'monday')!.at).toEqual(new Date(2026, 10, 30, 9, 0));
    expect(times.every((x) => x.at > now)).toBe(true);
  });
});
