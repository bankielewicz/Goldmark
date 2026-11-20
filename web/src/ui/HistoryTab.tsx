import {
  Avatar,
  Badge,
  Button,
  Link,
  Skeleton,
  SkeletonItem,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  AddCircleRegular,
  ArchiveRegular,
  ArrowCounterclockwiseRegular,
  ArrowSwapRegular,
  ChevronDownRegular,
  ChevronUpRegular,
  EditRegular,
  HistoryRegular,
  InfoRegular,
  LinkDismissRegular,
  LinkRegular,
  PersonRegular,
  SubtractCircleRegular,
} from '@fluentui/react-icons';
import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type ReactElement, type ReactNode } from 'react';

import { api, type ApiError } from '../api';
import { localHint, withAsOf } from '../asOf';
import { followLink } from '../router';
import { ErrorBar, ScrollBox } from './common';
import { asApiError, TypeIcon, type CiDetail, type CiReference, type CiType } from './inventory';

/**
 * Screen 5's History tab (US-43; DEC-047 items 6 to 10; the slice 7 boards `Main`, `Hist*`): the CI's changes,
 * newest first, from `GET /api/v1/cis/{id}/history`, 50 at a time. From 1100 px a timeline beside the selected
 * entry's detail; below that one column, the selected entry expanding in place. In a past view the CI's own entry in
 * force at that moment is marked "Shown above".
 */

interface HistoryActor {
  id: string | null;
  displayName: string | null;
  name: string | null;
}

interface HistoryChange {
  field: string;
  label: string;
  kind: string | null;
  change: 'added' | 'removed' | 'changed';
  before: unknown;
  after: unknown;
  attributeDeleted: boolean;
}

interface HistoryRelationship {
  id: string;
  type: string;
  direction: 'dependencies' | 'dependents';
  ci: string;
  redundancyGroup: string | null;
}

interface HistoryEntry {
  at: string;
  kind:
    'created' | 'changed' | 'retired' | 'restored' | 'relationshipAdded' | 'relationshipChanged' | 'relationshipEnded';
  by: HistoryActor | null;
  changes: HistoryChange[];
  relationship: HistoryRelationship | null;
}

interface HistoryPage {
  items: HistoryEntry[];
  total: number | null;
  next: string | null;
  references: Record<string, CiReference>;
}

