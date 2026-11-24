import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../i18n/i18n';
import { header, json, mockFetch } from '../test/fetchRoutes';
import { EmbedApp, textOn, tokenSubject } from './EmbedApp';
import { HostBridge, isHostCommand, resolveParentOrigin, type HostCommand } from './hostBridge';
import type { VisitorRealtime } from './visitorRealtime';

const widgetId = '01a205e7-68c1-76f1-bcaf-e17aba6f1c76';
const shop = 'https://shop.example.org';
const config = {
  id: widgetId, title: 'Chat with Acme', welcomeMessage: 'Hi! How can we help?', accentColor: '#0b6e66', position: 'right',
  defaultLanguage: 'auto', allowAnonymous: true, maxFileSize: 26214400,
};
const ben = { id: 'u-ben', displayName: 'Ben', avatar: { initials: 'B' } };
const entry = (sequence: number, actorId: string, body: string) => ({
  id: `e-${sequence}`, sequence, kind: 'message', actor: actorId === 'v-user' ? { id: 'v-user', displayName: 'Visitor 1A2B', avatar: { initials: 'V' } } : ben,
  subject: null, text: null, createdAtUtc: '2026-11-23T10:00:00Z', body, version: 1,
});

function fakeBridge() {
  const listeners = new Set<(command: HostCommand) => void>();
  const send = vi.fn();
  return {
    bridge: { onCommand: (l: (c: HostCommand) => void) => (listeners.add(l), () => listeners.delete(l)), send } as unknown as HostBridge,
    send,
    command: (c: HostCommand) => listeners.forEach((l) => l(c)),
  };
}

const fakeRealtime = () => ({ start: vi.fn(async () => {}), stop: vi.fn(async () => {}), join: vi.fn(async () => {}), typing: vi.fn(async () => {}) }) as unknown as VisitorRealtime;

describe('EmbedApp', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    await i18n.changeLanguage('en');
  });

  it('starts an anonymous session only when the visitor opens the chat, then sends messages', async () => {
    let thread = { conversationId: null as string | null, visitorUserId: 'v-user', items: [] as unknown[], nextCursor: null, hasMore: false, responders: [] as unknown[], lastReadSequence: 0 };
    const sessionBodies: unknown[] = [];
    const fetchMock = mockFetch({
      [`GET /api/v1/widget/${widgetId}/config`]: () => json(200, config),
      [`POST /api/v1/widget/${widgetId}/sessions`]: (init) => {
        sessionBodies.push(JSON.parse(String(init?.body)));
        return json(200, { token: 'tok-1', expiresAtUtc: '2099-01-01T00:00:00Z', visitorId: 'v-1', displayName: 'Visitor 1A2B', identified: false });
      },
      'GET /api/v1/widget/visitor/conversation': (init) => {
        expect(header(init, 'Authorization')).toBe('Bearer tok-1');
        return json(200, thread);
      },
      'POST /api/v1/widget/visitor/messages': (init) => {
        const body = JSON.parse(String(init?.body)) as { body: string; clientMessageId: string };
        expect(body.clientMessageId).toBeTruthy();
        const sent = entry(1, 'v-user', body.body);
        thread = { ...thread, conversationId: 'c-1', items: [sent], responders: [ben] };
        return json(201, sent);
      },
    });
    const { bridge, send } = fakeBridge();
    const user = userEvent.setup();
    render(<EmbedApp widgetId={widgetId} parentOrigin={shop} language="en" bridge={bridge} realtimeFactory={fakeRealtime} />);

    const launcher = await screen.findByRole('button', { name: 'Open chat' });
    expect(send).toHaveBeenCalledWith({ type: 'ready' });
    // Sent from an effect after the launcher renders: wait for it rather than assume it already ran.
    await waitFor(() => expect(send).toHaveBeenCalledWith({ type: 'state', open: false, position: 'right', hidden: false }));
    // A page view alone creates no session.
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/sessions'))).toBe(false);

    await user.click(launcher);
    expect(await screen.findByRole('heading', { name: 'Chat with Acme' })).toBeInTheDocument();
    expect(screen.getByText('Hi! How can we help?')).toBeInTheDocument();
    await waitFor(() => expect(sessionBodies).toEqual([{ parentOrigin: shop, language: 'en' }]));
    await waitFor(() => expect(send).toHaveBeenCalledWith({ type: 'state', open: true, position: 'right', hidden: false }));
    expect(screen.queryByLabelText('Attach a file')).not.toBeInTheDocument();
    expect(screen.getByText(/Messages are kept/)).toBeInTheDocument();

    await user.type(screen.getByLabelText('Write a message'), 'Is this in stock?{Enter}');
    expect(await screen.findByText('Is this in stock?')).toBeInTheDocument();
    expect(await screen.findByText('You are talking to Ben')).toBeInTheDocument();
    expect(JSON.parse(window.localStorage.getItem(`chatx.widget.${widgetId}`)!)).toMatchObject({ token: 'tok-1', identified: false });

    await user.click(screen.getByRole('button', { name: 'Close chat' }));
    expect(await screen.findByRole('button', { name: 'Open chat' })).toBeInTheDocument();
  });

  it('identifies visitors the host vouches for and reports unread replies to the host', async () => {
    const sessionBodies: Record<string, unknown>[] = [];
    mockFetch({
      [`GET /api/v1/widget/${widgetId}/config`]: () => json(200, { ...config, position: 'left' }),
      [`POST /api/v1/widget/${widgetId}/sessions`]: (init) => {
        sessionBodies.push(JSON.parse(String(init?.body)) as Record<string, unknown>);
        return json(200, { token: 'tok-2', expiresAtUtc: '2099-01-01T00:00:00Z', visitorId: 'v-2', displayName: 'Dora', identified: true });
      },
      'GET /api/v1/widget/visitor/conversation': () =>
        json(200, { conversationId: 'c-2', visitorUserId: 'v-user', items: [entry(1, 'v-user', 'Hello'), entry(2, 'u-ben', 'Hi Dora')], nextCursor: null, hasMore: false, responders: [ben], lastReadSequence: 1 }),
      'POST /api/v1/widget/visitor/read': () => json(200, { lastReadSequence: 2, unreadCount: 0 }),
    });
    const { bridge, send, command } = fakeBridge();
    const user = userEvent.setup();
    render(<EmbedApp widgetId={widgetId} parentOrigin={shop} language="en" bridge={bridge} realtimeFactory={fakeRealtime} />);
    await screen.findByRole('button', { name: 'Open chat' });

    const token = `h.${btoa(JSON.stringify({ sub: 'customer-7' })).replace(/=+$/, '')}.s`;
    command({ type: 'init', identityToken: token, open: false });
    await waitFor(() => expect(sessionBodies).toEqual([{ parentOrigin: shop, language: 'en', identityToken: token }]));
    expect(await screen.findByRole('button', { name: 'Open chat, 1 unread message' })).toBeInTheDocument();
    await waitFor(() => expect(send).toHaveBeenCalledWith({ type: 'unread', count: 1 }));
    expect(send).toHaveBeenCalledWith({ type: 'state', open: false, position: 'left', hidden: false });

    // The same person again keeps the session.
    command({ type: 'identify', identityToken: token });
    await user.click(screen.getByRole('button', { name: /Open chat/ }));
    expect(await screen.findByText('Hi Dora')).toBeInTheDocument();
    expect(screen.getByLabelText('Attach a file')).toBeInTheDocument();
    expect(screen.getByText('Signed in as Dora. Messages are kept so the team can answer you.')).toBeInTheDocument();
    await waitFor(() => expect(send).toHaveBeenCalledWith({ type: 'unread', count: 0 }));
    expect(sessionBodies).toHaveLength(1);
  });

  it('stays hidden when the widget is unavailable', async () => {
    mockFetch({ [`GET /api/v1/widget/${widgetId}/config`]: () => json(404, { code: 'widget_not_found' }) });
    const { bridge, send } = fakeBridge();
    const { container } = render(<EmbedApp widgetId={widgetId} parentOrigin={shop} language="en" bridge={bridge} realtimeFactory={fakeRealtime} />);
    await waitFor(() => expect(send).toHaveBeenCalledWith({ type: 'state', open: false, position: 'right', hidden: true }));
    expect(container).toBeEmptyDOMElement();
  });
});

