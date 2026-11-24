import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { authConfig, json, mockFetch } from '../../test/fetchRoutes';
import { FakeHub } from '../../test/fakeHub';
import { AuthProvider } from '../auth/AuthContext';
import { RealtimeProvider } from '../realtime/RealtimeContext';
import { ConversationView } from './ConversationView';

const ana = { id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' } };
const ben = { id: 'u-ben', displayName: 'Ben', avatar: { initials: 'B' } };
const me = { ...ana, email: 'ana@example.org', emailConfirmed: true, preferences: { uiLanguage: 'en' }, hasPassword: true, externalProviders: [], roles: [], permissions: [] };

const conversation = {
  id: 'c1',
  type: 'direct',
  scope: 'personal',
  organizationId: null,
  organizationName: null,
  title: 'Ben',
  counterpart: ben,
  myRole: 'member',
  muted: false,
  lastSequence: 2,
  lastReadSequence: 2,
  unreadCount: 0,
  lastActivityAtUtc: '2026-11-22T10:00:00Z',
};

const detail = (benDelivered: number, benRead: number) => ({
  conversation,
  description: null,
  participants: [
    { user: ana, role: 'member', joinedAtUtc: '2026-11-22T09:00:00Z', lastDeliveredSequence: 3, lastReadSequence: 3 },
    { user: ben, role: 'member', joinedAtUtc: '2026-11-22T09:00:00Z', lastDeliveredSequence: benDelivered, lastReadSequence: benRead },
  ],
});

const history = {
  items: [
    { id: 'e1', sequence: 1, kind: 'created', actor: ana, subject: null, text: null, createdAtUtc: '2026-11-22T09:00:00Z' },
    { id: 'm1', sequence: 2, kind: 'message', actor: ben, subject: null, text: null, body: 'Hi Ana!\nHow are you?', createdAtUtc: '2026-11-22T09:01:00Z', version: 1, clientMessageId: 'b-1' },
  ],
  nextCursor: null,
  hasMore: false,
};

describe('ConversationView', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  it('catches up after a reconnect with what changed while away, without reloading history', async () => {
    const hub = new FakeHub();
    let historyLoads = 0;
    const asked: string[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/conversations/c1': () => json(200, detail(2, 2)),
      'GET /api/v1/conversations/c1/history': () => {
        historyLoads++;
        return json(200, history);
      },
      'GET /api/v1/changes/cursor': () => json(200, { cursor: 'cur-1' }),
      'GET /api/v1/changes?since=cur-1': () => {
        asked.push('cur-1');
        return json(200, {
          reset: false,
          cursor: 'cur-2',
          conversationIds: ['c1'],
          entries: [
            { conversationId: 'c1', entry: { ...history.items[1], body: 'Hello again, Ana', version: 2, editedAtUtc: '2026-11-22T09:05:00Z' } },
            { conversationId: 'c1', entry: { id: 'm4', sequence: 3, kind: 'message', actor: ben, subject: null, text: null, body: 'Missed while away', createdAtUtc: '2026-11-22T09:06:00Z', version: 1, clientMessageId: 'b-4' } },
            { conversationId: 'c-other', entry: { id: 'x1', sequence: 9, kind: 'message', actor: ben, subject: null, text: null, body: 'Another chat', createdAtUtc: '2026-11-22T09:06:00Z', version: 1 } },
          ],
        });
      },
      'POST /api/v1/conversations/c1/delivered': () => json(200, { lastDeliveredSequence: 3 }),
      'POST /api/v1/conversations/c1/read': () => json(200, { lastReadSequence: 3, unreadCount: 0 }),
    });
    render(
      <AuthProvider>
        <RealtimeProvider factory={() => hub.asHub()}>
          <ConversationView conversationId="c1" onChanged={() => undefined} />
        </RealtimeProvider>
      </AuthProvider>,
    );

    expect(await screen.findByText(/How are you\?/)).toBeInTheDocument();
    await waitFor(() => expect(hub.start).toHaveBeenCalled());
    const loadsBefore = historyLoads;
    hub.reconnect();

    expect(await screen.findByText('Missed while away')).toBeInTheDocument();
    expect(screen.getByText('Hello again, Ana')).toBeInTheDocument();
    expect(screen.queryByText(/How are you\?/)).not.toBeInTheDocument();
    expect(screen.queryByText('Another chat')).not.toBeInTheDocument();
    expect(asked).toEqual(['cur-1']);
    expect(historyLoads).toBe(loadsBefore);
  });

  it('sends a reply with a client message id and shows its receipt', async () => {
    const sent: Record<string, unknown>[] = [];
    let benRead = 0;
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/conversations/c1': () => json(200, detail(3, benRead)),
      'GET /api/v1/conversations/c1/history': () => json(200, history),
      'POST /api/v1/conversations/c1/messages': (init) => {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        sent.push(body);
        benRead = 0;
        return json(201, {
          id: 'm2', sequence: 3, kind: 'message', actor: ana, subject: null, text: null, body: body.body, createdAtUtc: new Date().toISOString(),
          version: 1, clientMessageId: body.clientMessageId, replyTo: { id: 'm1', sequence: 2, author: ben, excerpt: 'Hi Ana!', deleted: false },
        });
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <ConversationView conversationId="c1" onChanged={() => undefined} />
      </AuthProvider>,
    );

    expect(await screen.findByText(/Hi Ana!/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reply' }));
    expect(screen.getByText(/Replying to Ben/)).toBeInTheDocument();
    await user.type(screen.getByLabelText('Message'), 'Great, thanks!{Enter}');

    expect(await screen.findByText('Great, thanks!')).toBeInTheDocument();
    expect(sent).toHaveLength(1);
    expect(sent[0]).toMatchObject({ body: 'Great, thanks!', replyToId: 'm1' });
    expect(String(sent[0]!.clientMessageId)).toMatch(/.{8,}/);
    // Ben's device acknowledged position 3 but has not read it.
    expect(await screen.findByText(/Delivered/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
    expect(screen.getByLabelText('Message')).toHaveValue('');
  });

  it('uploads a chosen file, sends it with the message and shows received files', async () => {
    const sent: Record<string, unknown>[] = [];
    const uploads: string[] = [];
    const withFiles = {
      ...history,
      items: [
        ...history.items,
        {
          id: 'm3', sequence: 3, kind: 'message', actor: ben, subject: null, text: null, body: '', createdAtUtc: '2026-11-22T09:02:00Z', version: 1, clientMessageId: 'b-2',
          attachments: [
            { id: 'a-photo', fileName: 'beach.png', contentType: 'image/png', kind: 'image', size: 2048 },
            { id: 'a-doc', fileName: 'plan.pdf', contentType: 'application/pdf', kind: 'file', size: 1536 },
          ],
        },
      ],
    };
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/attachments/limits': () =>
        json(200, { maxFileSize: 1024 * 1024, maxPerMessage: 10, userQuota: 0, allowedExtensions: ['pdf', 'png', 'txt'], scanningEnabled: false }),
      'GET /api/v1/conversations/c1': () => json(200, detail(3, 3)),
      'GET /api/v1/conversations/c1/history': () => json(200, withFiles),
      'POST /api/v1/conversations/c1/attachments': (init) => {
        const file = (init?.body as FormData).get('file') as File;
        uploads.push(file.name);
        return json(201, { id: 'a-new', conversationId: 'c1', fileName: file.name, contentType: 'text/plain', kind: 'file', size: file.size, status: 'ready', messageId: null, createdAtUtc: new Date().toISOString() });
      },
      'POST /api/v1/conversations/c1/messages': (init) => {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        sent.push(body);
        return json(201, {
          id: 'm4', sequence: 4, kind: 'message', actor: ana, subject: null, text: null, body: body.body, createdAtUtc: new Date().toISOString(), version: 1,
          clientMessageId: body.clientMessageId, attachments: [{ id: 'a-new', fileName: 'notes.txt', contentType: 'text/plain', kind: 'file', size: 5 }],
        });
      },
    });
    // The picker's accept filter is bypassed, as a drag-and-drop or another browser could.
    const user = userEvent.setup({ applyAccept: false });
    render(
      <AuthProvider>
        <ConversationView conversationId="c1" onChanged={() => undefined} />
      </AuthProvider>,
    );

    // Received files: an inline image preview and a download card.
    expect(await screen.findByRole('img', { name: 'beach.png' })).toHaveAttribute('src', '/api/v1/attachments/a-photo/content');
    expect(screen.getByRole('link', { name: 'Download plan.pdf' })).toHaveAttribute('href', '/api/v1/attachments/a-doc/content?download=true');

    // A file type the installation does not allow is refused before uploading.
    await user.upload(screen.getByLabelText('Attach files'), new File(['MZ'], 'tool.exe'));
    expect(await screen.findByText(/tool\.exe: this type of file is not allowed/)).toBeInTheDocument();

    await user.upload(screen.getByLabelText('Attach files'), new File(['hello'], 'notes.txt', { type: 'text/plain' }));
    expect(await screen.findByText('5 B')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByRole('link', { name: 'Download notes.txt' })).toBeInTheDocument();
    expect(uploads).toEqual(['notes.txt']);
    expect(sent[0]).toMatchObject({ body: '', attachmentIds: ['a-new'] });
    expect(screen.queryByRole('button', { name: 'Remove notes.txt' })).not.toBeInTheDocument();
  });

  it('keeps a failed message with a retry that reuses the same client message id', async () => {
    const ids: unknown[] = [];
    let fail = true;
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/conversations/c1': () => json(200, detail(2, 2)),
      'GET /api/v1/conversations/c1/history': () => json(200, history),
      'POST /api/v1/conversations/c1/messages': (init) => {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        ids.push(body.clientMessageId);
        if (fail) return json(503, { code: 'service_unavailable' });
        return json(200, { id: 'm2', sequence: 3, kind: 'message', actor: ana, subject: null, text: null, body: body.body, createdAtUtc: new Date().toISOString(), version: 1, clientMessageId: body.clientMessageId });
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <ConversationView conversationId="c1" onChanged={() => undefined} />
      </AuthProvider>,
    );

    await screen.findByText(/Hi Ana!/);
    await user.type(screen.getByLabelText('Message'), 'Hello?');
    await user.click(screen.getByRole('button', { name: 'Send' }));
    expect(await screen.findByText('Not sent.')).toBeInTheDocument();

    fail = false;
    await user.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText(/Sent$/)).toBeInTheDocument();
    expect(screen.queryByText('Not sent.')).not.toBeInTheDocument();
    expect(ids).toHaveLength(2);
    expect(ids[0]).toBe(ids[1]);
  });

  it('suggests people after @, sends their ids and changes the notification level (Phase 17)', async () => {
    const sent: Record<string, unknown>[] = [];
    const settings: Record<string, unknown>[] = [];
    const carl = { id: 'u-carl', displayName: 'Carl Diaz', avatar: { initials: 'C' } };
    const group = { ...conversation, type: 'group', title: 'Team', counterpart: null, myRole: 'member' };
    const groupDetail = {
      conversation: group,
      description: null,
      participants: [
        { user: ana, role: 'member', joinedAtUtc: '2026-11-22T09:00:00Z', lastDeliveredSequence: 2, lastReadSequence: 2 },
        { user: ben, role: 'member', joinedAtUtc: '2026-11-22T09:00:00Z', lastDeliveredSequence: 2, lastReadSequence: 2 },
        { user: carl, role: 'member', joinedAtUtc: '2026-11-22T09:00:00Z', lastDeliveredSequence: 2, lastReadSequence: 2 },
      ],
    };
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/conversations/c1': () => json(200, groupDetail),
      'GET /api/v1/conversations/c1/history': () => json(200, history),
      'PUT /api/v1/conversations/c1/settings': (init) => {
        settings.push(JSON.parse(String(init?.body)) as Record<string, unknown>);
        return json(200, { ...group, notificationLevel: 'mentions', muted: true });
      },
      'POST /api/v1/conversations/c1/messages': (init) => {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        sent.push(body);
        return json(201, { id: 'm9', sequence: 3, kind: 'message', actor: ana, subject: null, text: null, body: body.body, createdAtUtc: new Date().toISOString(), version: 1, clientMessageId: body.clientMessageId });
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <ConversationView conversationId="c1" onChanged={() => undefined} />
      </AuthProvider>,
    );

    const box = await screen.findByLabelText('Message');
    await user.type(box, 'Hi @');
    // Small groups offer @everyone and @here as well as the people.
    expect(await screen.findByRole('option', { name: /@everyone/ })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /Ben/ })).toBeInTheDocument();
    await user.type(box, 'ca');
    const option = await screen.findByRole('option', { name: /Carl Diaz/ });
    expect(option).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByRole('option', { name: /@everyone/ })).not.toBeInTheDocument();
    await user.keyboard('{Enter}');
    expect(box).toHaveValue('Hi @Carl Diaz ');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    await user.type(box, 'see this{Enter}');
    expect(sent[0]).toMatchObject({ body: 'Hi @Carl Diaz see this', mentionUserIds: ['u-carl'] });

    await user.selectOptions(screen.getByLabelText('Notifications'), 'mentions');
    expect(settings).toEqual([{ notificationLevel: 'mentions' }]);
  });

  it('saves, pins and forwards a message, and opens at a linked message (Phase 18)', async () => {
    const calls: string[] = [];
    let pinned = false;
    const pinnedEntry = { ...history.items[1], pinned: true };
    window.history.replaceState(null, '', '/conversations?id=c1&message=m1');
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/conversations/c1': () => json(200, detail(3, 3)),
      'GET /api/v1/conversations/c1/history': () =>
        json(200, { ...history, items: [history.items[0], pinned ? pinnedEntry : history.items[1]] }),
      'GET /api/v1/conversations/c1/pins': () =>
        json(200, pinned ? [{ entry: pinnedEntry, pinnedBy: ana, pinnedAtUtc: '2026-11-23T10:00:00Z' }] : []),
      'PUT /api/v1/conversations/c1/messages/m1/saved': (init) => {
        calls.push(`saved ${String(init?.body)}`);
        return new Response(null, { status: 204 });
      },
      'PUT /api/v1/conversations/c1/messages/m1/pin': (init) => {
        calls.push(`pin ${String(init?.body)}`);
        pinned = true;
        return new Response(null, { status: 204 });
      },
      'GET /api/v1/conversations': () => json(200, [conversation, { ...conversation, id: 'c9', title: 'Trip planning', type: 'group', counterpart: null }]),
      'POST /api/v1/conversations/c1/messages/m1/forward': (init) => {
        calls.push(`forward ${String(init?.body)}`);
        return json(201, { ...history.items[1], id: 'm50', forwardedFrom: ben });
      },
    });
    const user = userEvent.setup();
    const { container } = render(
      <AuthProvider>
        <ConversationView conversationId="c1" onChanged={() => undefined} />
      </AuthProvider>,
    );

    // Opened at the linked message: it is highlighted.
    expect(await screen.findByText(/Hi Ana!/)).toBeInTheDocument();
    await waitFor(() => expect(container.querySelector('#msg-m1')).toHaveClass('is-highlighted'));

    await user.click(screen.getByRole('button', { name: 'More' }));
    await user.click(screen.getByRole('menuitem', { name: 'Save' }));
    expect(await screen.findByText('Message saved.')).toBeInTheDocument();
    expect(calls).toContain('saved {"saved":true}');

    await user.click(screen.getByRole('button', { name: 'More' }));
    await user.click(screen.getByRole('menuitem', { name: 'Pin' }));
    expect(await screen.findByRole('button', { name: '1 pinned' })).toBeInTheDocument();
    expect(calls).toContain('pin {"pinned":true}');
    await user.click(screen.getByRole('button', { name: '1 pinned' }));
    expect(screen.getByText(/Pinned by Ana/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'More' }));
    await user.click(screen.getByRole('menuitem', { name: 'Forward' }));
    const dialog = await screen.findByRole('dialog', { name: 'Forward message' });
    await user.click(within(dialog).getByLabelText('Trip planning'));
    await user.type(within(dialog).getByLabelText('Add a comment (optional)'), 'FYI');
    await user.click(within(dialog).getByRole('button', { name: 'Forward' }));
    expect(await screen.findByText('Message forwarded.')).toBeInTheDocument();
    const forward = JSON.parse(calls.find((c) => c.startsWith('forward '))!.slice(8)) as Record<string, unknown>;
    expect(forward).toMatchObject({ targetConversationId: 'c9', comment: 'FYI' });
    expect(String(forward.clientMessageId)).toMatch(/.{8,}/);
    window.history.replaceState(null, '', '/');
  });

  it('formats with shortcuts and the toolbar, and shows formatted messages (Phase 19)', async () => {
    const sent: Record<string, unknown>[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/conversations/c1': () => json(200, detail(3, 3)),
      'GET /api/v1/conversations/c1/history': () => json(200, history),
      'POST /api/v1/conversations/c1/messages': (init) => {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        sent.push(body);
        return json(201, { id: 'm8', sequence: 3, kind: 'message', actor: ana, subject: null, text: null, body: body.body, createdAtUtc: new Date().toISOString(), version: 1, clientMessageId: body.clientMessageId });
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <ConversationView conversationId="c1" onChanged={() => undefined} />
      </AuthProvider>,
    );

    const box = (await screen.findByLabelText('Message')) as HTMLTextAreaElement;
    await user.type(box, 'ship it');
    box.setSelectionRange(5, 7);
    await user.keyboard('{Control>}b{/Control}');
    expect(box).toHaveValue('ship **it**');
    box.setSelectionRange(0, 4);
    await user.click(screen.getByRole('button', { name: 'Strikethrough' }));
    expect(box).toHaveValue('~~ship~~ **it**');
    await user.click(box);
    await user.keyboard('{End}{Enter}');
    expect(sent[0]).toMatchObject({ body: '~~ship~~ **it**' });
    const bubble = (await screen.findByText('it', { selector: 'strong' })).closest('.cx-message-body')!;
    expect(bubble.querySelector('del')?.textContent).toBe('ship');
  });

  it('reacts with an emoji and replies in a thread (Phase 16)', async () => {
    const reactions: Record<string, unknown>[] = [];
    const sent: Record<string, unknown>[] = [];
    const withThread = {
      ...history,
      items: [
        history.items[0],
        { ...history.items[1], reactions: [{ emoji: '🎉', count: 1, userIds: ['u-ben'] }], thread: { replyCount: 1, lastReplyAtUtc: '2026-11-22T09:03:00Z', following: true, unread: 1 } },
      ],
    };
    const reply = { id: 'r1', sequence: 3, kind: 'message', actor: ben, subject: null, text: null, body: 'In the thread', createdAtUtc: '2026-11-22T09:03:00Z', version: 1, threadRootId: 'm1', shownInConversation: false };
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/conversations/c1': () => json(200, detail(3, 3)),
      'GET /api/v1/conversations/c1/history': () => json(200, withThread),
      'PUT /api/v1/conversations/c1/messages/m1/reactions': (init) => {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        reactions.push(body);
        return json(200, [
          { emoji: '🎉', count: 1, userIds: ['u-ben'] },
          { emoji: '👍', count: 1, userIds: ['u-ana'] },
        ]);
      },
      'GET /api/v1/conversations/c1/messages/m1/thread': () =>
        json(200, { root: withThread.items[1], items: [reply], nextCursor: null, hasMore: false, following: true, lastReadSequence: 2 }),
      'POST /api/v1/conversations/c1/messages/m1/thread/read': () => json(200, { unread: 0 }),
      'POST /api/v1/conversations/c1/messages': (init) => {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        sent.push(body);
        return json(201, { ...reply, id: 'r2', sequence: 4, actor: ana, body: body.body, clientMessageId: body.clientMessageId, shownInConversation: true });
      },
    });
    const user = userEvent.setup();
    render(
      <AuthProvider>
        <ConversationView conversationId="c1" onChanged={() => undefined} />
      </AuthProvider>,
    );

    expect(await screen.findByText(/Hi Ana!/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /🎉: 1 person — Ben/ })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'React' }));
    await user.click(screen.getByRole('button', { name: 'React with 👍' }));
    expect(reactions).toEqual([{ emoji: '👍', reacted: true }]);
    expect(await screen.findByRole('button', { name: /👍: 1 person — You/ })).toHaveAttribute('aria-pressed', 'true');

    // The thread summary opens the side panel; reading it clears the badge.
    await user.click(screen.getByRole('button', { name: /1 reply/ }));
    const panel = await screen.findByRole('complementary', { name: 'Thread' });
    expect(await screen.findByText('In the thread')).toBeInTheDocument();
    expect(panel).toContainElement(screen.getByText('In the thread'));
    await user.click(screen.getByLabelText('Also send to the conversation'));
    await user.type(screen.getByLabelText('Reply…'), 'Count me in{Enter}');
    expect(sent[0]).toMatchObject({ body: 'Count me in', threadRootId: 'm1', alsoInConversation: true });
    expect(await screen.findByText('Count me in')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Close thread' }));
    expect(screen.queryByRole('complementary', { name: 'Thread' })).not.toBeInTheDocument();
  });

  it('starts a private chat from the conversation menu, asking with whom in a group', async () => {
    const cara = { id: 'u-cara', displayName: 'Cara', avatar: { initials: 'C' } };
    const group = {
      conversation: { ...conversation, id: 'c2', type: 'group', title: 'Trip planning', counterpart: null },
      description: null,
      participants: [ana, ben, cara].map((user) => ({ user, role: 'member', joinedAtUtc: '2026-11-22T09:00:00Z', lastDeliveredSequence: 2, lastReadSequence: 2 })),
    };
    const invited: unknown[] = [];
    mockFetch({
      'GET /api/v1/auth/config': () => json(200, authConfig),
      'GET /api/v1/users/me': () => json(200, me),
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      'GET /api/v1/conversations/c1': () => json(200, detail(2, 2)),
      'GET /api/v1/conversations/c1/history': () => json(200, history),
      'GET /api/v1/conversations/c2': () => json(200, group),
      'GET /api/v1/conversations/c2/history': () => json(200, history),
      'POST /api/v1/private-chats': (init) => {
        invited.push(JSON.parse(String(init?.body)));
        return json(201, { id: `p${invited.length}` });
      },
    });
    const user = userEvent.setup();

    // Direct: the one other person is invited straight away.
    const { unmount } = render(
      <AuthProvider>
        <ConversationView conversationId="c1" onChanged={() => undefined} />
      </AuthProvider>,
    );
    expect(await screen.findByText(/How are you\?/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'More actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Private chat' }));
    await waitFor(() => expect(window.location.search).toBe('?id=p1'));
    expect(window.location.pathname).toBe('/private');
    expect(invited).toEqual([{ userId: 'u-ben' }]);
    unmount();

    // Group: the menu lists the other members (not me), and the chosen one is invited.
    render(
      <AuthProvider>
        <ConversationView conversationId="c2" onChanged={() => undefined} />
      </AuthProvider>,
    );
    expect(await screen.findByRole('heading', { name: /Trip planning/ })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'More actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Private chat' }));
    const people = screen.getByRole('group', { name: 'Private chat with' });
    expect(within(people).getAllByRole('menuitem').map((b) => b.textContent)).toEqual(['BBen', 'CCara']);
    await user.click(within(people).getByRole('menuitem', { name: /Cara/ }));
    await waitFor(() => expect(window.location.search).toBe('?id=p2'));
    expect(invited).toEqual([{ userId: 'u-ben' }, { userId: 'u-cara' }]);
    window.history.replaceState(null, '', '/');
  });
});
