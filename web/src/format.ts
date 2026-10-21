/** Times are shown in UTC, as the boards label them ("Last sign-in (UTC)"). */
export function formatUtc(iso: string | null | undefined): string {
  if (!iso) return 'Never';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return `${date.toISOString().slice(0, 10)} ${date.toISOString().slice(11, 16)}`;
}

/** "2026-10-20 07:58:12": an audit entry's time, UTC, to the second. */
export function formatUtcSeconds(iso: string): string {
  const text = new Date(iso).toISOString();
  return `${text.slice(0, 10)} ${text.slice(11, 19)}`;
}

/** "2026-10-20 09:58:12 (UTC+2)": the same moment in the viewer's own time zone. */
export function formatLocalSeconds(iso: string): string {
  const date = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  const offset = -date.getTimezoneOffset();
  const hours = Math.trunc(Math.abs(offset) / 60);
  const minutes = Math.abs(offset) % 60;
  const zone = offset === 0 ? 'UTC' : `UTC${offset > 0 ? '+' : '-'}${hours}${minutes ? `:${pad(minutes)}` : ''}`;
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())} (${zone})`
  );
}

export function formatUtcTime(iso: string): string {
  return new Date(iso).toISOString().slice(11, 16);
}

/** "in 20 h", "in 45 min": how long until a link expires. */
export function formatRemaining(iso: string, now: Date = new Date()): string {
  const minutes = Math.max(0, Math.round((new Date(iso).getTime() - now.getTime()) / 60000));
  return minutes >= 60 ? `${Math.round(minutes / 60)} h` : `${minutes} min`;
}

export function initialsOf(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');
}