const useStyles = makeStyles({
  // One column that may be narrower than its content: on a phone the diff table scrolls in its own box.
  root: { display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: tokens.spacingHorizontalL, alignItems: 'start' },
  wide: { gridTemplateColumns: 'minmax(0, 2fr) minmax(0, 3fr)' },
  card: {
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
  },
  list: { maxHeight: '70vh', overflowY: 'auto' },
  day: {
    margin: 0,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM} ${tokens.spacingVerticalXS}`,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
  },
  entries: { margin: 0, padding: 0, listStyleType: 'none' },
  entry: {
    width: '100%',
    minHeight: '56px',
    boxSizing: 'border-box',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    border: 'none',
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: 'transparent',
    color: tokens.colorNeutralForeground1,
    font: 'inherit',
    textAlign: 'left',
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
    ':focus-visible': {
      outline: `${tokens.strokeWidthThick} solid ${tokens.colorStrokeFocus2}`,
      outlineOffset: '-2px',
    },
  },
  selected: {
    backgroundColor: tokens.colorBrandBackground2,
    boxShadow: `inset 3px 0 0 ${tokens.colorBrandForeground1}`,
    ':hover': { backgroundColor: tokens.colorBrandBackground2Hover },
  },
  time: {
    flexShrink: 0,
    width: '66px',
    fontSize: '13px',
    color: tokens.colorNeutralForeground2,
    fontVariantNumeric: 'tabular-nums',
    whiteSpace: 'nowrap',
  },
  kind: { flexShrink: 0, fontSize: '16px', marginTop: '2px', color: tokens.colorNeutralForeground2 },
  summary: { flexGrow: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: '2px', overflowWrap: 'anywhere' },
  what: { fontWeight: tokens.fontWeightSemibold },
  who: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
    fontSize: tokens.fontSizeBase200,
    lineHeight: '18px',
    color: tokens.colorNeutralForeground2,
  },
  // "by Dana K" in the detail: the name in full weight.
  by: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, flexWrap: 'wrap' },
  byName: { fontWeight: tokens.fontWeightSemibold, color: tokens.colorNeutralForeground1 },
  // The timeline while it loads: a day heading, then six entries (time, icon, two lines).
  loading: { padding: tokens.spacingHorizontalM, display: 'flex', flexDirection: 'column' },
  loadingDay: { width: '110px' },
  loadingEntry: { minHeight: '56px', display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  loadingTime: { width: '58px', flexShrink: 0 },
  loadingIcon: { width: '16px', flexShrink: 0 },
  loadingText: { flexGrow: 1, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  loadingShort: { width: '40%' },
  emptyDetail: { minHeight: '240px' },
  unknown: { color: tokens.colorNeutralForeground3 },
  chevron: { flexShrink: 0, marginTop: '2px' },
  foot: {
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  more: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  detail: {
    padding: tokens.spacingHorizontalXL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  inline: {
    padding: tokens.spacingHorizontalL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    boxShadow: `inset 3px 0 0 ${tokens.colorBrandForeground1}`,
  },
  heading: {
    margin: 0,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    fontWeight: tokens.fontWeightSemibold,
  },
  meta: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
    color: tokens.colorNeutralForeground2,
  },
  tableBox: {
    // position: the visually hidden caption and "No value" texts are positioned, so without it they would lie
    // outside this box's clipping and make the whole page as wide as the table.
    position: 'relative',
    overflowX: 'auto',
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
  },
  table: { width: '100%', minWidth: '520px', borderCollapse: 'collapse' },
  fieldColumn: { width: '26%' },
  changeColumn: { width: '96px' },
  beforeColumn: { width: '24%' },
  head: { height: '36px', boxSizing: 'border-box', color: tokens.colorNeutralForeground2 },
  th: {
    textAlign: 'left',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    fontWeight: tokens.fontWeightSemibold,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke1}`,
    verticalAlign: 'top',
  },
  td: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    verticalAlign: 'top',
    overflowWrap: 'anywhere',
  },
  fieldCell: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  before: { color: tokens.colorNeutralForeground2 },
  noValue: { color: tokens.colorNeutralForeground3 },
  // Read, not shown: on screen an empty value is a dash, for a screen reader it is "No value" (the boards).
  hidden: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    overflow: 'hidden',
    clip: 'rect(0 0 0 0)',
    whiteSpace: 'nowrap',
  },
  // A long value: three lines, then "Show all" beneath (the boards).
  long: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS, alignItems: 'flex-start' },
  clamp: { display: '-webkit-box', WebkitLineClamp: '3', WebkitBoxOrient: 'vertical', overflow: 'hidden' },
  showAll: { minHeight: '24px', fontWeight: tokens.fontWeightSemibold },
  link: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalXS },
  linkType: {
    marginLeft: tokens.spacingHorizontalS,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  small: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  strong: { fontWeight: tokens.fontWeightSemibold },
  code: { fontFamily: tokens.fontFamilyMonospace, fontSize: '13px', fontWeight: tokens.fontWeightSemibold },
  grid: {
    margin: 0,
    display: 'grid',
    gridTemplateColumns: 'max-content minmax(0, 1fr)',
    gap: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
  },
  term: { fontWeight: tokens.fontWeightSemibold, color: tokens.colorNeutralForeground3 },
  value: { margin: 0, display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  muted: { color: tokens.colorNeutralForeground3 },
  note: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    alignItems: 'flex-start',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground3,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
  },
});

const pageSize = 50;
const dayFormat = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' });
const icons: Record<HistoryEntry['kind'] | 'removed', ReactElement> = {
  created: <AddCircleRegular />,
  changed: <EditRegular />,
  removed: <SubtractCircleRegular />,
  retired: <ArchiveRegular />,
  restored: <ArrowCounterclockwiseRegular />,
  relationshipAdded: <LinkRegular />,
  relationshipChanged: <ArrowSwapRegular />,
  relationshipEnded: <LinkDismissRegular />,
};

