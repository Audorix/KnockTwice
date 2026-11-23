import { useSyncExternalStore, type AnchorHTMLAttributes, type MouseEvent } from 'react';

/**
 * Minimal history-based router for the handful of Phase 2 routes. Avoids a routing dependency
 * until the chat application (Phase 8) needs nested routes and data loading.
 */

const listeners = new Set<() => void>();

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  window.addEventListener('popstate', listener);
  return () => {
    listeners.delete(listener);
    window.removeEventListener('popstate', listener);
  };
}

const snapshot = () => window.location.pathname + window.location.search;

export function navigate(to: string, options: { replace?: boolean } = {}): void {
  if (options.replace) window.history.replaceState(null, '', to);
  else window.history.pushState(null, '', to);
  listeners.forEach((listener) => listener());
}

export interface RouteLocation {
  pathname: string;
  query: URLSearchParams;
}

export function useLocation(): RouteLocation {
  const current = useSyncExternalStore(subscribe, snapshot, snapshot);
  const url = new URL(current, window.location.origin);
  return { pathname: url.pathname, query: url.searchParams };
}

type LinkProps = AnchorHTMLAttributes<HTMLAnchorElement> & { to: string };

/** An ordinary anchor (keyboard, middle-click and "open in new tab" keep working) with client-side navigation. */
export function Link({ to, onClick, ...props }: LinkProps) {
  const handleClick = (event: MouseEvent<HTMLAnchorElement>) => {
    onClick?.(event);
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    navigate(to);
  };
  return <a href={to} onClick={handleClick} {...props} />;
}
