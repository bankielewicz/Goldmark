import { useSyncExternalStore, type AnchorHTMLAttributes, type MouseEvent, type ReactNode } from 'react';

/**
 * A small client-side router on the browser's history API (owner, DEC-040: no routing package in
 * slice 3). The server answers every path without a file extension with the SPA shell, so a route
 * must never end in a segment with a dot. `/api` and `/health` belong to the server.
 */

const listeners = new Set<() => void>();

function notify() {
  listeners.forEach((listener) => listener());
}

window.addEventListener('popstate', notify);

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function snapshot() {
  return window.location.pathname + window.location.search + window.location.hash;
}

export interface Location {
  pathname: string;
  search: URLSearchParams;
  hash: string;
}

export function useLocation(): Location {
  const href = useSyncExternalStore(subscribe, snapshot);
  const url = new URL(href, window.location.origin);
  return { pathname: url.pathname, search: url.searchParams, hash: url.hash };
}

export function navigate(to: string, options: { replace?: boolean } = {}): void {
  if (options.replace) {
    window.history.replaceState(null, '', to);
  } else {
    window.history.pushState(null, '', to);
  }
  notify();
}

/** A same-origin path to return to after sign-in; anything else (another origin, `//host`) becomes `/`. */
export function safeReturnPath(value: string | null): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return '/';
  }
  return value;
}

/** Follows a link inside the app without reloading; modified clicks open as the browser would. */
export function followLink(event: MouseEvent<HTMLElement>, to: string): void {
  if (
    event.defaultPrevented ||
    event.button !== 0 ||
    event.metaKey ||
    event.ctrlKey ||
    event.shiftKey ||
    event.altKey
  ) {
    return;
  }
  event.preventDefault();
  navigate(to);
}

export function RouterLink({
  to,
  children,
  ...rest
}: { to: string; children: ReactNode } & AnchorHTMLAttributes<HTMLAnchorElement>) {
  return (
    <a href={to} onClick={(event) => followLink(event, to)} {...rest}>
      {children}
    </a>
  );
}