const key = (entry: HistoryEntry) => `${entry.at}|${entry.kind}|${entry.relationship?.id ?? ''}`;
const utc = (at: string) => {
  const text = new Date(at).toISOString();
  return { date: text.slice(0, 10), time: text.slice(11, 16) };
};
/** Only a value that left with its attribute (DEC-047 item 6). */
const attributeRemoval = (entry: HistoryEntry) =>
  entry.kind === 'changed' && entry.changes.length > 0 && entry.changes.every((c) => c.attributeDeleted);

function whoName(by: HistoryActor | null): string {
  if (!by) return 'Unknown';
  if (by.displayName) return by.displayName;
  return by.id ? 'Former user' : (by.name ?? 'Unknown');
}

function otherName(entry: HistoryEntry, references: Record<string, CiReference>): string {
  const id = entry.relationship?.ci ?? '';
  return references[id]?.name ?? id;
}

/** The entry's one line (the handoff's summaries). */
function summary(entry: HistoryEntry, references: Record<string, CiReference>): string {
  const relationship = entry.relationship;
  if (relationship) {
    const other = otherName(entry, references);
    const what =
      relationship.direction === 'dependencies'
        ? `${relationship.type} ${other}`
        : `depended on by ${other} (${relationship.type})`;
    switch (entry.kind) {
      case 'relationshipAdded':
        return `Added relationship: ${what}`;
      case 'relationshipEnded':
        return `Ended relationship: ${what}`;
      default:
        return relationship.direction === 'dependencies'
          ? `Changed redundancy group on ${what}`
          : `Changed redundancy group: ${what}`;
    }
  }
  switch (entry.kind) {
    case 'created':
      return 'Created';
    case 'retired':
      return 'Retired';
    case 'restored':
      return 'Restored';
    default: {
      if (attributeRemoval(entry)) {
        const [first] = entry.changes;
        return entry.changes.length === 1 && first
          ? `Removed ${first.label} (attribute deleted)`
          : `Removed ${entry.changes.length} attributes (attributes deleted)`;
      }
      // One field named, more counted: "Changed Name and 2 attributes", "Changed Rack".
      const name = entry.changes.find((c) => c.field === 'name');
      const others = entry.changes.filter((c) => c.field !== 'name');
      if (name) {
        const [only] = others;
        return others.length === 0
          ? 'Changed Name'
          : others.length === 1 && only
            ? `Changed Name and ${only.label}`
            : `Changed Name and ${others.length} attributes`;
      }
      const [a, b] = others;
      if (others.length === 1 && a) return `Changed ${a.label}`;
      if (others.length === 2 && a && b) return `Changed ${a.label} and ${b.label}`;
      return `Changed ${others.length} attributes`;
    }
  }
}

/**
 * Who made a change: initials and the name; in the timeline a former user gets the person icon, in the detail the
 * name alone, in full weight (the boards). Nobody on record reads "Unknown".
 */
function Who({ by, detail = false }: { by: HistoryActor | null; detail?: boolean }) {
  const styles = useStyles();
  const name = whoName(by);
  if (!by) return <span className={styles.unknown}>Unknown</span>;
  return (
    <span className={detail ? styles.by : styles.who}>
      {by.displayName ? (
        <Avatar size={20} name={by.displayName} aria-hidden />
      ) : by.id && !detail ? (
        <Avatar size={20} icon={<PersonRegular />} aria-hidden />
      ) : null}
      <span className={detail ? styles.byName : undefined}>{name}</span>
    </span>
  );
}

