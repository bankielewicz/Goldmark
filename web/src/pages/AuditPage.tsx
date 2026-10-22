import {
  Badge,
  Button,
  Combobox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Dropdown,
  Field,
  Input,
  Link,
  MessageBar,
  MessageBarBody,
  Option,
  OptionGroup,
  Skeleton,
  SkeletonItem,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  Toast,
  ToastBody,
  ToastTitle,
  Toaster,
  Tooltip,
  makeStyles,
  tokens,
  useId,
  useToastController,
} from '@fluentui/react-components';
import {
  ArrowExportRegular,
  CheckmarkCircleRegular,
  ChevronDownRegular,
  ChevronRightRegular,
  DismissCircleRegular,
  FilterRegular,
  InfoRegular,
  LockClosedRegular,
} from '@fluentui/react-icons';
import { Fragment, useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { api, ApiError } from '../api';
import { formatLocalSeconds, formatUtc, formatUtcSeconds } from '../format';
import { usePreferences } from '../preferences';
import { followLink, navigate, useLocation } from '../router';
import { isAdmin, useSession } from '../session';
import { useWide } from '../shell/AppShell';
import { PageHeader } from '../shell/PageHeader';
import { downloadBlob, ErrorBar } from '../ui/common';

const useStyles = makeStyles({
  filters: { display: 'flex', gap: tokens.spacingHorizontalM, flexWrap: 'wrap', alignItems: 'flex-end' },
  time: { width: '160px', minWidth: '160px' },
  person: { width: '190px', minWidth: '190px' },
  action: { width: '200px', minWidth: '200px' },
  ip: { width: '150px', fontFamily: tokens.fontFamilyMonospace },
  outcome: { width: '130px', minWidth: '130px' },
  custom: { width: '200px' },
  count: { fontWeight: tokens.fontWeightSemibold },
  region: {
    minWidth: 0,
    overflowX: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
  },
  table: { minWidth: '820px' },
  wideTable: { minWidth: '1000px' },
  expandColumn: { width: '48px' },
  timeCell: { whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' },
  party: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  dot: { color: tokens.colorNeutralForeground3 },
  mono: { fontFamily: tokens.fontFamilyMonospace, color: tokens.colorNeutralForeground2 },
  none: { color: tokens.colorNeutralForeground3 },
  ipHead: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalXXS, whiteSpace: 'nowrap' },
  openRow: { backgroundColor: tokens.colorBrandBackground2 },
  detailCell: { padding: 0 },
  details: {
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalXXL} ${tokens.spacingVerticalL} 52px`,
    backgroundColor: tokens.colorNeutralBackground2,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  dl: {
    margin: 0,
    display: 'grid',
    gridTemplateColumns: '150px minmax(0, 1fr) 150px minmax(0, 1fr)',
    gap: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    '@media (max-width: 1099px)': { gridTemplateColumns: '150px minmax(0, 1fr)' },
  },
  dt: { color: tokens.colorNeutralForeground3, fontWeight: tokens.fontWeightSemibold },
  dd: { margin: 0, overflowWrap: 'anywhere' },
  code: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  detailActions: { display: 'flex', gap: tokens.spacingHorizontalL, alignItems: 'center', flexWrap: 'wrap' },
  empty: {
    padding: tokens.spacingHorizontalXXL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    alignItems: 'flex-start',
  },
  skeleton: {
    padding: tokens.spacingHorizontalL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  dialogList: {
    margin: 0,
    display: 'grid',
    gridTemplateColumns: '80px minmax(0, 1fr)',
    gap: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
  },
  dialogBody: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  srOnly: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    margin: '-1px',
    overflow: 'hidden',
    clipPath: 'inset(50%)',
    whiteSpace: 'nowrap',
  },
});

/** The server's description of every audit action (`GET /api/v1/audit/catalog`, DEC-041). */
interface CatalogAction {
  code: string;
  group: string;
  label: string;
  outcome: Outcome;
}

type Outcome = 'Succeeded' | 'Failed' | 'Blocked';

interface Catalog {
  retentionDays: number;
  actions: CatalogAction[];
}

interface Person {
  id: string;
  userName: string;
  displayName: string;
}

/** Who acted, or whom an entry is about. A person whose account was removed since is `removed`. */
interface Party {
  type: string;
  id: string | null;
  userName: string | null;
  displayName: string | null;
  removed: boolean;
}

interface Entry {
  id: number;
  at: string;
  actor: Party;
  action: string;
  label: string;
  outcome: Outcome;
  target: Party | null;
  ip: string | null;
  details: Record<string, unknown> | null;
}

interface EntriesPage {
  entries: Entry[];
  total: number;
  next: number | null;
}

const exportLimit = 100_000;

type Range = '24h' | '7d' | '30d' | '365d' | 'custom';

const presets: Record<Exclude<Range, 'custom'>, { label: string; hours: number }> = {
  '24h': { label: 'Last 24 hours', hours: 24 },
  '7d': { label: 'Last 7 days', hours: 24 * 7 },
  '30d': { label: 'Last 30 days', hours: 24 * 30 },
  '365d': { label: 'Last 365 days', hours: 24 * 365 },
};

const outcomes: Outcome[] = ['Succeeded', 'Failed', 'Blocked'];
const ipPattern = /^[0-9A-Fa-f.:]{1,45}$/;

/** The filters, kept in the page address so a filtered view can be bookmarked or sent on. */
interface Filters {
  range: Range;
  /** Custom range: `YYYY-MM-DDTHH:mm`, read as UTC. */
  from: string;
  to: string;
  /** A person's id, `System` or `Anonymous`; empty for any actor. */
  actor: string;
  actions: string[];
  target: string;
  ip: string;
  outcome: string;
  oldest: boolean;
}

const defaults: Filters = {
  range: '7d',
  from: '',
  to: '',
  actor: '',
  actions: [],
  target: '',
  ip: '',
  outcome: '',
  oldest: false,
};

function readFilters(search: URLSearchParams): Filters {
  const range = search.get('range');
  return {
    range: range === 'custom' || (range !== null && Object.hasOwn(presets, range)) ? (range as Range) : defaults.range,
    from: search.get('from') ?? '',
    to: search.get('to') ?? '',
    actor: search.get('actor') ?? '',
    actions: search.getAll('action'),
    target: search.get('target') ?? '',
    ip: search.get('ip') ?? '',
    outcome: outcomes.includes(search.get('outcome') as Outcome) ? (search.get('outcome') ?? '') : '',
    oldest: search.get('order') === 'oldest',
  };
}

/** The page address's query: only what differs from the defaults. */
function pageQuery(filters: Filters): string {
  const query = new URLSearchParams();
  if (filters.range !== defaults.range) query.set('range', filters.range);
  if (filters.range === 'custom') {
    if (filters.from) query.set('from', filters.from);
    if (filters.to) query.set('to', filters.to);
  }
  if (filters.actor) query.set('actor', filters.actor);
  filters.actions.forEach((action) => query.append('action', action));
  if (filters.target) query.set('target', filters.target);
  if (filters.ip) query.set('ip', filters.ip);
  if (filters.outcome) query.set('outcome', filters.outcome);
  if (filters.oldest) query.set('order', 'oldest');
  const text = query.toString();
  return text ? `?${text}` : '';
}

function isDefault(filters: Filters): boolean {
  return pageQuery({ ...filters, oldest: false }) === '';
}

/**
 * A custom-range time as the API takes it, or null. The years are the server's (2000 to 9998,
 * `AuditQuery`): a browser's date field passes through years like 0002 and 0202 while one is typed.
 */
function customTime(value: string): string | null {
  const match = /^(\d{4})-\d{2}-\d{2}T\d{2}:\d{2}$/.exec(value);
  const year = Number(match?.[1]);
  return match && year >= 2000 && year <= 9998 ? `${value}:00Z` : null;
}

/** The custom range's problem, if any: both ends are needed, in years Goldmark accepts, From before To. */
function rangeProblem(filters: Filters): string | null {
  if (filters.range !== 'custom') return null;
  if (!filters.from || !filters.to) return 'Enter both times.';
  const from = customTime(filters.from);
  const to = customTime(filters.to);
  if (!from || !to) return 'Use a year from 2000 to 9998.';
  return from < to ? null : 'From must be before To.';
}

/** Any failure as an `ApiError`, so an unexpected one still ends in an error state with Retry. */
function asApiError(error: unknown): ApiError {
  return error instanceof ApiError ? error : new ApiError(0, 'unexpected', 'Something went wrong on this page.');
}

/** The API's query for these filters; preset ranges count back from now. */
function apiQuery(filters: Filters): URLSearchParams {
  const query = new URLSearchParams();
  if (filters.range === 'custom') {
    const from = customTime(filters.from);
    const to = customTime(filters.to);
    if (from) query.set('from', from);
    if (to) query.set('to', to);
  } else {
    query.set('from', new Date(Date.now() - presets[filters.range].hours * 3_600_000).toISOString());
  }
  if (filters.actor) query.set('actor', filters.actor);
  filters.actions.forEach((action) => query.append('action', action));
  if (filters.target) query.set('target', filters.target);
  if (filters.ip) query.set('ip', filters.ip);
  if (filters.outcome) query.set('outcome', filters.outcome);
  if (filters.oldest) query.set('order', 'oldest');
  return query;
}

function count(n: number, one: string, many: string): string {
  return `${n.toLocaleString('en-US')} ${n === 1 ? one : many}`;
}

const actorTypes: Record<string, string> = { User: 'Person', System: 'System', Anonymous: 'Not signed in' };

function personText(party: Party): string {
  if (party.displayName && party.userName) return `${party.displayName} · ${party.userName}`;
  if (party.removed) return party.userName ? `${party.userName} (account removed)` : 'Removed account';
  return '';
}

/**
 * A person's name: from the people list or, for a removed account (no longer on it), from a loaded
 * entry about them; their id if neither knows them.
 */
function peopleName(id: string, people: Person[], entries: readonly Entry[] = []): string {
  const person = people.find((p) => p.id === id);
  if (person) return person.displayName;
  const party = entries.flatMap((e) => [e.actor, e.target]).find((p) => p?.id === id);
  return (party && personText(party)) || id;
}

/** The picker option for the person a filter names when the people list doesn't have them (a removed account). */
function unlistedPerson(id: string, people: Person[], entries: readonly Entry[]): { key: string; text: string }[] {
  if (!id || Object.hasOwn(actorTypes, id) || people.some((p) => p.id === id)) return [];
  return [{ key: id, text: peopleName(id, people, entries) }];
}

/** The filters in words, for the export dialog: "Last 7 days · Outcome: Failed". */
function describeFilters(
  filters: Filters,
  catalog: Catalog | null,
  people: Person[],
  entries: readonly Entry[],
): string {
  const parts = [
    filters.range === 'custom'
      ? `${filters.from.replace('T', ' ')} to ${filters.to.replace('T', ' ')} UTC`
      : presets[filters.range].label,
  ];
  if (filters.actor) {
    parts.push(
      `Actor: ${Object.hasOwn(actorTypes, filters.actor) ? actorTypes[filters.actor] : peopleName(filters.actor, people, entries)}`,
    );
  }
  if (filters.actions.length > 0) {
    const labels = filters.actions.map((code) => catalog?.actions.find((a) => a.code === code)?.label ?? code);
    parts.push(`Action: ${labels.join(', ')}`);
  }
  if (filters.target) parts.push(`Person affected: ${peopleName(filters.target, people, entries)}`);
  if (filters.ip) parts.push(`Source IP: ${filters.ip}`);
  if (filters.outcome) parts.push(`Outcome: ${filters.outcome}`);
  return parts.length === 1 ? `${parts[0]} · no other filters` : parts.join(' · ');
}

const reasons: Record<string, string> = {
  password: 'Wrong password',
  'authenticator-code': 'Wrong authenticator code',
  'recovery-code': 'Wrong recovery code',
  'locked-out': 'Account locked',
  'locked-by-administrator': 'Locked by an administrator',
  disabled: 'Account disabled',
  'not-set-up': 'Account not set up yet',
  'two-factor-reset': 'Two-factor was reset',
  'password-reset-pending': 'Password reset pending',
  restart: 'Goldmark restarted (a recovery link lasts until the next start)',
  'unknown-user': 'Unknown username (the name typed is not recorded)',
};
const methods: Record<string, string> = {
  authenticator: 'Password and authenticator code',
  'recovery-code': 'Password and recovery code',
};
// A link's purpose as stored (link entries) and as named in request entries (`for`).
const purposes: Record<string, string> = {
  Invitation: 'Invitation',
  TwoFactorReset: 'Two-factor reset',
  PasswordReset: 'Password reset',
  CombinedReset: 'Password and two-factor reset',
  invitation: 'Invitation',
  'two-factor-reset': 'Two-factor reset',
  'password-reset': 'Password reset',
  'combined-reset': 'Password and two-factor reset',
};
const confirmations: Record<string, string> = {
  'authenticator-code': 'Authenticator code',
  'recovery-code': 'Recovery code',
  'new-authenticator': 'New authenticator',
};
const approvals: Record<string, string> = { requested: 'Requested', 'only-admin': 'Not needed: only one Admin' };
// Who issued a link that no account issued, or whose issuer was removed (DEC-042).
const issuers: Record<string, string> = {
  'server-setting': 'The recovery setting on the server',
  'removed-account': 'A removed account',
};

function yesNo(value: unknown): string {
  return value === true ? 'Yes' : value === false ? 'No' : text(value);
}

function utc(value: unknown): string {
  return typeof value === 'string' ? `${formatUtc(value)} UTC` : text(value);
}

function text(value: unknown): string {
  if (typeof value === 'string') return value;
  if (typeof value === 'number') return value.toLocaleString('en-US');
  if (Array.isArray(value)) return value.map(text).join(', ');
  return JSON.stringify(value);
}

function days(value: unknown): string {
  return value === 0 ? 'Kept indefinitely' : `${text(value)} days`;
}

function strings(value: unknown): string[] {
  return Array.isArray(value) ? value.map(String) : [];
}

/** An export's recorded filters, in words (the entry's own `filters` detail). */
function describeRecordedFilters(value: unknown, catalog: Catalog | null, people: Person[]): string {
  if (typeof value !== 'object' || value === null) return text(value);
  const f = value as Record<string, unknown>;
  const parts: string[] = [];
  if (typeof f.from === 'string') parts.push(`From ${formatUtc(f.from)} UTC`);
  if (typeof f.to === 'string') parts.push(`to ${formatUtc(f.to)} UTC`);
  if (typeof f.actor === 'string') parts.push(`Actor: ${actorTypes[f.actor] ?? peopleName(f.actor, people)}`);
  if (Array.isArray(f.actions)) {
    parts.push(
      `Action: ${strings(f.actions)
        .map((code) => catalog?.actions.find((a) => a.code === code)?.label ?? code)
        .join(', ')}`,
    );
  }
  if (typeof f.target === 'string') parts.push(`Person affected: ${peopleName(f.target, people)}`);
  if (typeof f.ip === 'string') parts.push(`Source IP: ${f.ip}`);
  if (typeof f.outcome === 'string') parts.push(`Outcome: ${f.outcome}`);
  return parts.length > 0 ? parts.join(' · ') : 'No filters';
}

/** An entry's own details in words, never raw JSON; an unknown detail shows as its name and value. */
function describeDetails(entry: Entry, catalog: Catalog | null, people: Person[]): [string, string][] {
  const details = entry.details ?? {};
  const shown = new Set(['userName']);
  const rows: [string, string][] = [];
  const add = (key: string, label: string, value: string) => {
    shown.add(key);
    rows.push([label, value]);
  };

  switch (entry.action) {
    case 'user.roles.changed': {
      const before = strings(details.before);
      const after = strings(details.after);
      add('before', 'Before', before.join(', ') || 'No role');
      add('after', 'After', after.join(', ') || 'No role');
      const added = after.filter((r) => !before.includes(r));
      const removed = before.filter((r) => !after.includes(r));
      rows.push(['Change', `Added: ${added.join(', ') || 'none'}. Removed: ${removed.join(', ') || 'none'}.`]);
      break;
    }
    case 'audit.retention.purged':
      add(
        'deleted',
        'Deleted',
        `${text(details.deleted)} entries from before ${typeof details.before === 'string' ? formatUtc(details.before) : text(details.before)} UTC`,
      );
      shown.add('before');
      add('retentionDays', 'Retention', days(details.retentionDays));
      break;
    case 'audit.retention.changed':
      add('before', 'Before', details.before === undefined ? 'Not set' : days(details.before));
      add('after', 'After', days(details.after));
      break;
    case 'audit.exported':
      add('filters', 'Filters', describeRecordedFilters(details.filters, catalog, people));
      add('entries', 'Entries exported', text(details.entries));
      break;
    default:
      break;
  }

  for (const [key, value] of Object.entries(details)) {
    if (shown.has(key)) continue;
    if (key === 'approvedBy' && value === null) {
      // Approval wasn't needed: one Admin, or the recovery setting (DEC-042).
      add(key, 'Approved by', details.issuedBy === 'server-setting' ? 'Not needed' : 'Not needed: only one Admin');
      continue;
    }
    if (value === null) continue;
    switch (key) {
      case 'reason':
        add(key, 'Reason', reasons[text(value)] ?? text(value));
        break;
      case 'method':
        add(key, 'Method', methods[text(value)] ?? text(value));
        break;
      case 'remaining':
        add(key, 'Recovery codes left', text(value));
        break;
      case 'minutes':
        add(key, 'Locked for', `${text(value)} minutes`);
        break;
      case 'roles':
        add(key, 'Roles', text(value));
        break;
      case 'expiresAt':
      case 'linkExpiresAt':
        add(key, 'Link expires', utc(value));
        break;
      case 'purpose':
      case 'for':
        add(key, 'For', purposes[text(value)] ?? text(value));
        break;
      case 'twoFactorToo':
        add(key, 'Two-factor reset too', yesNo(value));
        break;
      case 'confirmedWith':
        add(key, 'Confirmed with', confirmations[text(value)] ?? text(value));
        break;
      case 'issuedBy':
        add(key, 'Link issued by', issuers[text(value)] ?? text(value));
        break;
      case 'approvedBy':
        add(key, 'Approved by', text(value));
        break;
      case 'approval':
        add(key, 'Approval', approvals[text(value)] ?? text(value));
        break;
      case 'askedBy':
        add(key, 'Asked by', text(value));
        break;
      case 'accountRemoved':
        add(key, 'Account removed', yesNo(value));
        break;
      case 'otherSessionsEnded':
        add(key, 'Other sessions ended', text(value));
        break;
      case 'triesLeft':
        add(key, 'Tries left', text(value));
        break;
      default:
        add(key, key, text(value));
    }
  }
  return rows;
}

type ExportState =
  | { kind: 'confirm' }
  | { kind: 'preparing' }
  | { kind: 'too-many'; entries: number }
  | { kind: 'failed'; error: ApiError };

/**
 * Screen 16, the audit log (US-07, NFR-LOG-3, DEC-041), for Admins and Auditors: who did what, when
 * and from where. Read-only; an export is recorded in the log itself. Built on the approved boards
 * (`docs/design/screens/slice-4/`).
 */
export function AuditPage() {
  const styles = useStyles();
  const session = useSession();
  const admin = isAdmin(session.user);
  const { search } = useLocation();
  const searchText = search.toString();
  const filters = useMemo(() => readFilters(new URLSearchParams(searchText)), [searchText]);
  const wide = useWide();
  const { compact } = usePreferences();
  const toasterId = useId('audit-toaster');
  const { dispatchToast } = useToastController(toasterId);
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [people, setPeople] = useState<Person[]>([]);
  const [page, setPage] = useState<EntriesPage | null>(null);
  // The entries' failure (first page or "Load more"), and the catalog's and people's, kept apart so
  // each Retry repeats the request that failed.
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [referenceFailure, setReferenceFailure] = useState<ApiError | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [expanded, setExpanded] = useState<ReadonlySet<number>>(new Set());
  const [exportState, setExportState] = useState<ExportState | null>(null);
  const [ipText, setIpText] = useState(filters.ip);
  // After "Load more": the first new entry, whose expand button takes focus.
  const [focusEntry, setFocusEntry] = useState<number | null>(null);
  // The API query behind the entries shown, so "Load more" and the export continue the same list, and
  // the number of the latest first-page request: an answer to an older one is dropped.
  const shownQuery = useRef('');
  const listRequest = useRef(0);
  // Cancels the export in progress (its Cancel stays available, as on the board).
  const exportAbort = useRef<AbortController | null>(null);
  const problem = rangeProblem(filters);

  const apply = (next: Filters) => navigate(`/audit${pageQuery(next)}`, { replace: true });

  // The action labels, the retention period and the people for the filters: loaded once.
  const loadReference = useCallback(async () => {
    setReferenceFailure(null);
    try {
      const [catalogResponse, peopleResponse] = await Promise.all([
        api<Catalog>('GET', '/api/v1/audit/catalog'),
        api<{ people: Person[] }>('GET', '/api/v1/audit/people'),
      ]);
      setCatalog(catalogResponse.data);
      setPeople(peopleResponse.data.people);
    } catch (error) {
      setReferenceFailure(asApiError(error));
    }
  }, []);

  const load = useCallback(async () => {
    // A new list starts, even an empty one for an incomplete custom range: answers to the list it
    // replaces, first page or "Load more", fail the request-number check and are dropped (Codex, PR #32).
    const request = ++listRequest.current;
    setFailure(null);
    setExpanded(new Set());
    setLoadingMore(false);
    if (rangeProblem(filters)) {
      shownQuery.current = '';
      setPage({ entries: [], total: 0, next: null });
      return;
    }
    setPage(null);
    const query = apiQuery(filters).toString();
    shownQuery.current = query;
    try {
      const { data } = await api<EntriesPage>('GET', `/api/v1/audit?${query}`);
      if (listRequest.current === request) setPage(data);
    } catch (error) {
      if (listRequest.current === request) setFailure(asApiError(error));
    }
  }, [filters]);

  useEffect(() => {
    void loadReference();
  }, [loadReference]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => setIpText(filters.ip), [filters.ip]);

  useEffect(() => () => exportAbort.current?.abort(), []);

  useEffect(() => {
    if (focusEntry !== null) {
      document.getElementById(`audit-entry-${focusEntry}`)?.focus();
      setFocusEntry(null);
    }
  }, [focusEntry, page]);

  const loadMore = async () => {
    if (!page?.next) return;
    const request = listRequest.current;
    setLoadingMore(true);
    setFailure(null);
    try {
      const { data } = await api<EntriesPage>('GET', `/api/v1/audit?${shownQuery.current}&cursor=${page.next}`);
      // The filters changed meanwhile: these entries belong to the list that was replaced.
      if (listRequest.current !== request) return;
      setPage((shown) => shown && { entries: [...shown.entries, ...data.entries], total: data.total, next: data.next });
      setFocusEntry(data.entries[0]?.id ?? null);
    } catch (error) {
      if (listRequest.current === request) setFailure(asApiError(error));
    } finally {
      // Only for this list: a replaced list's continuation must not touch the new one's button.
      if (listRequest.current === request) setLoadingMore(false);
    }
  };

  const toggle = (id: number) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const commitIp = () => {
    const value = ipText.trim();
    if (value === filters.ip || (value !== '' && !ipPattern.test(value))) return;
    apply({ ...filters, ip: value });
  };
  const ipInvalid = ipText.trim() !== '' && !ipPattern.test(ipText.trim());

  const startExport = () => {
    if (!page) return;
    setExportState(page.total > exportLimit ? { kind: 'too-many', entries: page.total } : { kind: 'confirm' });
  };

  const runExport = async () => {
    const abort = new AbortController();
    exportAbort.current = abort;
    setExportState({ kind: 'preparing' });
    try {
      // The list shown, not a new "last 7 days" counted from now: the export is what the dialog confirmed.
      const { data, headers } = await api<Blob>(
        'POST',
        `/api/v1/audit/export?${shownQuery.current}`,
        {},
        {
          file: true,
          signal: abort.signal,
        },
      );
      if (abort.signal.aborted) return;
      const entries = Number(headers.get('X-Goldmark-Entries') ?? page?.total ?? 0);
      const fileName = `goldmark-audit-${new Date().toISOString().slice(0, 10).replace(/-/g, '')}.csv`;
      downloadBlob(data, fileName);
      setExportState(null);
      dispatchToast(
        <Toast>
          <ToastTitle media={<CheckmarkCircleRegular />}>Exported {count(entries, 'entry', 'entries')}</ToastTitle>
          <ToastBody>{fileName}</ToastBody>
        </Toast>,
        { intent: 'success', politeness: 'polite' },
      );
    } catch (error) {
      if (abort.signal.aborted) return;
      if (error instanceof ApiError && error.code === 'export-too-large') {
        setExportState({ kind: 'too-many', entries: Number(error.body.entries ?? 0) });
      } else {
        setExportState({ kind: 'failed', error: asApiError(error) });
      }
    }
  };

  const closeExport = () => {
    exportAbort.current?.abort();
    exportAbort.current = null;
    setExportState(null);
  };

  const retention =
    catalog === null
      ? null
      : catalog.retentionDays === 0
        ? 'Entries are kept indefinitely.'
        : `Entries older than ${catalog.retentionDays.toLocaleString('en-US')} days are deleted automatically.`;

  const shownEntries = page?.entries;
  const actorOptions = useMemo(
    () => [
      { key: 'System', text: 'System' },
      { key: 'Anonymous', text: 'Not signed in' },
      ...people.map((p) => ({ key: p.id, text: `${p.displayName} (${p.userName})` })),
      ...unlistedPerson(filters.actor, people, shownEntries ?? []),
    ],
    [people, filters.actor, shownEntries],
  );
  const targetOptions = useMemo(
    () => [
      ...people.map((p) => ({ key: p.id, text: `${p.displayName} (${p.userName})` })),
      ...unlistedPerson(filters.target, people, shownEntries ?? []),
    ],
    [people, filters.target, shownEntries],
  );
  const groups = useMemo(() => {
    const byGroup = new Map<string, CatalogAction[]>();
    (catalog?.actions ?? []).forEach((a) => byGroup.set(a.group, [...(byGroup.get(a.group) ?? []), a]));
    return [...byGroup.entries()];
  }, [catalog]);

  const nothingToExport = !page || page.total === 0;
  const exportButton = (
    <Button icon={<ArrowExportRegular />} disabledFocusable={nothingToExport} onClick={startExport}>
      Export CSV…
    </Button>
  );

  const columns = wide ? 7 : 6;
  const emptyText =
    filters.range === 'custom'
      ? 'No entries in this time range.'
      : `No entries in the ${presets[filters.range].label.toLowerCase()}.`;

  let body: ReactNode;
  if (failure && !page) {
    body = <ErrorBar error={failure} onRetry={() => void load()} message="Couldn’t load the audit log." />;
  } else if (!page) {
    body = (
      <Skeleton aria-label="Loading audit entries" className={styles.skeleton}>
        {[1, 2, 3, 4, 5, 6].map((n) => (
          <SkeletonItem key={n} size={32} />
        ))}
      </Skeleton>
    );
  } else if (page.entries.length === 0) {
    body = isDefault(filters) ? (
      <div className={styles.empty}>
        <Text>{emptyText}</Text>
      </div>
    ) : (
      <div className={styles.empty}>
        <Text>No entries match these filters.</Text>
        <Button onClick={() => apply({ ...defaults, oldest: filters.oldest })}>Clear filters</Button>
      </div>
    );
  } else {
    body = (
      <Table
        size={compact ? 'small' : 'medium'}
        aria-label={filters.oldest ? 'Audit log entries, oldest first' : 'Audit log entries, newest first'}
        className={wide ? styles.wideTable : styles.table}
      >
        <TableHeader>
          <TableRow>
            <TableHeaderCell className={styles.expandColumn}>
              <span className={styles.srOnly}>Details</span>
            </TableHeaderCell>
            <TableHeaderCell
              sortable
              sortDirection={filters.oldest ? 'ascending' : 'descending'}
              onClick={() => apply({ ...filters, oldest: !filters.oldest })}
            >
              Time (UTC)
            </TableHeaderCell>
            <TableHeaderCell>Actor</TableHeaderCell>
            <TableHeaderCell>Action</TableHeaderCell>
            <TableHeaderCell>Person affected</TableHeaderCell>
            {wide ? (
              <TableHeaderCell>
                <span className={styles.ipHead}>
                  IP address
                  <Tooltip
                    content="The address that connected to Goldmark. Behind a reverse proxy, this is the proxy’s address."
                    relationship="description"
                  >
                    <Button
                      appearance="transparent"
                      size="small"
                      icon={<InfoRegular />}
                      aria-label="About IP address: the address that connected to Goldmark"
                    />
                  </Tooltip>
                </span>
              </TableHeaderCell>
            ) : null}
            <TableHeaderCell>Outcome</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {page.entries.map((entry) => {
            const open = expanded.has(entry.id);
            const utc = formatUtcSeconds(entry.at);
            return (
              <Fragment key={entry.id}>
                <TableRow className={open ? styles.openRow : undefined}>
                  <TableCell>
                    <Button
                      id={`audit-entry-${entry.id}`}
                      appearance="subtle"
                      icon={open ? <ChevronDownRegular /> : <ChevronRightRegular />}
                      aria-expanded={open}
                      aria-label={`Show details for entry ${entry.id}: ${entry.label} at ${utc.slice(11)}`}
                      onClick={() => toggle(entry.id)}
                    />
                  </TableCell>
                  <TableCell className={styles.timeCell}>
                    <Tooltip content={`Your local time: ${formatLocalSeconds(entry.at)}`} relationship="description">
                      <span>{utc}</span>
                    </Tooltip>
                  </TableCell>
                  <TableCell>
                    <span className={styles.party}>
                      <Badge appearance="tint" color="subtle">
                        {actorTypes[entry.actor.type] ?? entry.actor.type}
                      </Badge>
                      {entry.actor.type === 'User' ? <PartyName party={entry.actor} /> : null}
                    </span>
                  </TableCell>
                  <TableCell>{entry.label}</TableCell>
                  <TableCell>{entry.target ? <PartyName party={entry.target} /> : <None />}</TableCell>
                  {wide ? <TableCell className={styles.mono}>{entry.ip ?? <None />}</TableCell> : null}
                  <TableCell>
                    <OutcomeBadge outcome={entry.outcome} />
                  </TableCell>
                </TableRow>
                {open ? (
                  <TableRow>
                    <TableCell colSpan={columns} className={styles.detailCell}>
                      <EntryDetails
                        entry={entry}
                        catalog={catalog}
                        people={people}
                        admin={admin}
                        onShowAll={(id) => apply({ ...filters, target: id })}
                      />
                    </TableCell>
                  </TableRow>
                ) : null}
              </Fragment>
            );
          })}
        </TableBody>
      </Table>
    );
  }

  return (
    <>
      <Toaster toasterId={toasterId} position="bottom-end" />
      <PageHeader
        crumbs={[{ label: 'Administration' }, { label: 'Audit log' }]}
        title="Audit log"
        actions={
          nothingToExport ? (
            <Tooltip content="No entries to export" relationship="description">
              {exportButton}
            </Tooltip>
          ) : (
            exportButton
          )
        }
      />
      <MessageBar intent="info">
        <MessageBarBody>
          The audit log is append-only: entries can’t be edited or deleted.{retention ? ` ${retention}` : ''}
        </MessageBarBody>
      </MessageBar>
      {referenceFailure ? (
        <ErrorBar
          error={referenceFailure}
          onRetry={() => void loadReference()}
          message="Couldn’t load the action names, the people for the filters and the retention period."
        />
      ) : null}
      <div role="search" aria-label="Filter the audit log" className={styles.filters}>
        <Field label="Time range (UTC)">
          <Dropdown
            className={styles.time}
            value={filters.range === 'custom' ? 'Custom range' : presets[filters.range].label}
            selectedOptions={[filters.range]}
            onOptionSelect={(_, data) =>
              apply({ ...filters, range: (data.optionValue as Range | undefined) ?? defaults.range })
            }
          >
            {(Object.keys(presets) as Exclude<Range, 'custom'>[]).map((key) => (
              <Option key={key} value={key}>
                {presets[key].label}
              </Option>
            ))}
            <Option value="custom">Custom range…</Option>
          </Dropdown>
        </Field>
        {filters.range === 'custom' ? (
          <>
            <Field label="From (UTC)">
              <Input
                className={styles.custom}
                type="datetime-local"
                value={filters.from}
                onChange={(_, data) => apply({ ...filters, from: data.value })}
              />
            </Field>
            <Field
              label="To (UTC)"
              validationState={problem ? 'error' : 'none'}
              {...(problem ? { validationMessage: problem } : {})}
            >
              <Input
                className={styles.custom}
                type="datetime-local"
                value={filters.to}
                onChange={(_, data) => apply({ ...filters, to: data.value })}
              />
            </Field>
          </>
        ) : null}
        <Field label="Actor">
          <PersonPicker
            className={styles.person}
            placeholder="Any actor"
            options={actorOptions}
            value={filters.actor}
            onSelect={(actor) => apply({ ...filters, actor })}
          />
        </Field>
        <Field label="Action">
          <Dropdown
            className={styles.action}
            multiselect
            placeholder="All actions"
            value={
              filters.actions.length === 0
                ? ''
                : filters.actions.length === 1
                  ? (catalog?.actions.find((a) => a.code === filters.actions[0])?.label ?? '1 action')
                  : `${filters.actions.length} actions`
            }
            selectedOptions={filters.actions}
            onOptionSelect={(_, data) => apply({ ...filters, actions: data.selectedOptions })}
          >
            {groups.map(([group, actions]) => (
              <OptionGroup key={group} label={group}>
                {actions.map((a) => (
                  <Option key={a.code} value={a.code}>
                    {a.label}
                  </Option>
                ))}
              </OptionGroup>
            ))}
          </Dropdown>
        </Field>
        <Field label="Person affected">
          <PersonPicker
            className={styles.person}
            placeholder="Anyone"
            options={targetOptions}
            value={filters.target}
            onSelect={(target) => apply({ ...filters, target })}
          />
        </Field>
        <Field
          label="Source IP"
          validationState={ipInvalid ? 'error' : 'none'}
          {...(ipInvalid ? { validationMessage: 'Use digits, the letters a to f, dots and colons.' } : {})}
        >
          <Input
            className={styles.ip}
            placeholder="e.g. 10.20.4."
            value={ipText}
            onChange={(_, data) => setIpText(data.value)}
            onBlur={commitIp}
            onKeyDown={(event) => {
              if (event.key === 'Enter') commitIp();
            }}
          />
        </Field>
        <Field label="Outcome">
          <Dropdown
            className={styles.outcome}
            value={filters.outcome || 'Any'}
            selectedOptions={[filters.outcome || 'Any']}
            onOptionSelect={(_, data) =>
              apply({ ...filters, outcome: data.optionValue === 'Any' ? '' : (data.optionValue ?? '') })
            }
          >
            {['Any', ...outcomes].map((o) => (
              <Option key={o} value={o}>
                {o}
              </Option>
            ))}
          </Dropdown>
        </Field>
        {!isDefault(filters) ? (
          <Button appearance="subtle" onClick={() => apply({ ...defaults, oldest: filters.oldest })}>
            Clear filters
          </Button>
        ) : null}
      </div>
      <div aria-live="polite" className={styles.count}>
        {page ? count(page.total, 'entry', 'entries') : ''}
      </div>
      {failure && page ? (
        <ErrorBar error={failure} onRetry={() => void loadMore()} message="Couldn’t load more entries." />
      ) : null}
      <div className={styles.region}>{body}</div>
      {page && page.entries.length > 0 ? (
        <div className={styles.footer}>
          <span aria-live="polite">
            Showing {page.entries.length.toLocaleString('en-US')} of {count(page.total, 'entry', 'entries')}
          </span>
          {page.next ? (
            <Button
              disabled={loadingMore}
              {...(loadingMore ? { icon: <Spinner size="tiny" /> } : {})}
              onClick={() => void loadMore()}
            >
              Load more
            </Button>
          ) : null}
        </div>
      ) : null}
      {exportState ? (
        <ExportDialog
          state={exportState}
          total={page?.total ?? 0}
          filters={describeFilters(filters, catalog, people, page?.entries ?? [])}
          onExport={() => void runExport()}
          onClose={closeExport}
        />
      ) : null}
    </>
  );
}

function None() {
  const styles = useStyles();
  return (
    <span className={styles.none} aria-label="None">
      —
    </span>
  );
}

function PartyName({ party }: { party: Party }) {
  const styles = useStyles();
  if (party.displayName && party.userName) {
    return (
      <span>
        {party.displayName} <span className={styles.dot}>·</span> <span className={styles.mono}>{party.userName}</span>
      </span>
    );
  }
  return party.userName ? (
    <span>
      <span className={styles.mono}>{party.userName}</span> <span className={styles.none}>(account removed)</span>
    </span>
  ) : (
    <span className={styles.none}>Removed account</span>
  );
}

function OutcomeBadge({ outcome }: { outcome: Outcome }) {
  switch (outcome) {
    case 'Failed':
      return (
        <Badge appearance="tint" color="danger" icon={<DismissCircleRegular />}>
          Failed
        </Badge>
      );
    case 'Blocked':
      return (
        <Badge appearance="tint" color="severe" icon={<LockClosedRegular />}>
          Blocked
        </Badge>
      );
    default:
      return (
        <Badge appearance="tint" color="success" icon={<CheckmarkCircleRegular />}>
          Succeeded
        </Badge>
      );
  }
}

function EntryDetails({
  entry,
  catalog,
  people,
  admin,
  onShowAll,
}: {
  entry: Entry;
  catalog: Catalog | null;
  people: Person[];
  admin: boolean;
  onShowAll: (id: string) => void;
}) {
  const styles = useStyles();
  const target = entry.target;
  const fields: [string, ReactNode][] = [
    ['Entry number', String(entry.id)],
    ['Time (UTC)', `${formatUtcSeconds(entry.at)} UTC · local ${formatLocalSeconds(entry.at)}`],
    [
      'Actor',
      [actorTypes[entry.actor.type] ?? entry.actor.type, entry.actor.type === 'User' ? personText(entry.actor) : '']
        .filter(Boolean)
        .join(' · '),
    ],
    [
      'Action',
      <>
        {entry.label} <span className={styles.code}>{entry.action}</span>
      </>,
    ],
    ['Person affected', target ? personText(target) : '—'],
    ['IP address', entry.ip ?? '—'],
    ...describeDetails(entry, catalog, people),
  ];
  return (
    <div className={styles.details}>
      <dl className={styles.dl}>
        {fields.map(([label, value]) => (
          <Fragment key={label}>
            <dt className={styles.dt}>{label}</dt>
            <dd className={styles.dd}>{value}</dd>
          </Fragment>
        ))}
      </dl>
      {target?.id ? (
        <div className={styles.detailActions}>
          {admin && !target.removed ? (
            <Link
              href={`/users?q=${encodeURIComponent(target.userName ?? '')}`}
              onClick={(event) => followLink(event, `/users?q=${encodeURIComponent(target.userName ?? '')}`)}
            >
              Show this person in Users &amp; roles
            </Link>
          ) : null}
          <Button appearance="subtle" icon={<FilterRegular />} onClick={() => onShowAll(target.id ?? '')}>
            Show all entries about {target.displayName ?? target.userName ?? 'this person'}
          </Button>
        </div>
      ) : null}
    </div>
  );
}

/** A Combobox over people (and, for the actor, System and Not signed in); typing narrows the list. */
function PersonPicker({
  className,
  placeholder,
  options,
  value,
  onSelect,
}: {
  className: string;
  placeholder: string;
  options: { key: string; text: string }[];
  value: string;
  onSelect: (key: string) => void;
}) {
  const [typed, setTyped] = useState<string | null>(null);
  const selected = options.find((o) => o.key === value);
  const shown = typed === null ? options : options.filter((o) => o.text.toLowerCase().includes(typed.toLowerCase()));
  return (
    <Combobox
      className={className}
      clearable
      placeholder={placeholder}
      value={typed ?? selected?.text ?? ''}
      selectedOptions={value ? [value] : []}
      onChange={(event) => setTyped(event.target.value)}
      onOpenChange={(_, data) => {
        if (!data.open) setTyped(null);
      }}
      onOptionSelect={(_, data) => {
        setTyped(null);
        onSelect(data.optionValue ?? '');
      }}
    >
      {shown.map((o) => (
        <Option key={o.key} value={o.key} text={o.text}>
          {o.text}
        </Option>
      ))}
    </Combobox>
  );
}

function ExportDialog({
  state,
  total,
  filters,
  onExport,
  onClose,
}: {
  state: ExportState;
  total: number;
  filters: string;
  onExport: () => void;
  onClose: () => void;
}) {
  const styles = useStyles();
  if (state.kind === 'too-many') {
    return (
      <Dialog open modalType="modal" onOpenChange={(_, data) => (data.open ? undefined : onClose())}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Too many entries to export</DialogTitle>
            <DialogContent>
              That’s {state.entries.toLocaleString('en-US')} entries. Exports are limited to{' '}
              {exportLimit.toLocaleString('en-US')}; narrow the time range or the filters.
            </DialogContent>
            <DialogActions>
              <Button onClick={onClose}>Close</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    );
  }
  const preparing = state.kind === 'preparing';
  return (
    <Dialog open modalType="modal" onOpenChange={(_, data) => (data.open ? undefined : onClose())}>
      <DialogSurface aria-busy={preparing}>
        <DialogBody>
          <DialogTitle>Export {count(total, 'entry', 'entries')}?</DialogTitle>
          <DialogContent className={styles.dialogBody}>
            {state.kind === 'failed' ? (
              <ErrorBar error={state.error} onRetry={onExport} message="Couldn’t export. Nothing was downloaded." />
            ) : null}
            <dl className={styles.dialogList}>
              <dt className={styles.dt}>Filters</dt>
              <dd className={styles.dd}>{filters}</dd>
              <dt className={styles.dt}>Columns</dt>
              <dd className={styles.dd}>
                Time (UTC), Actor type, Actor, Action, Action code, Person affected, IP address, Outcome, Details
              </dd>
            </dl>
            <MessageBar intent="info">
              <MessageBarBody>The export is recorded in the audit log.</MessageBarBody>
            </MessageBar>
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose}>Cancel</Button>
            <Button
              appearance="primary"
              disabled={preparing}
              {...(preparing ? { icon: <Spinner size="tiny" /> } : {})}
              onClick={onExport}
            >
              {preparing ? 'Preparing…' : 'Export'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
