import {
  Button,
  Checkbox,
  DrawerBody,
  DrawerHeader,
  DrawerHeaderTitle,
  Link,
  OverlayDrawer,
  SearchBox,
  Skeleton,
  SkeletonItem,
  Spinner,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  AddRegular,
  ChevronDownRegular,
  ChevronLeftRegular,
  CubeRegular,
  DismissRegular,
  FilterRegular,
  SearchRegular,
} from '@fluentui/react-icons';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { api, ApiError } from '../api';
import { formatAgo, formatUtc } from '../format';
import { followLink, navigate, useLocation } from '../router';
import { useSession } from '../session';
import { useWide } from '../shell/AppShell';
import { PageHeader } from '../shell/PageHeader';
import { CiPanel } from '../ui/CiPanel';
import { ErrorBar } from '../ui/common';
import {
  asApiError,
  canEditCis,
  categories,
  LifecycleBadge,
  TypeIcon,
  useCiTypes,
  type CiPageData,
  type CiSummary,
  type CiType,
} from '../ui/inventory';

const useStyles = makeStyles({
  search: { maxWidth: '720px', width: '100%' },
  searchInput: { fontSize: tokens.fontSizeBase400 },
  count: { fontWeight: tokens.fontWeightSemibold, minHeight: tokens.lineHeightBase300 },
  layout: { display: 'flex', gap: tokens.spacingHorizontalL, alignItems: 'flex-start', minWidth: 0 },
  filters: {
    width: '260px',
    flexShrink: 0,
    boxSizing: 'border-box',
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM} ${tokens.spacingVerticalL}`,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  filtersHeader: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS },
  filtersTitle: {
    margin: 0,
    flexGrow: 1,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
  },
  fieldset: {
    margin: 0,
    padding: 0,
    border: 'none',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  legend: { padding: `0 0 ${tokens.spacingVerticalXS}`, fontWeight: tokens.fontWeightSemibold },
  category: {
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalXS,
  },
  typeRow: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: tokens.spacingHorizontalS },
  typeCount: { color: tokens.colorNeutralForeground3, fontVariantNumeric: 'tabular-nums' },
  results: { flexGrow: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  tableBox: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    overflowX: 'auto',
  },
  table: { minWidth: '560px' },
  row: { cursor: 'pointer' },
  retired: { color: tokens.colorNeutralForeground3 },
  name: { fontWeight: tokens.fontWeightSemibold },
  typeCell: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  empty: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalM,
    padding: `${tokens.spacingVerticalXXXL} ${tokens.spacingHorizontalXL}`,
    textAlign: 'center',
  },
  emptyIcon: { fontSize: '40px', color: tokens.colorNeutralForeground3 },
  emptyTitle: {
    margin: 0,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    fontWeight: tokens.fontWeightSemibold,
  },
  emptyBox: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
  },
  emptyActions: { display: 'flex', gap: tokens.spacingHorizontalS },
  toolbar: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  countSkeleton: { width: '120px' },
  typeCountSkeleton: { width: '28px' },
});

type Sort = 'name' | '-name' | 'type' | '-type' | 'changed' | '-changed';

interface Query {
  q: string;
  types: string[];
  retired: boolean;
  sort: Sort;
}

const sorts: Sort[] = ['name', '-name', 'type', '-type', 'changed', '-changed'];

/** The whole view lives in the page address (US-53 `/search?q=`), so it can be shared; slice 20's saved filters keep it (#45). */
function readQuery(search: URLSearchParams): Query {
  const sort = search.get('sort') as Sort | null;
  return {
    q: search.get('q') ?? '',
    types: search.getAll('type'),
    retired: search.get('retired') === 'true',
    sort: sort && sorts.includes(sort) ? sort : 'name',
  };
}

function pageAddress(query: Query): string {
  const params = new URLSearchParams();
  if (query.q.trim()) params.set('q', query.q.trim());
  query.types.forEach((t) => params.append('type', t));
  if (query.retired) params.set('retired', 'true');
  if (query.sort !== 'name') params.set('sort', query.sort);
  const text = params.toString();
  return `/search${text ? `?${text}` : ''}`;
}

/** The API's query: one record for the list (and, in slice 20, the CSV export: #47). */
function apiQuery(query: Query): URLSearchParams {
  const params = new URLSearchParams({ limit: '100', sort: query.sort });
  if (query.q.trim()) params.set('q', query.q.trim());
  query.types.forEach((t) => params.append('type', t));
  if (query.retired) params.set('retired', 'true');
  return params;
}

interface Column {
  id: 'name' | 'type' | 'lifecycle' | 'changed';
  label: string;
  sort: 'name' | 'type' | 'changed' | null;
  /** Kept below 1100 px only if `narrow` is true. */
  narrow: boolean;
  render: (ci: CiSummary, types: CiType[] | null, styles: ReturnType<typeof useStyles>) => ReactNode;
}

/** The list's columns, as data: slice 20's column chooser (#46) adds visibility to this array. */
const columns: Column[] = [
  {
    id: 'name',
    label: 'Name',
    sort: 'name',
    narrow: true,
    render: (ci, _types, styles) => (
      <Link
        id={`ci-link-${ci.id}`}
        href={`/ci/${ci.id}`}
        className={styles.name}
        onClick={(event) => followLink(event, `/ci/${ci.id}`)}
      >
        {ci.name}
      </Link>
    ),
  },
  {
    id: 'type',
    label: 'Type',
    sort: 'type',
    narrow: true,
    render: (ci, types, styles) => (
      <span className={styles.typeCell}>
        <TypeIcon type={ci.type} types={types} />
        {ci.type}
      </span>
    ),
  },
  {
    id: 'lifecycle',
    label: 'Lifecycle',
    sort: null,
    narrow: true,
    render: (ci) => <LifecycleBadge state={ci.lifecycleState} />,
  },
  {
    id: 'changed',
    label: 'Changed',
    sort: 'changed',
    narrow: false,
    render: (ci) => (
      <Tooltip content={`${formatUtc(ci.changedAt)} UTC`} relationship="description">
        <span>{formatAgo(ci.changedAt)}</span>
      </Tooltip>
    ),
  },
];

/**
 * Screen 4, CI search / list (US-11, US-53 `/search?q=`; DEC-044 items 1, 2 and 6): CIs whose name
 * contains the search text in any case, narrowed by type, with retired CIs only when asked, sorted, and
 * paged with "Load more". Admins and Editors start a new CI here. Built on the approved boards
 * (`docs/design/screens/slice-5/`).
 */
export function SearchPage() {
  const styles = useStyles();
  const session = useSession();
  const canEdit = canEditCis(session.user);
  const { search } = useLocation();
  const searchText = search.toString();
  const query = useMemo(() => readQuery(new URLSearchParams(searchText)), [searchText]);
  const wide = useWide();
  const { types, failure: typesFailure, reload: reloadTypes } = useCiTypes();
  const [page, setPage] = useState<CiPageData | null>(null);
  const [counts, setCounts] = useState<CiPageData['types']>(null);
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [moreFailure, setMoreFailure] = useState<ApiError | null>(null);
  const [typed, setTyped] = useState(query.q);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [showAllTypes, setShowAllTypes] = useState(false);
  // The filter panel collapsed at 1100 px and wider; the toolbar's Filters button brings it back.
  const [filtersHidden, setFiltersHidden] = useState(false);
  const [creating, setCreating] = useState(false);
  // No CI at all, rather than none that isn't retired: asked only when the list is empty.
  const [anyCi, setAnyCi] = useState<boolean | null>(null);
  const shownQuery = useRef('');
  const listRequest = useRef(0);
  const [focusRow, setFocusRow] = useState<string | null>(null);

  const apply = useCallback((next: Query) => navigate(pageAddress(next), { replace: true }), []);

  const load = useCallback(async () => {
    // The types first: a link may name a type this Goldmark doesn't have, which the list ignores.
    if (types === null && typesFailure === null) return;
    const request = ++listRequest.current;
    setFailure(null);
    setMoreFailure(null);
    setLoadingMore(false);
    const known = types
      ? query.types.filter((t) => types.some((x) => x.name.toLowerCase() === t.toLowerCase()))
      : query.types;
    const text = apiQuery({ ...query, types: known }).toString();
    shownQuery.current = text;
    try {
      const { data } = await api<CiPageData>('GET', `/api/v1/cis?${text}`);
      if (listRequest.current !== request) return;
      setPage(data);
      setCounts(data.types);
      if (data.total === 0 && !query.q.trim() && query.types.length === 0 && !query.retired) {
        const { data: retired } = await api<CiPageData>('GET', '/api/v1/cis?retired=true&limit=1');
        if (listRequest.current === request) setAnyCi((retired.total ?? 0) > 0);
      } else {
        setAnyCi(true);
      }
    } catch (error) {
      // The last list stays under the error (the board), so only the failure is set.
      if (listRequest.current === request) setFailure(asApiError(error));
    }
  }, [query, types, typesFailure]);

  useEffect(() => {
    void load();
  }, [load]);

  // The address changed (Back, a shared link): the box follows it, keeping a space still being typed.
  useEffect(() => setTyped((current) => (current.trim() === query.q.trim() ? current : query.q)), [query.q]);

  // The list updates when typing pauses.
  useEffect(() => {
    if (typed.trim() === query.q.trim()) return undefined;
    const timer = window.setTimeout(() => apply({ ...query, q: typed }), 300);
    return () => window.clearTimeout(timer);
  }, [typed, query, apply]);

  useEffect(() => {
    if (focusRow) {
      document.getElementById(`ci-link-${focusRow}`)?.focus();
      setFocusRow(null);
    }
  }, [focusRow, page]);

  const loadMore = async () => {
    if (!page?.next) return;
    const request = listRequest.current;
    setLoadingMore(true);
    setMoreFailure(null);
    try {
      const { data } = await api<CiPageData>(
        'GET',
        `/api/v1/cis?${shownQuery.current}&cursor=${encodeURIComponent(page.next)}`,
      );
      if (listRequest.current !== request) return;
      setPage((shown) => shown && { ...shown, items: [...shown.items, ...data.items], next: data.next });
      setFocusRow(data.items[0]?.id ?? null);
    } catch (error) {
      if (listRequest.current === request) setMoreFailure(asApiError(error));
    } finally {
      if (listRequest.current === request) setLoadingMore(false);
    }
  };

  const toggleType = (name: string, checked: boolean) =>
    apply({ ...query, types: checked ? [...query.types, name] : query.types.filter((t) => t !== name) });

  const clearFilters = () => apply({ ...query, types: [] });

  const sortBy = (column: 'name' | 'type' | 'changed') =>
    apply({ ...query, sort: query.sort === column ? (`-${column}` as Sort) : column });

  const loading = page === null && failure === null;
  const empty = page !== null && anyCi === false;
  const total = page?.total ?? 0;
  const countText =
    page === null
      ? ''
      : `${total.toLocaleString('en-US')} ${total === 1 ? 'CI' : 'CIs'}${query.q.trim() ? ` match ‘${query.q.trim()}’` : ''}${query.retired ? ', including retired' : ''}`;
  const filterCount = query.types.length;
  const shownColumns = columns.filter((c) => wide || c.narrow);

  const filterPanel = (inDrawer: boolean) => (
    <TypeFilter
      types={types}
      counts={counts}
      ticked={query.types}
      loading={loading}
      showAll={showAllTypes}
      onShowAll={() => setShowAllTypes(true)}
      onToggle={toggleType}
      onClear={clearFilters}
      onHide={() => setFiltersHidden(true)}
      inDrawer={inDrawer}
    />
  );

  return (
    <>
      <PageHeader
        crumbs={[{ label: 'Inventory' }, { label: 'Search' }]}
        title="Search"
        actions={
          empty ? undefined : (
            <div className={styles.toolbar}>
              {wide && !filtersHidden ? null : (
                <Button
                  icon={<FilterRegular />}
                  {...(wide ? { 'aria-expanded': false } : { 'aria-haspopup': 'dialog' as const })}
                  onClick={() => (wide ? setFiltersHidden(false) : setDrawerOpen(true))}
                >
                  {filterCount ? `Filters (${filterCount})` : 'Filters'}
                </Button>
              )}
              <Switch
                label="Show retired"
                checked={query.retired}
                onChange={(_, data) => apply({ ...query, retired: data.checked })}
              />
              {canEdit ? (
                <Button
                  appearance="primary"
                  icon={<AddRegular />}
                  aria-haspopup="dialog"
                  disabled={!types}
                  onClick={() => setCreating(true)}
                >
                  New CI
                </Button>
              ) : null}
            </div>
          )
        }
      />

      {typesFailure ? <ErrorBar error={typesFailure} onRetry={reloadTypes} /> : null}

      {empty ? (
        <section className={mergeClasses(styles.empty, styles.emptyBox)} aria-labelledby="no-cis">
          <CubeRegular className={styles.emptyIcon} aria-hidden />
          <h2 id="no-cis" className={styles.emptyTitle}>
            There are no CIs yet
          </h2>
          <Text>{canEdit ? 'Add the first one.' : 'An Editor or Admin can add them.'}</Text>
          {canEdit ? (
            <Button
              appearance="primary"
              icon={<AddRegular />}
              aria-haspopup="dialog"
              disabled={!types}
              onClick={() => setCreating(true)}
            >
              New CI
            </Button>
          ) : null}
        </section>
      ) : (
        <>
          <div role="search" aria-label="Search CIs" className={styles.search}>
            <SearchBox
              size="large"
              className={styles.search}
              input={{ className: styles.searchInput, 'aria-label': 'Search by name' }}
              placeholder="Search by name"
              value={typed}
              onChange={(_, data) => setTyped(data.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') apply({ ...query, q: typed });
              }}
            />
          </div>
          {failure ? (
            <ErrorBar
              error={failure}
              message={
                failure.status === 0
                  ? 'Couldn’t load the CIs. Check your connection, then try again.'
                  : 'Couldn’t load the CIs: something went wrong on the server.'
              }
              onRetry={() => void load()}
            />
          ) : null}
          <div aria-live="polite" className={styles.count}>
            {loading ? (
              <Skeleton aria-label="Loading">
                <SkeletonItem size={16} className={styles.countSkeleton} />
              </Skeleton>
            ) : (
              countText
            )}
          </div>

          <div className={styles.layout}>
            {wide && !filtersHidden ? filterPanel(false) : null}
            <div className={styles.results} aria-busy={loading}>
              <div className={styles.tableBox}>
                {page === null && failure !== null ? null : page && page.items.length === 0 ? (
                  <NoResults
                    q={query.q.trim()}
                    filtered={filterCount > 0}
                    onClear={clearFilters}
                    onAllTypes={clearFilters}
                    ticked={query.types}
                  />
                ) : (
                  <Table className={styles.table} aria-label="CIs" sortable>
                    <TableHeader>
                      <TableRow>
                        {shownColumns.map((column) => (
                          <TableHeaderCell
                            key={column.id}
                            {...(column.sort ? { onClick: () => sortBy(column.sort ?? 'name') } : {})}
                            {...(column.sort && query.sort === column.sort
                              ? { sortDirection: 'ascending' as const }
                              : {})}
                            {...(column.sort && query.sort === `-${column.sort}`
                              ? { sortDirection: 'descending' as const }
                              : {})}
                          >
                            {column.label}
                          </TableHeaderCell>
                        ))}
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {loading || page === null
                        ? Array.from({ length: 10 }, (_, i) => (
                            <TableRow key={i}>
                              {shownColumns.map((column) => (
                                <TableCell key={column.id}>
                                  <Skeleton aria-label="Loading">
                                    <SkeletonItem size={12} />
                                  </Skeleton>
                                </TableCell>
                              ))}
                            </TableRow>
                          ))
                        : page.items.map((ci) => (
                            <TableRow
                              key={ci.id}
                              className={mergeClasses(styles.row, ci.lifecycleState === 'Retired' && styles.retired)}
                              onClick={(event) => {
                                if ((event.target as HTMLElement).closest('a')) return;
                                navigate(`/ci/${ci.id}`);
                              }}
                            >
                              {shownColumns.map((column) => (
                                <TableCell key={column.id}>{column.render(ci, types, styles)}</TableCell>
                              ))}
                            </TableRow>
                          ))}
                    </TableBody>
                  </Table>
                )}
              </div>
              {moreFailure ? <ErrorBar error={moreFailure} onRetry={() => void loadMore()} /> : null}
              {page && page.items.length > 0 ? (
                <div className={styles.footer}>
                  <Text>
                    {page.next
                      ? `Showing 1–${page.items.length.toLocaleString('en-US')} of ${total.toLocaleString('en-US')}`
                      : `Showing all ${page.items.length.toLocaleString('en-US')}`}
                  </Text>
                  {page.next ? (
                    <Button disabled={loadingMore} onClick={() => void loadMore()}>
                      {loadingMore ? <Spinner size="tiny" aria-label="Loading more" /> : null}
                      Load more
                    </Button>
                  ) : null}
                </div>
              ) : null}
            </div>
          </div>
        </>
      )}

      {wide ? null : (
        <OverlayDrawer
          open={drawerOpen}
          position="start"
          aria-labelledby="filters-drawer-title"
          onOpenChange={(_, data) => setDrawerOpen(data.open)}
        >
          <DrawerHeader>
            <DrawerHeaderTitle
              heading={{ id: 'filters-drawer-title' }}
              action={
                <Button
                  appearance="subtle"
                  aria-label="Close"
                  icon={<DismissRegular />}
                  onClick={() => setDrawerOpen(false)}
                />
              }
            >
              Filters
            </DrawerHeaderTitle>
          </DrawerHeader>
          <DrawerBody>{filterPanel(true)}</DrawerBody>
        </OverlayDrawer>
      )}

      {creating && types ? <CiPanel mode={{ kind: 'new' }} types={types} onClose={() => setCreating(false)} /> : null}
    </>
  );
}

function NoResults({
  q,
  filtered,
  ticked,
  onClear,
  onAllTypes,
}: {
  q: string;
  filtered: boolean;
  ticked: string[];
  onClear: () => void;
  onAllTypes: () => void;
}) {
  const styles = useStyles();
  return (
    <div className={styles.empty}>
      <SearchRegular className={styles.emptyIcon} aria-hidden />
      <h2 className={styles.emptyTitle}>{q ? `No CIs match ‘${q}’.` : 'No CIs match these filters.'}</h2>
      <Text>
        {filtered
          ? `Only ${ticked.join(', ')} ${ticked.length === 1 ? 'is' : 'are'} ticked. Check the spelling, or search every type.`
          : 'Check the spelling, or try part of the name.'}
      </Text>
      {filtered ? (
        <div className={styles.emptyActions}>
          <Button onClick={onClear}>Clear filters</Button>
          <Button appearance="primary" onClick={onAllTypes}>
            Search all types
          </Button>
        </div>
      ) : null}
    </div>
  );
}

/**
 * The CI type filter (a labelled region): a checkbox per type with its count for the current search,
 * grouped as PRD §9.3. Types with no CIs wait behind "Show N more types"; a ticked type always shows.
 */
function TypeFilter({
  types,
  counts,
  ticked,
  loading,
  showAll,
  onShowAll,
  onToggle,
  onClear,
  onHide,
  inDrawer,
}: {
  types: CiType[] | null;
  counts: CiPageData['types'];
  ticked: string[];
  loading: boolean;
  showAll: boolean;
  onShowAll: () => void;
  onToggle: (name: string, checked: boolean) => void;
  onClear: () => void;
  onHide: () => void;
  inDrawer: boolean;
}) {
  const styles = useStyles();
  const countOf = (name: string) => counts?.find((c) => c.type === name)?.count ?? 0;
  const all = types ?? [];
  const visible = all.filter((t) => showAll || countOf(t.name) > 0 || ticked.includes(t.name));
  const hidden = all.length - visible.length;
  const groups = [...categories, null]
    .map((category) => ({
      category,
      types: visible.filter((t) =>
        category === null ? !categories.includes(t.category ?? '') : t.category === category,
      ),
    }))
    .filter((g) => g.types.length > 0);
  const body = (
    <fieldset className={styles.fieldset}>
      <legend className={styles.legend}>CI type</legend>
      {groups.map((g) => (
        <div key={g.category ?? 'Other'}>
          <div className={styles.category}>{g.category ?? 'Other'}</div>
          {g.types.map((t) => (
            <div key={t.name} className={styles.typeRow}>
              <Checkbox
                label={t.name}
                checked={ticked.includes(t.name)}
                onChange={(_, data) => onToggle(t.name, data.checked === true)}
              />
              {loading ? (
                <Skeleton aria-label="Loading">
                  <SkeletonItem size={12} className={styles.typeCountSkeleton} />
                </Skeleton>
              ) : (
                <span className={styles.typeCount}>{countOf(t.name).toLocaleString('en-US')}</span>
              )}
            </div>
          ))}
        </div>
      ))}
      {hidden > 0 ? (
        <Button appearance="subtle" icon={<ChevronDownRegular />} aria-expanded="false" onClick={onShowAll}>
          Show {hidden} more {hidden === 1 ? 'type' : 'types'}
        </Button>
      ) : null}
    </fieldset>
  );
  if (inDrawer) return body;
  return (
    <section className={styles.filters} aria-labelledby="filters-title">
      <div className={styles.filtersHeader}>
        <h2 id="filters-title" className={styles.filtersTitle}>
          Filters
        </h2>
        {ticked.length > 0 ? (
          <Button size="small" appearance="transparent" onClick={onClear}>
            Clear all
          </Button>
        ) : null}
        <Tooltip content="Hide filters" relationship="label">
          <Button
            size="small"
            appearance="subtle"
            icon={<ChevronLeftRegular />}
            aria-expanded="true"
            onClick={onHide}
          />
        </Tooltip>
      </div>
      {body}
    </section>
  );
}
