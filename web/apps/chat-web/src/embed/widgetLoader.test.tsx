import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, render } from '@testing-library/react';
import loaderSource from '../../public/widget.js?raw';
import { ChatXWidget, loadChatXWidget, normalizeBaseUrl, type ChatXWidgetApi } from '../../../../packages/chat-embed/src/index';

const widgetId = '01a205e7-68c1-76f1-bcaf-e17aba6f1c76';
const base = 'https://chat.example.org';

type Win = Window & { ChatXWidget?: unknown };

function runLoader(dataset: Record<string, string> = { widgetId }) {
  const script = document.createElement('script');
  script.src = `${base}/widget.js`;
  Object.assign(script.dataset, dataset);
  Object.defineProperty(document, 'currentScript', { configurable: true, get: () => script });
  window.eval(loaderSource);
  Object.defineProperty(document, 'currentScript', { configurable: true, get: () => null });
  return (window as Win).ChatXWidget as ChatXWidgetApi;
}

const frameOf = () => document.querySelector<HTMLIFrameElement>('iframe');
const fromFrame = (data: unknown, origin = base, source: Window | null = frameOf()!.contentWindow) =>
  window.dispatchEvent(new MessageEvent('message', { data, origin, source }));

describe('widget.js loader', () => {
  afterEach(() => {
    ((window as Win).ChatXWidget as ChatXWidgetApi | undefined)?.destroy?.();
    delete (window as Win).ChatXWidget;
  });

  it('frames the embed page and resizes only on messages from that frame', () => {
    const api = runLoader();
    const frame = frameOf()!;
    const url = new URL(frame.src);
    expect(url.origin + url.pathname).toBe(`${base}/embed.html`);
    expect(Object.fromEntries(url.searchParams)).toEqual({ widget: widgetId, parent: window.location.origin });
    expect(frame.getAttribute('sandbox')).toContain('allow-scripts');
    expect(frame.style.display).toBe('none');

    const post = vi.spyOn(frame.contentWindow!, 'postMessage').mockImplementation(() => undefined);
    api.open(); // queued until the frame is ready
    expect(post).not.toHaveBeenCalled();

    // Lookalikes are ignored: another origin, or another window.
    fromFrame({ source: 'chatx', type: 'state', open: true, position: 'left', hidden: false }, 'https://evil.example');
    fromFrame({ source: 'chatx', type: 'state', open: true, position: 'left', hidden: false }, base, window);
    expect(frame.style.display).toBe('none');

    const onOpen = vi.fn();
    api.on('open', onOpen);
    fromFrame({ source: 'chatx', type: 'ready' });
    expect(post).toHaveBeenNthCalledWith(1, { source: 'chatx-host', type: 'init', identityToken: null, open: false }, base);
    expect(post).toHaveBeenNthCalledWith(2, { source: 'chatx-host', type: 'open' }, base);

    fromFrame({ source: 'chatx', type: 'state', open: true, position: 'left', hidden: false });
    expect(frame.style.display).toBe('block');
    expect(frame.style.left).toBe('16px');
    expect(frame.style.width).toContain('400px');
    expect(onOpen).toHaveBeenCalledTimes(1);

    const onUnread = vi.fn();
    api.on('unread', onUnread);
    fromFrame({ source: 'chatx', type: 'unread', count: 2 });
    expect(onUnread).toHaveBeenCalledWith(2);

    api.identify('signed.token.value');
    expect(post).toHaveBeenLastCalledWith({ source: 'chatx-host', type: 'identify', identityToken: 'signed.token.value' }, base);
    api.destroy();
    expect(frameOf()).toBeNull();
  });

  it('opens full screen on phones', () => {
    const media = { matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() };
    vi.stubGlobal('matchMedia', vi.fn(() => media));
    runLoader();
    const frame = frameOf()!;
    expect(frame.style.width).toBe('72px');
    fromFrame({ source: 'chatx', type: 'state', open: true, position: 'right', hidden: false });
    expect([frame.style.top, frame.style.right, frame.style.bottom, frame.style.width, frame.style.borderRadius]).toEqual(['0px', '0px', '0px', '100vw', '0px']);
    fromFrame({ source: 'chatx', type: 'state', open: false, position: 'right', hidden: false });
    expect([frame.style.width, frame.style.bottom]).toEqual(['72px', '16px']);
    vi.unstubAllGlobals();
  });

  it('refuses to start without a valid widget id', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    runLoader({ widgetId: 'not-a-guid' });
    expect(frameOf()).toBeNull();
    expect(warn).toHaveBeenCalled();
  });
});

describe('@audorix/chatx-embed', () => {
  afterEach(() => {
    document.head.querySelectorAll('script[data-chatx-loader]').forEach((s) => s.remove());
    delete (window as Win).ChatXWidget;
  });

  const fakeApi = () => {
    const api = {
      version: 1,
      init: vi.fn(() => api),
      identify: vi.fn(() => api),
      logout: vi.fn(() => api),
      open: vi.fn(() => api),
      close: vi.fn(() => api),
      toggle: vi.fn(() => api),
      destroy: vi.fn(() => api),
      on: vi.fn(() => api),
    };
    return api;
  };

  it('loads the installation loader once and drives it from props', async () => {
    expect(normalizeBaseUrl('https://chat.example.org/some/path')).toBe(base);
    expect(() => normalizeBaseUrl('javascript:alert(1)')).toThrow();

    const { rerender, unmount } = render(<ChatXWidget baseUrl={base} widgetId={widgetId} identityToken="tok-a" />);
    const again = loadChatXWidget(base);
    const scripts = document.head.querySelectorAll<HTMLScriptElement>('script[data-chatx-loader]');
    expect(scripts).toHaveLength(1);
    expect(scripts[0]!.src).toBe(`${base}/widget.js`);

    const api = fakeApi();
    await act(async () => {
      (window as Win).ChatXWidget = api;
      scripts[0]!.dispatchEvent(new Event('load'));
      await again;
    });
    expect(api.init).toHaveBeenCalledWith({ widgetId, identityToken: 'tok-a' });

    rerender(<ChatXWidget baseUrl={base} widgetId={widgetId} identityToken="tok-b" />);
    expect(api.identify).toHaveBeenCalledWith('tok-b');
    rerender(<ChatXWidget baseUrl={base} widgetId={widgetId} identityToken={null} />);
    expect(api.logout).toHaveBeenCalledTimes(1);

    unmount();
    expect(api.destroy).toHaveBeenCalled();
  });
});