describe('host bridge', () => {
  it('accepts commands only from the parent window and the embedding origin', () => {
    const frame = document.createElement('iframe');
    document.body.appendChild(frame);
    const parent = frame.contentWindow!;
    const post = vi.spyOn(parent, 'postMessage').mockImplementation(() => undefined);
    const bridge = new HostBridge(shop, parent, window);
    const received: HostCommand[] = [];
    bridge.onCommand((c) => received.push(c));

    const dispatch = (data: unknown, origin: string, source: Window | null) => window.dispatchEvent(new MessageEvent('message', { data, origin, source }));
    dispatch({ source: 'chatx-host', type: 'open' }, 'https://evil.example', parent);
    dispatch({ source: 'chatx-host', type: 'open' }, shop, window);
    dispatch({ source: 'other', type: 'open' }, shop, parent);
    dispatch({ source: 'chatx-host', type: 'identify', identityToken: 42 }, shop, parent);
    dispatch({ source: 'chatx-host', type: 'open' }, shop, parent);
    expect(received).toEqual([{ source: 'chatx-host', type: 'open' }]);

    bridge.send({ type: 'unread', count: 3 });
    expect(post).toHaveBeenCalledWith({ source: 'chatx', type: 'unread', count: 3 }, shop);
    bridge.dispose();
    frame.remove();
  });

  it('validates commands and the parent origin', () => {
    expect(isHostCommand({ source: 'chatx-host', type: 'init', identityToken: null, open: true })).toBe(true);
    expect(isHostCommand({ source: 'chatx-host', type: 'navigate' })).toBe(false);
    expect(resolveParentOrigin(new URLSearchParams('parent=https://shop.example.org/some/page'))).toBe(shop);
    expect(resolveParentOrigin(new URLSearchParams('parent=javascript:alert(1)'))).toBeNull();
    expect(resolveParentOrigin(new URLSearchParams(''))).toBeNull();
  });

  it('reads token subjects and picks readable text colours', () => {
    expect(tokenSubject(`a.${btoa('{"sub":"c-1"}')}.b`)).toBe('c-1');
    expect(tokenSubject('garbage')).toBeNull();
    expect(textOn('#0b6e66')).toBe('#ffffff');
    expect(textOn('#ffd34d')).toBe('#111827');
  });
});
