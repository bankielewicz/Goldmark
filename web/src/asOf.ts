import { useEffect } from 'react';

import { navigate, useLocation } from './router';

/**
 * The moment a past view shows (US-42; DEC-047, the slice 7 boards): `asOf` in the address of the three screens
 * that have a past (Search, a CI's page, the Graph explorer), a UTC instant written as the API takes it
 * (`2026-10-06T14:00Z`, or with seconds and up to seven decimals when it comes from a history entry). On those
 * screens the address is the truth. The moment is also kept for the browser session, so a detour to another screen
 * doesn't lose it (Notes default 2): the navigation's links to the three screens bring it back. One of the three
 * opened without `asOf` shows the present and forgets the kept moment.
 *
 * Nothing of one moment stays on the page of another (DEC-047 item 12): a CI's page is one page per moment (`App`
 * puts the moment in its key, so a change of moment starts it again, as a change of CI does); Search keeps its rows
 * and counts with the moment they were read for; the types come with theirs (`useCiTypes`); and a form that writes
 * is never rendered in a past view.
 */

const storageKey = 'goldmark.asOf';
const pattern = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?Z$/;

/** The screens with a past. */
export function hasPast(pathname: string): boolean {
  return pathname === '/search' || pathname === '/graph' || pathname.startsWith('/ci/');
}

/**
 * The moment if `value` is written as the API takes it (a real UTC date and time, years 2000 to 9998), else null: a
 * link with a moment the page can't show opens the present (docs/integration/deep-links.md). Whether a moment is in
 * the future is the database's to say, not this browser's clock (DEC-047 item 2): the API refuses one that is, and
 * {@link presentInsteadOfFuture} then shows the present.
 */
export function parseAsOf(value: string | null | undefined): string | null {
  const match = value?.match(pattern);
  if (!match || !value) return null;
  const [y, mo, d, h, mi, s] = match.slice(1, 7).map((part) => (part === undefined ? 0 : Number(part))) as [
    number,
    number,
    number,
    number,
    number,
    number,
  ];
  const date = new Date(Date.UTC(y, mo - 1, d, h, mi, s));
  const real =
    date.getUTCFullYear() === y &&
    date.getUTCMonth() === mo - 1 &&
    date.getUTCDate() === d &&
    h < 24 &&
    mi < 60 &&
    s < 60;
  return real && y >= 2000 && y <= 9998 ? value : null;
}

/** The moment as a Date (to the millisecond: enough to show it; the API is sent the text itself). */
export function asOfDate(value: string): Date {
  return new Date(value);
}

const pad = (n: number) => String(n).padStart(2, '0');
const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** "2026-10-06 14:00 UTC": the top bar, the banner and the dialogs show minutes (Notes default 7). */
export function formatMoment(value: string): string {
  const text = asOfDate(value).toISOString();
  return `${text.slice(0, 10)} ${text.slice(11, 16)} UTC`;
}

/** "06 Oct 14:00", or "01 Mar 2019 09:00" in another year: the top bar's label on the rail and on a phone. */
export function shortMoment(value: string, now: Date = new Date()): string {
  const date = asOfDate(value);
  const day = `${pad(date.getUTCDate())} ${months[date.getUTCMonth()]}`;
  const year = date.getUTCFullYear() === now.getUTCFullYear() ? '' : ` ${date.getUTCFullYear()}`;
  return `${day}${year} ${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}`;
}

/**
 * "16:00 your local time": the moment in the browser's time zone. When the local date isn't the UTC date, it is
 * named: "01:30 on 2026-10-07 your local time".
 */
export function localHint(value: string): string {
  const date = asOfDate(value);
  const time = `${pad(date.getHours())}:${pad(date.getMinutes())}`;
  const localDay = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  return localDay === date.toISOString().slice(0, 10)
    ? `${time} your local time`
    : `${time} on ${localDay} your local time`;
}

/** The moment kept for the browser session, or null. */
export function keptAsOf(): string | null {
  try {
    return parseAsOf(window.sessionStorage.getItem(storageKey));
  } catch {
    return null;
  }
}

function keepAsOf(value: string | null) {
  try {
    if (value) window.sessionStorage.setItem(storageKey, value);
    else window.sessionStorage.removeItem(storageKey);
  } catch {
    // Storage refused (a private window's quota): the address still carries the moment.
  }
}

/** `path` with `asOf` set to `value`, or without it for null; any other parameter stays. */
export function withAsOf(path: string, value: string | null): string {
  const url = new URL(path, window.location.origin);
  if (value) url.searchParams.set('asOf', value);
  else url.searchParams.delete('asOf');
  return url.pathname + url.search + url.hash;
}

/**
 * The moment the current screen shows: its address's `asOf` on the three screens with a past, else null. On those
 * screens it also keeps (or forgets) the moment for the session.
 */
export function useAsOf(): string | null {
  const { pathname, search } = useLocation();
  const past = hasPast(pathname);
  const value = past ? parseAsOf(search.get('asOf')) : null;
  useEffect(() => {
    if (past) keepAsOf(value);
  }, [past, value]);
  return value;
}

/** Shows the current screen at `value`, or in the present for null (a new history entry, so Back returns). */
export function setAsOf(value: string | null): void {
  keepAsOf(value);
  navigate(withAsOf(window.location.pathname + window.location.search + window.location.hash, value));
}

/**
 * The API refused the moment of the request to `requestPath` as ahead of the database's clock (`FutureTime`). If
 * the screen still shows that moment, it shows the present instead, in the same history entry, and the moment is
 * forgotten; an answer that comes after the person moved on changes nothing.
 */
export function presentInsteadOfFuture(requestPath: string): void {
  const asked = new URL(requestPath, window.location.origin).searchParams.get('asOf');
  const shown = new URLSearchParams(window.location.search).get('asOf');
  if (!asked || asked !== shown || !hasPast(window.location.pathname)) return;
  keepAsOf(null);
  navigate(withAsOf(window.location.pathname + window.location.search + window.location.hash, null), {
    replace: true,
  });
}

/** Read-only in a past view: the one Tooltip on every action that would change something (Notes default 10). */
export const readOnlyTip = 'You’re viewing a past time. Return to now to make changes.';