export function HistoryTab({
  ci,
  asOf,
  wide,
  types,
}: {
  ci: CiDetail;
  asOf: string | null;
  wide: boolean;
  types: CiType[] | null;
}) {
  const styles = useStyles();
  const [items, setItems] = useState<HistoryEntry[] | null>(null);
  const [references, setReferences] = useState<Record<string, CiReference>>({});
  const [total, setTotal] = useState(0);
  const [next, setNext] = useState<string | null>(null);
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [moreBusy, setMoreBusy] = useState(false);
  const [moreFailure, setMoreFailure] = useState<ApiError | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const request = useRef(0);

  const load = useCallback(async () => {
    const current = ++request.current;
    setFailure(null);
    setMoreFailure(null);
    try {
      const { data } = await api<HistoryPage>('GET', `/api/v1/cis/${ci.id}/history`);
      if (request.current !== current) return;
      setItems(data.items);
      setReferences(data.references);
      setTotal(data.total ?? data.items.length);
      setNext(data.next);
      // As the tab opens, the newest entry is selected (the HistNewest board).
      setSelected((shown) =>
        shown && data.items.some((e) => key(e) === shown) ? shown : data.items[0] ? key(data.items[0]) : null,
      );
    } catch (error) {
      if (request.current === current) setFailure(asApiError(error));
    }
  }, [ci.id]);

  useEffect(() => {
    setItems(null);
    void load();
  }, [load]);

  const more = async () => {
    if (!next || moreBusy) return;
    const current = request.current;
    setMoreBusy(true);
    setMoreFailure(null);
    try {
      const { data } = await api<HistoryPage>(
        'GET',
        `/api/v1/cis/${ci.id}/history?limit=${pageSize}&cursor=${encodeURIComponent(next)}`,
      );
      if (request.current !== current) return;
      setItems((shown) => [...(shown ?? []), ...data.items]);
      setReferences((shown) => ({ ...shown, ...data.references }));
      setNext(data.next);
    } catch (error) {
      setMoreFailure(asApiError(error));
    } finally {
      setMoreBusy(false);
    }
  };

  // Up and Down move between the entries; Enter or Space selects (the Notes board's keyboard column).
  const onListKey = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
    const buttons = Array.from(listRef.current?.querySelectorAll<HTMLButtonElement>('button[data-entry]') ?? []);
    const at = buttons.indexOf(document.activeElement as HTMLButtonElement);
    if (at < 0) return;
    event.preventDefault();
    buttons[Math.max(0, Math.min(buttons.length - 1, at + (event.key === 'ArrowDown' ? 1 : -1)))]?.focus();
  };

  if (failure && !items) {
    return (
      <ErrorBar
        error={failure}
        message={
          failure.status === 0
            ? 'Couldn’t load the history. Check your connection, then try again.'
            : 'Couldn’t load the history: something went wrong on the server.'
        }
        onRetry={() => void load()}
      />
    );
  }

  if (!items) {
    return (
      <div aria-busy="true" className={mergeClasses(styles.root, wide && styles.wide)}>
        <div role="group" aria-label="Timeline" className={styles.card}>
          <Skeleton aria-label="Loading the history" className={styles.loading}>
            <SkeletonItem size={16} className={styles.loadingDay} />
            {Array.from({ length: 6 }, (_, i) => (
              <div key={i} className={styles.loadingEntry}>
                <SkeletonItem size={16} className={styles.loadingTime} />
                <SkeletonItem size={16} shape="square" className={styles.loadingIcon} />
                <div className={styles.loadingText}>
                  <SkeletonItem size={16} />
                  <SkeletonItem size={12} className={styles.loadingShort} />
                </div>
              </div>
            ))}
          </Skeleton>
        </div>
        {wide ? <div className={mergeClasses(styles.card, styles.detail, styles.emptyDetail)} /> : null}
      </div>
    );
  }

  // In a past view, the CI's own entry in force at that moment (Notes: "Shown above").
  const shownAbove = asOf
    ? items.find((e) => !e.relationship && new Date(e.at).getTime() <= new Date(asOf).getTime())
    : undefined;
  const current = items.find((e) => key(e) === selected) ?? items[0];
  const days: { label: string; entries: HistoryEntry[] }[] = [];
  for (const entry of items) {
    const label = dayFormat.format(new Date(entry.at));
    const last = days[days.length - 1];
    if (last && last.label === label) last.entries.push(entry);
    else days.push({ label, entries: [entry] });
  }
  const countLine = next
    ? `Showing ${items.length.toLocaleString('en-US')} of ${total.toLocaleString('en-US')} changes`
    : `${total.toLocaleString('en-US')} ${total === 1 ? 'change' : 'changes'}`;

  const detail = (entry: HistoryEntry, inline: boolean) => (
    <Detail
      entry={entry}
      ci={ci}
      references={references}
      types={types}
      className={inline ? styles.inline : mergeClasses(styles.card, styles.detail)}
    />
  );

  return (
    <div className={mergeClasses(styles.root, wide && styles.wide)}>
      <div
        ref={listRef}
        role="group"
        aria-label="Timeline"
        className={mergeClasses(styles.card, wide && styles.list)}
        onKeyDown={onListKey}
      >
        {days.map((day) => (
          <div key={day.label}>
            <h2 className={styles.day}>{day.label}</h2>
            {/* role="list": a list without markers stays a list for VoiceOver. */}
            <ul role="list" className={styles.entries}>
              {day.entries.map((entry) => {
                const on = current !== undefined && key(entry) === key(current);
                const { date, time } = utc(entry.at);
                return (
                  <li key={key(entry)}>
                    <button
                      type="button"
                      data-entry
                      className={mergeClasses(styles.entry, on && styles.selected)}
                      {...(wide ? { 'aria-current': on ? true : undefined } : { 'aria-expanded': on })}
                      onClick={() => setSelected(key(entry))}
                    >
                      <time
                        dateTime={entry.at}
                        title={`${date} ${time} UTC · ${localHint(entry.at)}`}
                        className={styles.time}
                      >
                        {time} UTC
                      </time>
                      <span className={styles.kind} aria-hidden>
                        {attributeRemoval(entry) ? icons.removed : icons[entry.kind]}
                      </span>
                      <span className={styles.summary}>
                        <span className={styles.what}>{summary(entry, references)}</span>
                        <span className={styles.who}>
                          <Who by={entry.by} />
                          {shownAbove && key(shownAbove) === key(entry) ? (
                            <Badge appearance="tint" color="subtle">
                              Shown above
                            </Badge>
                          ) : null}
                        </span>
                      </span>
                      {wide ? null : on ? (
                        <ChevronUpRegular aria-hidden className={styles.chevron} />
                      ) : (
                        <ChevronDownRegular aria-hidden className={styles.chevron} />
                      )}
                    </button>
                    {!wide && on ? detail(entry, true) : null}
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
        <div className={styles.foot}>
          {moreFailure ? (
            <ErrorBar
              error={moreFailure}
              message="Couldn’t load more changes. The ones above are still here."
              onRetry={() => void more()}
            />
          ) : null}
          <div className={styles.more}>
            {next && !moreFailure ? (
              <Button disabledFocusable={moreBusy} onClick={() => void more()}>
                {moreBusy ? <Spinner size="tiny" aria-hidden /> : null}
                {moreBusy ? `Loading ${pageSize} more…` : `Show ${pageSize} more`}
              </Button>
            ) : null}
            <Text aria-live="polite" className={styles.muted}>
              {countLine}
            </Text>
          </div>
        </div>
      </div>
      {wide && current ? detail(current, false) : null}
    </div>
  );
}

function Detail({
  entry,
  ci,
  references,
  types,
  className,
}: {
  entry: HistoryEntry;
  ci: CiDetail;
  references: Record<string, CiReference>;
  types: CiType[] | null;
  className: string;
}) {
  const styles = useStyles();
  const { date, time } = utc(entry.at);
  const headingId = `history-detail-${entry.at.replace(/[^0-9]/g, '')}`;
  const viewPath = withAsOf(`/ci/${ci.id}`, entry.at);
  const name = whoName(entry.by);
  const relationship = entry.relationship;
  return (
    <section role="region" aria-labelledby={headingId} aria-live="polite" className={className}>
      <div>
        <h2 id={headingId} className={styles.heading}>
          Change on {date} {time} UTC
        </h2>
        <div className={styles.meta}>
          <span>{localHint(entry.at)}</span>
          <span aria-hidden>·</span>
          <span className={styles.by}>
            by <Who by={entry.by} detail />
          </span>
        </div>
      </div>
      <div>
        <Button as="a" href={viewPath} icon={<HistoryRegular />} onClick={(event) => followLink(event, viewPath)}>
          View this CI as of this time
        </Button>
      </div>
      {entry.kind === 'retired' || entry.kind === 'restored' ? (
        <Text as="p" size={400}>
          {entry.by ? `${name} ${entry.kind} this CI.` : `This CI was ${entry.kind}.`}
        </Text>
      ) : null}
      {attributeRemoval(entry) ? (
        <Text as="p">
          {`${entry.by ? name : 'Someone'} deleted the attribute ${entry.changes.map((c) => c.label).join(', ')} from the ${ci.type} type, so its value left this CI.`}
        </Text>
      ) : null}
      {relationship ? (
        <RelationshipDetail entry={entry} relationship={relationship} references={references} types={types} />
      ) : entry.changes.length > 0 ? (
        <ScrollBox className={styles.tableBox} label="What changed, as a table">
          <table className={styles.table}>
            <caption className={styles.hidden}>
              What changed on {date} at {time} UTC
            </caption>
            <colgroup>
              <col className={styles.fieldColumn} />
              <col className={styles.changeColumn} />
              <col className={styles.beforeColumn} />
              <col />
            </colgroup>
            <thead>
              <tr>
                {['Field', 'Change', 'Before', 'After'].map((column) => (
                  <th key={column} scope="col" className={mergeClasses(styles.th, styles.head)}>
                    {column}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {entry.changes.map((change) => (
                <tr key={change.field}>
                  <th scope="row" className={mergeClasses(styles.th, styles.td)}>
                    <span className={styles.fieldCell}>
                      {change.label}
                      {change.attributeDeleted ? (
                        <Badge appearance="tint" color="subtle">
                          Attribute deleted
                        </Badge>
                      ) : null}
                    </span>
                  </th>
                  <td className={styles.td}>
                    <Badge appearance="tint" color="subtle">
                      {change.change === 'added' ? 'Added' : change.change === 'removed' ? 'Removed' : 'Changed'}
                    </Badge>
                  </td>
                  <td className={mergeClasses(styles.td, styles.before)}>
                    <Value change={change} value={change.before} references={references} types={types} struck />
                  </td>
                  <td className={styles.td}>
                    <Value change={change} value={change.after} references={references} types={types} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </ScrollBox>
      ) : null}
      {entry.kind === 'relationshipEnded' && !entry.by ? (
        <div role="note" className={styles.note}>
          <InfoRegular aria-hidden />
          <span>The audit entry that recorded who ended it is no longer kept.</span>
        </div>
      ) : null}
    </section>
  );
}

function RelationshipDetail({
  entry,
  relationship,
  references,
  types,
}: {
  entry: HistoryEntry;
  relationship: HistoryRelationship;
  references: Record<string, CiReference>;
  types: CiType[] | null;
}) {
  const styles = useStyles();
  const other = references[relationship.ci];
  const otherName = other?.name ?? relationship.ci;
  const group = entry.changes.find((c) => c.field === 'redundancyGroup');
  const text = (value: unknown) => (typeof value === 'string' && value ? value : '—');
  const change =
    entry.kind === 'relationshipAdded' ? 'Added' : entry.kind === 'relationshipEnded' ? 'Ended' : 'Changed';
  return (
    <>
      <dl className={styles.grid}>
        <dt className={styles.term}>Change</dt>
        <dd className={styles.value}>
          <Badge appearance="tint" color="subtle">
            {change}
          </Badge>
        </dd>
        <dt className={styles.term}>Relationship</dt>
        <dd className={styles.value}>
          <code className={styles.code}>{relationship.type}</code>
        </dd>
        <dt className={styles.term}>Direction</dt>
        <dd className={styles.value}>
          {relationship.direction === 'dependencies'
            ? `This CI depends on ${otherName}`
            : `${otherName} depends on this CI`}
        </dd>
        <dt className={styles.term}>Redundancy group</dt>
        <dd className={styles.value}>
          {group ? (
            <>
              <span className={styles.small}>Before</span>
              <span>{text(group.before)}</span>
              <span aria-hidden>→</span>
              <span className={styles.small}>After</span>
              <span className={styles.strong}>{text(group.after)}</span>
            </>
          ) : (
            <span className={relationship.redundancyGroup ? undefined : styles.muted}>
              {text(relationship.redundancyGroup)}
            </span>
          )}
        </dd>
        <dt className={styles.term}>Other CI</dt>
        <dd className={styles.value}>
          <Link
            href={`/ci/${relationship.ci}`}
            className={styles.link}
            onClick={(event) => followLink(event, `/ci/${relationship.ci}`)}
          >
            {other ? <TypeIcon type={other.type} types={types} /> : null}
            {otherName}
          </Link>
          {other ? <span className={styles.small}>{other.type}</span> : null}
          {other?.lifecycleState === 'Retired' ? (
            <Badge appearance="tint" color="subtle">
              Retired
            </Badge>
          ) : null}
        </dd>
      </dl>
      <Text size={200} className={styles.muted}>
        The link opens the other CI as it is now.
      </Text>
    </>
  );
}

function Value({
  change,
  value,
  references,
  types,
  struck = false,
}: {
  change: HistoryChange;
  value: unknown;
  references: Record<string, CiReference>;
  types: CiType[] | null;
  struck?: boolean;
}): ReactNode {
  const styles = useStyles();
  if (value === null || value === undefined || value === '') {
    // A dash on screen; "No value" for a screen reader, never a bare dash (the boards, and the Notes board's
    // keyboard and screen reader column).
    return (
      <span className={styles.noValue}>
        <span aria-hidden>—</span>
        <span className={styles.hidden}>No value</span>
      </span>
    );
  }
  const reference = change.kind === 'ciRef' && typeof value === 'string' ? references[value] : undefined;
  if (reference) {
    const link = (
      <span>
        <Link
          href={`/ci/${reference.id}`}
          className={styles.link}
          onClick={(event) => followLink(event, `/ci/${reference.id}`)}
        >
          <TypeIcon type={reference.type} types={types} />
          {reference.name}
        </Link>
        <span className={styles.linkType}>{reference.type}</span>
      </span>
    );
    return struck ? <s>{link}</s> : link;
  }
  const text =
    change.kind === 'bool'
      ? value === true
        ? 'Yes'
        : 'No'
      : typeof value === 'string'
        ? value
        : JSON.stringify(value);
  return <LongText text={text} struck={struck} />;
}

/**
 * A value as text: three lines of it, and when there is more, "Show all" beneath (the boards); "Show less" folds it
 * again (the boards draw only the folded state).
 */
function LongText({ text, struck }: { text: string; struck: boolean }) {
  const styles = useStyles();
  const [all, setAll] = useState(false);
  const [more, setMore] = useState(false);
  const box = useRef<HTMLSpanElement>(null);
  useEffect(() => {
    const element = box.current;
    if (!element || all) return undefined;
    // Is there more than the three lines show? Asked when the value is drawn and whenever its column's width changes.
    const observer = new ResizeObserver(() => setMore(element.scrollHeight > element.clientHeight + 1));
    observer.observe(element);
    return () => observer.disconnect();
  }, [all, text]);
  return (
    <span className={styles.long}>
      <span ref={box} className={all ? undefined : styles.clamp}>
        {struck ? <s>{text}</s> : text}
      </span>
      {more ? (
        <Link as="button" className={styles.showAll} aria-expanded={all} onClick={() => setAll((open) => !open)}>
          {all ? 'Show less' : 'Show all'}
        </Link>
      ) : null}
    </span>
  );
}
