import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { json, mockFetch } from '../../test/fetchRoutes';
import { MentionsPage } from './MentionsPage';

const ben = { id: 'u-ben', displayName: 'Ben', avatar: { initials: 'B' } };
const entry = (id: string, body: string, extra: Record<string, unknown> = {}) => ({
  id, sequence: 4, kind: 'message', actor: ben, subject: null, text: null, body, createdAtUtc: '2026-11-23T10:00:00Z', version: 1, ...extra,
});

describe('MentionsPage', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  it('lists mentions with their place and unread state, and loads older ones', async () => {
    mockFetch({
      'GET /api/v1/mentions': () =>
        json(200, {
          items: [
            { conversationId: 'c1', conversationType: 'channel', conversationTitle: 'general', entry: entry('m1', 'Ana, can you check?'), unread: true },
            { conversationId: 'c2', conversationType: 'direct', conversationTitle: null, entry: entry('m2', 'Thanks @Ana', { threadRootId: 'r1' }), unread: false },
          ],
          nextCursor: 's1:abc',
          hasMore: true,
        }),
      'GET /api/v1/mentions?before=s1%3Aabc': () =>
        json(200, { items: [{ conversationId: 'c3', conversationType: 'group', conversationTitle: 'Team', entry: entry('m3', 'Old one'), unread: false }], nextCursor: null, hasMore: false }),
    });
    const user = userEvent.setup();
    render(<MentionsPage />);

    expect(await screen.findByText('Ana, can you check?')).toBeInTheDocument();
    expect(screen.getByText('# general', { selector: '.cx-mention-place' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: '# general' })).toBeInTheDocument(); // the conversation filter
    expect(screen.getByText('mentioned you in a direct message')).toBeInTheDocument();
    expect(screen.getByText('· in a thread')).toBeInTheDocument();
    expect(screen.getAllByRole('img', { name: 'Unread' })).toHaveLength(1);
    // Redesign: one "Mark as read" per unread mention, and "Mark all as read" while any is unread.
    expect(screen.getAllByRole('button', { name: 'Mark as read' })).toHaveLength(1);
    expect(await screen.findByRole('button', { name: 'Mark all as read' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Load older mentions' }));
    expect(await screen.findByText('Old one')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load older mentions' })).not.toBeInTheDocument();
  });

  it('lists saved messages in the second tab and removes one (Phase 18)', async () => {
    window.history.replaceState(null, '', '/mentions?tab=saved');
    const calls: string[] = [];
    mockFetch({
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/saved': () =>
        json(200, {
          items: [{ conversationId: 'c1', conversationType: 'group', conversationTitle: 'Team', entry: entry('m7', 'Remember the deadline'), savedAtUtc: '2026-11-23T11:00:00Z' }],
          nextCursor: null,
          hasMore: false,
        }),
      'PUT /api/v1/conversations/c1/messages/m7/saved': (init) => {
        calls.push(String(init?.body));
        return new Response(null, { status: 204 });
      },
    });
    const user = userEvent.setup();
    render(<MentionsPage />);

    expect(await screen.findByRole('heading', { name: 'Saved messages' })).toBeInTheDocument();
    expect(await screen.findByText('Remember the deadline')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /^Saved messages/ })).toHaveAttribute('aria-current', 'page');
    await user.click(screen.getByRole('button', { name: 'Remove from saved' }));
    expect(calls).toEqual(['{"saved":false}']);
    expect(await screen.findByText(/not saved any messages/)).toBeInTheDocument();
    window.history.replaceState(null, '', '/');
  });
});
