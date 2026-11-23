/**
 * The frame's side of the loader protocol (public/widget.js). Messages are accepted only from the
 * parent window and only from the origin that embeds us; everything sent goes to that origin only.
 * The browser has already checked the origin against the widget's frame-ancestors policy.
 */

export type HostCommand =
  | { type: 'init'; identityToken: string | null; open: boolean }
  | { type: 'identify'; identityToken: string }
  | { type: 'logout' }
  | { type: 'open' }
  | { type: 'close' };

export type FrameEvent =
  | { type: 'ready' }
  | { type: 'state'; open: boolean; position: 'left' | 'right'; hidden: boolean }
  | { type: 'unread'; count: number };

/**
 * The embedding page's origin. Chromium and WebKit report it (ancestorOrigins); elsewhere the
 * loader's `parent` parameter is used. The server checks it against the widget's origins either way.
 */
export function resolveParentOrigin(search: URLSearchParams, location: Location = window.location): string | null {
  const ancestor = location.ancestorOrigins?.length ? location.ancestorOrigins[0] : undefined;
  const claimed = search.get('parent');
  const origin = ancestor ?? claimed;
  if (!origin) return null;
  try {
    const url = new URL(origin);
    return url.protocol === 'https:' || url.protocol === 'http:' ? url.origin : null;
  } catch {
    return null;
  }
}

export function isHostCommand(value: unknown): value is HostCommand {
  if (!value || typeof value !== 'object') return false;
  const data = value as { source?: unknown; type?: unknown; identityToken?: unknown };
  if (data.source !== 'chatx-host') return false;
  switch (data.type) {
    case 'init':
      return data.identityToken === null || typeof data.identityToken === 'string';
    case 'identify':
      return typeof data.identityToken === 'string';
    case 'logout':
    case 'open':
    case 'close':
      return true;
    default:
      return false;
  }
}

export class HostBridge {
  private readonly listeners = new Set<(command: HostCommand) => void>();
  private readonly handler = (event: MessageEvent) => {
    if (event.source !== this.parent || event.origin !== this.parentOrigin || !isHostCommand(event.data)) return;
    const command = event.data;
    this.listeners.forEach((listener) => listener(command));
  };

  constructor(
    private readonly parentOrigin: string,
    private readonly parent: Window = window.parent,
    private readonly self: Window = window,
  ) {
    self.addEventListener('message', this.handler);
  }

  /** True when the page runs inside a frame at all (opened directly, it has no host). */
  get embedded(): boolean {
    return this.parent !== this.self;
  }

  onCommand(listener: (command: HostCommand) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  send(event: FrameEvent): void {
    if (this.embedded) this.parent.postMessage({ source: 'chatx', ...event }, this.parentOrigin);
  }

  dispose(): void {
    this.self.removeEventListener('message', this.handler);
    this.listeners.clear();
  }
}
