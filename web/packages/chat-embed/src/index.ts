import { createElement, useEffect, useRef, type ReactElement } from 'react';

/**
 * @audorix/chatx-embed: React integration for the Audorix.ChatX website chat.
 *
 * The chat itself runs in an iframe served by your Audorix.ChatX installation (ADR-024); this
 * package only loads that installation's `widget.js` loader and drives it. Nothing from the chat
 * runs in your page's JavaScript context, and your page never sees the visitor's session token.
 */

export interface ChatXWidgetApi {
  version: number;
  init(options: ChatXWidgetOptions): ChatXWidgetApi;
  identify(identityToken: string): ChatXWidgetApi;
  logout(): ChatXWidgetApi;
  open(): ChatXWidgetApi;
  close(): ChatXWidgetApi;
  toggle(): ChatXWidgetApi;
  destroy(): ChatXWidgetApi;
  on(event: 'ready' | 'open' | 'close', callback: () => void): ChatXWidgetApi;
  on(event: 'unread', callback: (count: number) => void): ChatXWidgetApi;
}

export interface ChatXWidgetOptions {
  widgetId: string;
  /** A JWT your server signed with the widget's secret (HS256; sub, name, email, aud, iat, exp). */
  identityToken?: string | null;
  /** "en" or "es"; by default the widget's setting or the visitor's browser. */
  language?: string;
  /** Open the chat panel right away. */
  open?: boolean;
}

declare global {
  interface Window {
    ChatXWidget?: ChatXWidgetApi | unknown[];
  }
}

const loads = new Map<string, Promise<ChatXWidgetApi>>();

function isApi(value: unknown): value is ChatXWidgetApi {
  return !!value && typeof value === 'object' && typeof (value as ChatXWidgetApi).init === 'function';
}

/** The installation's origin, e.g. "https://chat.example.org" (paths are ignored). */
export function normalizeBaseUrl(baseUrl: string): string {
  const url = new URL(baseUrl);
  if (url.protocol !== 'https:' && url.protocol !== 'http:') throw new Error('ChatX: baseUrl must be an http(s) URL.');
  return url.origin;
}

/**
 * Loads `widget.js` from the installation once per page and resolves with its API. Safe to call
 * repeatedly; concurrent callers share one script element.
 */
export function loadChatXWidget(baseUrl: string, doc: Document = document): Promise<ChatXWidgetApi> {
  const origin = normalizeBaseUrl(baseUrl);
  const win = doc.defaultView ?? window;
  if (isApi(win.ChatXWidget)) return Promise.resolve(win.ChatXWidget);
  const existing = loads.get(origin);
  if (existing) return existing;

  const promise = new Promise<ChatXWidgetApi>((resolve, reject) => {
    const script = doc.createElement('script');
    script.src = `${origin}/widget.js`;
    script.async = true;
    script.dataset.chatxLoader = 'true';
    script.onload = () => (isApi(win.ChatXWidget) ? resolve(win.ChatXWidget) : reject(new Error('ChatX: widget.js loaded but did not start.')));
    script.onerror = () => {
      loads.delete(origin);
      script.remove();
      reject(new Error(`ChatX: could not load ${script.src}.`));
    };
    doc.head.appendChild(script);
  });
  loads.set(origin, promise);
  return promise;
}

export interface ChatXWidgetProps extends ChatXWidgetOptions {
  /** Your Audorix.ChatX installation, e.g. "https://chat.example.org". */
  baseUrl: string;
  onReady?: () => void;
  onUnreadChange?: (count: number) => void;
  onOpenChange?: (open: boolean) => void;
  /** Called when the loader cannot be fetched (the chat then simply does not appear). */
  onError?: (error: Error) => void;
}

/**
 * Renders nothing in your tree: the chat floats in its own frame. Changing `identityToken`
 * identifies the visitor; changing it from a token to null ends the visitor session (sign-out).
 *
 * ```tsx
 * <ChatXWidget baseUrl="https://chat.example.org" widgetId="…" identityToken={tokenFromYourServer} />
 * ```
 */
export function ChatXWidget(props: ChatXWidgetProps): ReactElement | null {
  const { baseUrl, widgetId, identityToken, language, open } = props;
  const api = useRef<ChatXWidgetApi | null>(null);
  const callbacks = useRef(props);
  const lastToken = useRef<string | null | undefined>(identityToken);
  callbacks.current = props;

  useEffect(() => {
    let cancelled = false;
    loadChatXWidget(baseUrl).then(
      (widget) => {
        if (cancelled) return;
        api.current = widget;
        widget
          .on('ready', () => callbacks.current.onReady?.())
          .on('unread', (count: number) => callbacks.current.onUnreadChange?.(count))
          .on('open', () => callbacks.current.onOpenChange?.(true))
          .on('close', () => callbacks.current.onOpenChange?.(false));
        widget.init({
          widgetId,
          ...(lastToken.current ? { identityToken: lastToken.current } : {}),
          ...(language ? { language } : {}),
          ...(open ? { open } : {}),
        });
      },
      (error: Error) => !cancelled && callbacks.current.onError?.(error),
    );
    return () => {
      cancelled = true;
      api.current?.destroy();
      api.current = null;
    };
    // The frame is recreated only when the installation, widget or language changes.
  }, [baseUrl, widgetId, language]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const previous = lastToken.current;
    lastToken.current = identityToken;
    if (!api.current || previous === identityToken) return;
    if (identityToken) api.current.identify(identityToken);
    else if (previous) api.current.logout();
  }, [identityToken]);

  useEffect(() => {
    if (!api.current || open === undefined) return;
    if (open) api.current.open();
    else api.current.close();
  }, [open]);

  return createElement('span', { hidden: true, 'data-chatx-widget': widgetId });
}

export default ChatXWidget;
