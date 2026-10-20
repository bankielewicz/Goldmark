/** Times are shown in UTC, as the boards label them ("Last sign-in (UTC)"). */
export function formatUtc(iso: string | null | undefined): string {
  if (!iso) return 'Never';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return `${date.toISOString().slice(0, 10)} ${date.toISOString().slice(11, 16)}`;
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
