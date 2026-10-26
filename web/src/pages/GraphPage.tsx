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
  DrawerBody,
  DrawerFooter,
  DrawerHeader,
  DrawerHeaderTitle,
  Dropdown,
  Field,
  Link,
  Option,
  OptionGroup,
  OverlayDrawer,
  Radio,
  RadioGroup,
  Skeleton,
  SkeletonItem,
  Slider,
  Spinner,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  ToggleButton,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  ArrowDownloadRegular,
  DismissRegular,
  FilterRegular,
  FlowchartRegular,
  FullScreenMaximizeRegular,
  SearchRegular,
  ZoomInRegular,
  ZoomOutRegular,
} from '@fluentui/react-icons';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { api, type ApiError } from '../api';
import { GraphCanvas, type GraphCanvasHandle, type GraphSelection } from '../graph/GraphCanvas';
import {
  canvasLabel,
  completeHops,
  directions,
  exportFileName,
  exportSummary,
  fittingDepth,
  graphAddress,
  graphQuery,
  hopsText,
  layouts,
  readGraphView,
  relationshipLimit,
  statusLine,
  tableCaption,
  isEmpty,
  tooLarge,
  type GraphResult,
  type GraphView,
} from '../graph/graphData';
import { followLink, navigate, useLocation } from '../router';
import { useWide } from '../shell/AppShell';
import { PageHeader } from '../shell/PageHeader';
import { downloadBlob, ErrorBar } from '../ui/common';
import {
  asApiError,
  categories,
  isCiId,
  LifecycleBadge,
  TypeBadge,
  TypeIcon,
  useCiTypes,
  type CiDetail,
  type CiPageData,
  type CiSummary,
  type CiType,
} from '../ui/inventory';
import { morePage } from '../ui/relationships';

const useStyles = makeStyles({
  options: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'flex-end',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalM,
  },
  focusField: { width: '280px' },
  depth: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  depthValue: { minWidth: '1ch', fontWeight: tokens.fontWeightSemibold, fontVariantNumeric: 'tabular-nums' },
  filterField: { width: '220px' },
  fill: { width: '100%', minWidth: 0 },
  layoutField: { width: '160px' },
  viewGroup: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  viewLabel: { fontWeight: tokens.fontWeightSemibold },
  toggles: { display: 'flex' },
  exportGroup: { display: 'flex', gap: tokens.spacingHorizontalS },
  status: { minHeight: '20px', fontWeight: tokens.fontWeightSemibold },
  layout: { display: 'flex', gap: tokens.spacingHorizontalL, alignItems: 'flex-start' },
  layoutNarrow: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  graphColumn: { flexGrow: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  canvasBox: {
    position: 'relative',
    height: '600px',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground2,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
  },
  canvasBoxNarrow: { height: '420px' },
  tableLink: {
    position: 'absolute',
    left: '12px',
    top: '12px',
    zIndex: 2,
    padding: `${tokens.spacingVerticalXXS} ${tokens.spacingHorizontalS}`,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    fontSize: tokens.fontSizeBase200,
  },
  zoom: {
    position: 'absolute',
    right: '12px',
    top: '12px',
    zIndex: 2,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  zoomButton: { backgroundColor: tokens.colorNeutralBackground1 },
  legend: {
    margin: 0,
    padding: 0,
    listStyleType: 'none',
    display: 'flex',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalXL,
    rowGap: tokens.spacingVerticalS,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground2,
  },
  legendItem: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  swatchFocus: {
    width: '18px',
    height: '12px',
    boxSizing: 'border-box',
    border: `3px solid ${tokens.colorCompoundBrandStroke}`,
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  swatchSelected: {
    width: '18px',
    height: '12px',
    boxSizing: 'border-box',
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusSmall,
    outline: `${tokens.strokeWidthThick} solid ${tokens.colorStrokeFocus2}`,
    outlineOffset: '1px',
  },
  swatchLine: { width: '24px', height: 0, borderTop: `2px solid ${tokens.colorNeutralForeground3}` },
  swatchDotted: { width: '24px', height: 0, borderTop: `2px dotted ${tokens.colorNeutralForeground3}` },
  swatchDashed: { width: '24px', height: 0, borderTop: `2px dashed ${tokens.colorNeutralForeground3}` },
  swatchGroup: {
    width: '18px',
    height: '12px',
    boxSizing: 'border-box',
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: `3px 3px 0 -1px ${tokens.colorNeutralBackground1}, 3px 3px 0 0 ${tokens.colorNeutralStroke1}`,
  },
  card: {
    boxSizing: 'border-box',
    padding: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  cardWide: { width: '280px', flexShrink: 0 },
  cardHeader: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
  },
  cardTitle: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
    overflowWrap: 'anywhere',
  },
  badges: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalS },
  facts: {
    margin: 0,
    display: 'grid',
    gridTemplateColumns: 'auto 1fr',
    gap: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
  },
  term: { color: tokens.colorNeutralForeground3, fontWeight: tokens.fontWeightSemibold },
  fact: { margin: 0, overflowWrap: 'anywhere' },
  cardActions: { display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: tokens.spacingHorizontalM },
  hint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  mono: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200 },
  message: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    padding: `${tokens.spacingVerticalXXXL} ${tokens.spacingHorizontalXL}`,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    textAlign: 'center',
    gap: tokens.spacingVerticalM,
  },
  messageIcon: { fontSize: '40px', color: tokens.colorNeutralForeground3, display: 'inline-flex' },
  messageTitle: {
    margin: 0,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    fontWeight: tokens.fontWeightSemibold,
  },
  messageText: { margin: 0, maxWidth: '560px', color: tokens.colorNeutralForeground2 },
  messageActions: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingHorizontalL,
  },
  tableBox: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    overflowX: 'auto',
  },
  table: { minWidth: '720px' },
  caption: {
    captionSide: 'top',
    textAlign: 'left',
    padding: `14px ${tokens.spacingHorizontalL} 6px`,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
  },
  cell: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalXS },
  numeric: { fontVariantNumeric: 'tabular-nums' },
  tableFooter: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
  },
  skeleton: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
  },
  drawer: { width: '320px', maxWidth: '100vw' },
  drawerBody: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  dialogFacts: {
    margin: 0,
    display: 'grid',
    gridTemplateColumns: 'auto 1fr',
    gap: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
  },
  note: { marginTop: tokens.spacingVerticalM, color: tokens.colorNeutralForeground2 },
});

/** The width below which the page opens on the table view (Notes board: the table view on a phone). */
const phoneQuery = '(max-width: 599px)';

function usePhone(): boolean {
  const [phone, setPhone] = useState(() => window.matchMedia(phoneQuery).matches);
  useEffect(() => {
    const query = window.matchMedia(phoneQuery);
    const listener = () => setPhone(query.matches);
    query.addEventListener('change', listener);
    return () => query.removeEventListener('change', listener);
  }, []);
  return phone;
}

type Load =
  | { state: 'idle' }
  | { state: 'loading' }
  | { state: 'missing' }
  | { state: 'failed'; error: ApiError }
  | { state: 'loaded'; result: GraphResult };

/**
 * Screen 6, the graph explorer (US-40, US-53 `/graph?ci=…`; DEC-046 items 3 to 5, 7, 10, 14; the slice 6 boards
 * `Graph*`): a CI's dependencies and dependents up to 6 hops, filtered by CI type and relationship type, retired CIs
 * left out unless shown, more than 10 like neighbours grouped unless switched off, drawn with Cytoscape.js or listed
 * in the table view (the accessible equivalent, NFR-A11Y-2). The address holds the whole view. A graph past 500 CIs
 * offers the ways out instead; the CSV export is the server's, recorded in the audit log; the PNG is the canvas.
 */
export function GraphPage() {
  const styles = useStyles();
  const wide = useWide();
  const phone = usePhone();
  const { search } = useLocation();
  const view = useMemo(() => readGraphView(search), [search]);
  const kind = view.view ?? (phone ? 'table' : 'graph');
  const { types, relationshipTypes } = useCiTypes();
  const [focusCi, setFocusCi] = useState<CiDetail | null>(null);
  const [load, setLoad] = useState<Load>({ state: 'idle' });
  const [selection, setSelection] = useState<GraphSelection>(null);
  const [tableGroup, setTableGroup] = useState<string | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [exportBusy, setExportBusy] = useState(false);
  const [exportFailure, setExportFailure] = useState<ApiError | null>(null);
  const canvas = useRef<GraphCanvasHandle | null>(null);
  const request = useRef(0);
  const query = graphQuery(view);

  const update = useCallback(
    (patch: Partial<GraphView>, push = false) => navigate(graphAddress({ ...view, ...patch }), { replace: !push }),
    [view],
  );

  const fetchGraph = useCallback(async () => {
    const current = ++request.current;
    if (!view.ci) {
      setLoad({ state: 'idle' });
      setFocusCi(null);
      return;
    }
    if (!isCiId(view.ci)) {
      setLoad({ state: 'missing' });
      setFocusCi(null);
      return;
    }
    setLoad({ state: 'loading' });
    try {
      const [{ data: ci }, { data: result }] = await Promise.all([
        api<CiDetail>('GET', `/api/v1/cis/${view.ci}`),
        api<GraphResult>('GET', `/api/v1/graph?${query}`),
      ]);
      if (request.current !== current) return;
      setFocusCi(ci);
      setLoad({ state: 'loaded', result });
      setSelection({ kind: 'ci', id: result.focus });
      setTableGroup(null);
    } catch (error) {
      if (request.current !== current) return;
      const problem = asApiError(error);
      setLoad(problem.status === 404 ? { state: 'missing' } : { state: 'failed', error: problem });
    }
  }, [view.ci, query]);

  useEffect(() => {
    void fetchGraph();
  }, [fetchGraph]);

  const result = load.state === 'loaded' ? load.result : null;
  const focusName = focusCi?.name ?? '';
  const status =
    load.state === 'loading'
      ? `Loading ${hopsText(view.depth)}${focusName ? ` around ${focusName}` : ''}…`
      : result
        ? statusLine(result, focusName)
        : '';
  const filtered = view.types.length > 0 || view.relationshipTypes.length > 0;
  const empty = result !== null && isEmpty(result);
  const large = result !== null && tooLarge(result);

  const exportPng = () => {
    const blob = canvas.current?.png();
    if (blob) downloadBlob(blob, exportFileName(focusName, view.depth, 'png'));
  };

  const exportCsv = async () => {
    setExportBusy(true);
    setExportFailure(null);
    try {
      const { data } = await api<Blob>('POST', `/api/v1/graph/export?${query}`, {}, { file: true });
      downloadBlob(data, exportFileName(focusName, view.depth, 'csv'));
      setExporting(false);
    } catch (error) {
      setExportFailure(asApiError(error));
    } finally {
      setExportBusy(false);
    }
  };

  const filters = (
    <>
      <Field label="Direction">
        <RadioGroup
          layout={wide ? 'horizontal' : 'vertical'}
          value={view.direction}
          onChange={(_, data) => update({ direction: data.value as GraphView['direction'] })}
        >
          {directions.map((d) => (
            <Radio key={d.value} value={d.value} label={d.label} />
          ))}
        </RadioGroup>
      </Field>
      <TypeFilter
        types={types}
        value={view.types}
        onChange={(next) => update({ types: next })}
        className={styles.filterField}
        fillClass={styles.fill}
      />
      <Field label="Relationship types" className={styles.filterField}>
        <Dropdown
          className={styles.fill}
          multiselect
          placeholder="All relationship types"
          value={view.relationshipTypes.join(', ')}
          selectedOptions={view.relationshipTypes}
          onOptionSelect={(_, data) => update({ relationshipTypes: data.selectedOptions })}
        >
          {(relationshipTypes ?? []).map((t) => (
            <Option key={t.name} value={t.name} text={t.name}>
              <span className={styles.mono}>{t.name}</span>
            </Option>
          ))}
        </Dropdown>
      </Field>
      <Switch label="Show retired" checked={view.retired} onChange={(_, data) => update({ retired: data.checked })} />
      <Field label="Layout" className={styles.layoutField}>
        <Dropdown
          className={styles.fill}
          value={layouts.find((l) => l.value === view.layout)?.label ?? 'Hierarchical'}
          selectedOptions={[view.layout]}
          onOptionSelect={(_, data) => update({ layout: (data.optionValue ?? 'hierarchical') as GraphView['layout'] })}
        >
          {layouts.map((l) => (
            <Option key={l.value} value={l.value}>
              {l.label}
            </Option>
          ))}
        </Dropdown>
      </Field>
      <Switch
        label="Group many neighbours"
        checked={view.group}
        onChange={(_, data) => update({ group: data.checked })}
      />
    </>
  );

  return (
    <>
      <PageHeader crumbs={[{ label: 'Inventory' }, { label: 'Graph explorer' }]} title="Graph explorer" />
      <section aria-label="Graph options" className={styles.options}>
        <FocusPicker
          focus={focusCi && focusCi.id === view.ci ? focusCi : null}
          types={types}
          className={styles.focusField}
          onChange={(ci) => update({ ci }, true)}
        />
        <Field label="Depth">
          <span className={styles.depth}>
            <Slider
              min={1}
              max={6}
              step={1}
              value={view.depth}
              input={{ 'aria-valuetext': hopsText(view.depth) }}
              onChange={(_, data) => update({ depth: data.value })}
            />
            <span className={styles.depthValue} aria-hidden="true">
              {view.depth}
            </span>
          </span>
        </Field>
        {wide ? filters : null}
        <div role="group" aria-labelledby="graph-view-label" className={styles.viewGroup}>
          <span id="graph-view-label" className={styles.viewLabel}>
            View
          </span>
          <span className={styles.toggles}>
            <ToggleButton checked={kind === 'graph'} onClick={() => update({ view: phone ? 'graph' : null })}>
              Graph
            </ToggleButton>
            <ToggleButton checked={kind === 'table'} onClick={() => update({ view: phone ? null : 'table' })}>
              Table
            </ToggleButton>
          </span>
        </div>
        {result && !empty ? (
          <div role="group" aria-label="Export" className={styles.exportGroup}>
            {kind === 'graph' && !large ? (
              <Button icon={<ArrowDownloadRegular />} onClick={exportPng}>
                Export PNG
              </Button>
            ) : null}
            {result.truncated ? (
              <Tooltip
                content={`More than ${relationshipLimit.toLocaleString('en-US')} relationships: lower the depth, or filter by CI type or relationship type.`}
                relationship="description"
              >
                <Button icon={<ArrowDownloadRegular />} disabledFocusable aria-haspopup="dialog">
                  Export CSV…
                </Button>
              </Tooltip>
            ) : (
              <Button
                icon={<ArrowDownloadRegular />}
                aria-haspopup="dialog"
                onClick={() => {
                  setExportFailure(null);
                  setExporting(true);
                }}
              >
                Export CSV…
              </Button>
            )}
          </div>
        ) : null}
        {wide ? null : (
          <Button
            icon={<FilterRegular />}
            aria-haspopup="dialog"
            aria-expanded={drawerOpen}
            onClick={() => setDrawerOpen(true)}
          >
            Filters
          </Button>
        )}
      </section>

      <div aria-live="polite" className={styles.status}>
        {status}
      </div>
      {filtered && view.ci ? (
        <Text className={styles.hint}>Filters also stop the walk: CIs beyond a hidden one aren’t reached.</Text>
      ) : null}

      {load.state === 'failed' ? (
        <ErrorBar
          error={load.error}
          message={
            load.error.status === 0
              ? 'Couldn’t load the graph. Check your connection, then try again.'
              : 'Couldn’t load the graph: something went wrong on the server.'
          }
          onRetry={() => void fetchGraph()}
        />
      ) : null}

      {load.state === 'idle' ? (
        <Message
          icon={<FlowchartRegular />}
          title="Choose a CI to explore"
          text="The graph shows the CIs a CI depends on and the CIs that depend on it, up to 6 hops away. Choose one in Focus CI above, or use View in graph on a CI’s page."
        />
      ) : null}

      {load.state === 'missing' ? (
        <Message
          icon={<SearchRegular />}
          title="We can’t find that CI"
          text="The link may be out of date, or the CI was deleted. Choose a CI in Focus CI above."
        >
          <Link href="/search" onClick={(event) => followLink(event, '/search')}>
            Go to Search
          </Link>
        </Message>
      ) : null}

      {load.state === 'loading' ? (
        <div className={styles.canvasBox} aria-busy="true">
          <div className={styles.skeleton}>
            <Skeleton aria-label="Loading the graph">
              {Array.from({ length: 8 }, (_, i) => (
                <SkeletonItem key={i} size={24} />
              ))}
            </Skeleton>
          </div>
        </div>
      ) : null}

      {result && empty ? (
        <Message
          icon={<TypeIcon type={focusCi?.type ?? ''} types={types} />}
          title={
            filtered
              ? `${focusName} has no relationships within the current filters`
              : `${focusName} has no relationships`
          }
          text={
            filtered
              ? 'Clear the filters to see all of its relationships.'
              : 'Relationships show here once someone adds them on the CI’s Relationships tab.'
          }
        >
          {filtered ? (
            <Button appearance="primary" onClick={() => update({ types: [], relationshipTypes: [] })}>
              Clear filters
            </Button>
          ) : null}
          <Link href={`/ci/${result.focus}`} onClick={(event) => followLink(event, `/ci/${result.focus}`)}>
            Open CI
          </Link>
        </Message>
      ) : null}

      {result && !empty && large && kind === 'graph' ? (
        <TooLarge result={result} view={view} onUpdate={update} />
      ) : null}

      {result && !empty && !large && kind === 'graph' ? (
        <div className={wide ? styles.layout : styles.layoutNarrow}>
          <div className={styles.graphColumn}>
            <section aria-label="Graph" className={mergeClasses(styles.canvasBox, !wide && styles.canvasBoxNarrow)}>
              <Link as="button" className={styles.tableLink} onClick={() => update({ view: phone ? null : 'table' })}>
                Switch to table view (same relationships)
              </Link>
              <div role="group" aria-label="Zoom" className={styles.zoom}>
                <Tooltip content="Zoom in" relationship="label">
                  <Button
                    className={styles.zoomButton}
                    icon={<ZoomInRegular />}
                    onClick={() => canvas.current?.zoom(1.25)}
                  />
                </Tooltip>
                <Tooltip content="Zoom out" relationship="label">
                  <Button
                    className={styles.zoomButton}
                    icon={<ZoomOutRegular />}
                    onClick={() => canvas.current?.zoom(0.8)}
                  />
                </Tooltip>
                <Tooltip content="Fit the graph to the view" relationship="label">
                  <Button
                    className={styles.zoomButton}
                    icon={<FullScreenMaximizeRegular />}
                    onClick={() => canvas.current?.fit()}
                  />
                </Tooltip>
              </div>
              <GraphCanvas
                result={result}
                layout={view.layout}
                types={types}
                relationshipTypes={relationshipTypes}
                label={canvasLabel(result, focusName)}
                selection={selection}
                handle={canvas}
                onSelect={setSelection}
                onOpen={(id) => navigate(`/ci/${id}`)}
              />
            </section>
            <Legend />
          </div>
          <SelectionCard
            result={result}
            selection={selection}
            focusName={focusName}
            types={types}
            className={wide ? styles.cardWide : undefined}
            onClear={() => setSelection(null)}
            onExplore={(ci) => update({ ci }, true)}
            onShowGroup={(group) => {
              setTableGroup(group);
              update({ view: phone ? null : 'table' });
            }}
          />
        </div>
      ) : null}

      {result && !empty && kind === 'table' ? (
        <GraphTable
          result={result}
          focusName={focusName}
          types={types}
          group={tableGroup}
          onShowAll={() => setTableGroup(null)}
        />
      ) : null}

      {wide ? null : (
        <OverlayDrawer
          open={drawerOpen}
          position="start"
          className={styles.drawer}
          aria-labelledby="graph-filters-title"
          onOpenChange={(_, data) => setDrawerOpen(data.open)}
        >
          <DrawerHeader>
            <DrawerHeaderTitle
              heading={{ id: 'graph-filters-title' }}
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
          <DrawerBody className={styles.drawerBody}>{filters}</DrawerBody>
          <DrawerFooter>
            <Text>
              {status ? `${status.split(' · up to')[0] ?? status} · the graph updates as you change these` : ''}
            </Text>
          </DrawerFooter>
        </OverlayDrawer>
      )}

      <Dialog open={exporting} onOpenChange={(_, data) => (exportBusy ? undefined : setExporting(data.open))}>
        <DialogSurface aria-describedby="graph-export-body">
          <DialogBody>
            <DialogTitle>
              Export{' '}
              {result
                ? `${result.relationships.length.toLocaleString('en-US')} ${result.relationships.length === 1 ? 'relationship' : 'relationships'}`
                : ''}
              ?
            </DialogTitle>
            <DialogContent id="graph-export-body">
              {result ? (
                <dl className={styles.dialogFacts}>
                  <dt className={styles.term}>Graph</dt>
                  <dd className={styles.fact}>{exportSummary(result, view, focusName)}</dd>
                  <dt className={styles.term}>Columns</dt>
                  <dd className={styles.fact}>From, From type, Relationship, To, To type, Redundancy group, Hops</dd>
                </dl>
              ) : null}
              <div className={styles.note}>The export is recorded in the audit log.</div>
              {exportFailure ? (
                <ErrorBar
                  error={exportFailure}
                  {...(exportFailure.code === 'export-too-large'
                    ? {
                        message:
                          'Too many relationships to export: lower the depth, or filter by CI type or relationship type.',
                      }
                    : {})}
                />
              ) : null}
            </DialogContent>
            <DialogActions>
              <Button disabled={exportBusy} onClick={() => setExporting(false)}>
                Cancel
              </Button>
              <Button appearance="primary" disabled={exportBusy} onClick={() => void exportCsv()}>
                {exportBusy ? <Spinner size="tiny" aria-label="Exporting" /> : null}
                Export
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </>
  );
}

function Message({
  icon,
  title,
  text,
  children,
}: {
  icon: ReactNode;
  title: string;
  text: string;
  children?: ReactNode;
}) {
  const styles = useStyles();
  return (
    <section aria-labelledby="graph-message-title" className={styles.message}>
      <span className={styles.messageIcon} aria-hidden="true">
        {icon}
      </span>
      <h2 id="graph-message-title" className={styles.messageTitle}>
        {title}
      </h2>
      <p className={styles.messageText}>{text}</p>
      {children ? <div className={styles.messageActions}>{children}</div> : null}
    </section>
  );
}

/** More than the graph draws: the ways out (Notes board), and past the read's limit, where it stopped. */
function TooLarge({
  result,
  view,
  onUpdate,
}: {
  result: GraphResult;
  view: GraphView;
  onUpdate: (patch: Partial<GraphView>) => void;
}) {
  const fits = fittingDepth(result);
  // Hop 1 alone passes the limit: it is read whole before it is grouped, so only a filter helps.
  const firstHop = completeHops(result) === 0;
  const title = result.truncated
    ? `More than ${relationshipLimit.toLocaleString('en-US')} relationships within ${hopsText(view.depth)} is too many to draw.`
    : `${result.nodes.length.toLocaleString('en-US')} CIs within ${hopsText(view.depth)} is too many to draw.`;
  const table = result.truncated
    ? `The table view lists the ${hopsText(completeHops(result))} that fit.`
    : 'The table view lists every relationship.';
  const text = firstHop
    ? 'Filter by CI type or relationship type: hop 1 alone passes the limit.'
    : `${view.group ? 'Lower the depth' : 'Turn on Group many neighbours, lower the depth'}, or filter by CI type or relationship type. ${table}`;
  return (
    <Message icon={<FlowchartRegular />} title={title} text={text}>
      {view.group || firstHop ? null : (
        <Button appearance="primary" onClick={() => onUpdate({ group: true })}>
          Group many neighbours
        </Button>
      )}
      {fits !== null ? (
        <Link as="button" onClick={() => onUpdate({ depth: fits })}>
          Set depth to {fits}
        </Link>
      ) : null}
      {firstHop ? null : (
        <Link as="button" onClick={() => onUpdate({ view: 'table' })}>
          Switch to table view
        </Link>
      )}
    </Message>
  );
}

function Legend() {
  const styles = useStyles();
  const item = (swatch: string | null, text: string) => (
    <li className={styles.legendItem}>
      {swatch ? <span aria-hidden="true" className={swatch} /> : null}
      {text}
    </li>
  );
  return (
    <ul aria-label="Legend" className={styles.legend}>
      {item(styles.swatchFocus, 'Focus CI')}
      {item(styles.swatchSelected, 'Selected CI')}
      {item(styles.swatchLine, 'Depends on: the arrow points to the dependency')}
      {item(styles.swatchDotted, 'Impact None: no impact passes along it')}
      {item(styles.swatchDashed, 'In a redundancy group (its name on the line)')}
      {item(styles.swatchGroup, 'Group: more than 10 CIs of one type, counted')}
    </ul>
  );
}

/** The selected CI's card (type, lifecycle, hops, "Owned by" when it has one; DEC-046 item 14) or the group's. */
function SelectionCard({
  result,
  selection,
  focusName,
  types,
  className,
  onClear,
  onExplore,
  onShowGroup,
}: {
  result: GraphResult;
  selection: GraphSelection;
  focusName: string;
  types: CiType[] | null;
  className: string | undefined;
  onClear: () => void;
  onExplore: (ci: string) => void;
  onShowGroup: (group: string) => void;
}) {
  const styles = useStyles();
  if (!selection) return null;
  const close = <Button appearance="subtle" aria-label="Clear selection" icon={<DismissRegular />} onClick={onClear} />;
  const names = new Map(result.nodes.map((n) => [n.id, n.name]));
  if (selection.kind === 'group') {
    const group = result.groups.find((g) => g.id === selection.id);
    if (!group) return null;
    const hub = names.get(group.hub) ?? '';
    const label = `${group.count.toLocaleString('en-US')} ${group.ciType.endsWith('s') ? group.ciType : `${group.ciType}s`}`;
    return (
      <aside aria-labelledby="graph-selected-group" className={mergeClasses(styles.card, className)}>
        <div className={styles.cardHeader}>
          <h2 id="graph-selected-group" className={styles.cardTitle}>
            {label}
          </h2>
          {close}
        </div>
        <span>
          A group:{' '}
          {group.direction === 'dependents' ? (
            <>
              every one <code className={styles.mono}>{group.relationshipType}</code> {hub}
            </>
          ) : (
            <>
              {hub} <code className={styles.mono}>{group.relationshipType}</code> every one
            </>
          )}
          . The graph doesn’t go past it.
        </span>
        <dl className={styles.facts}>
          <dt className={styles.term}>Hops</dt>
          <dd className={styles.fact}>
            {group.hops} from {focusName}
          </dd>
        </dl>
        <div className={styles.cardActions}>
          <Link as="button" onClick={() => onShowGroup(group.id)}>
            Show these {group.count.toLocaleString('en-US')} in the table view
          </Link>
          <Button onClick={() => onExplore(group.hub)}>Explore from {hub}</Button>
        </div>
        <span className={styles.hint}>Turn off Group many neighbours to draw each {group.ciType}.</span>
      </aside>
    );
  }
  const node = result.nodes.find((n) => n.id === selection.id);
  if (!node) return null;
  const owners = result.relationships
    .filter((r) => r.type === 'owned_by' && r.from === node.id)
    .map((r) => names.get(r.to) ?? '');
  return (
    <aside aria-labelledby="graph-selected-ci" className={mergeClasses(styles.card, className)}>
      <div className={styles.cardHeader}>
        <h2 id="graph-selected-ci" className={styles.cardTitle}>
          {node.name}
        </h2>
        {close}
      </div>
      <div className={styles.badges}>
        <TypeBadge type={node.type} types={types} />
        <LifecycleBadge state={node.lifecycleState} />
      </div>
      <dl className={styles.facts}>
        <dt className={styles.term}>Hops</dt>
        <dd className={styles.fact}>{node.hops === 0 ? 'The focus CI' : `${node.hops} from ${focusName}`}</dd>
        {owners.length > 0 ? (
          <>
            <dt className={styles.term}>Owned by</dt>
            <dd className={styles.fact}>{owners.join(', ')}</dd>
          </>
        ) : null}
      </dl>
      <div className={styles.cardActions}>
        {node.id === result.focus ? null : <Button onClick={() => onExplore(node.id)}>Explore from here</Button>}
        <Link href={`/ci/${node.id}`} onClick={(event) => followLink(event, `/ci/${node.id}`)}>
          Open CI
        </Link>
      </div>
      <span className={styles.hint}>Double-click a CI to open it.</span>
    </aside>
  );
}

type SortColumn = 'from' | 'type' | 'to' | 'group' | 'hops';

/** The table view: every relationship the graph read lists, group members included, 50 rows then "Show N more". */
function GraphTable({
  result,
  focusName,
  types,
  group,
  onShowAll,
}: {
  result: GraphResult;
  focusName: string;
  types: CiType[] | null;
  group: string | null;
  onShowAll: () => void;
}) {
  const styles = useStyles();
  const [sort, setSort] = useState<{ column: SortColumn; descending: boolean }>({ column: 'hops', descending: false });
  const [shown, setShown] = useState(50);
  const nodes = useMemo(() => new Map(result.nodes.map((n) => [n.id, n])), [result]);
  const selectedGroup = group ? result.groups.find((g) => g.id === group) : undefined;
  const rows = useMemo(() => {
    const listed = selectedGroup
      ? result.relationships.filter((r) => r.group === selectedGroup.id)
      : result.relationships;
    const name = (id: string) => nodes.get(id)?.name ?? '';
    const key = (r: (typeof listed)[number]): string | number =>
      sort.column === 'from'
        ? name(r.from)
        : sort.column === 'to'
          ? name(r.to)
          : sort.column === 'type'
            ? r.type
            : sort.column === 'group'
              ? (r.redundancyGroup ?? '')
              : r.hops;
    const compare = (a: (typeof listed)[number], b: (typeof listed)[number]) => {
      const x = key(a);
      const y = key(b);
      const primary =
        typeof x === 'number' && typeof y === 'number'
          ? x - y
          : String(x).localeCompare(String(y), 'en', { sensitivity: 'base' });
      return (
        (sort.descending ? -primary : primary) ||
        a.hops - b.hops ||
        name(a.from).localeCompare(name(b.from), 'en', { sensitivity: 'base' }) ||
        a.type.localeCompare(b.type)
      );
    };
    return [...listed].sort(compare);
  }, [result, nodes, sort, selectedGroup]);

  useEffect(() => setShown(50), [result, sort, group]);

  const header = (column: SortColumn, label: string) => (
    <TableHeaderCell
      onClick={() => setSort((s) => ({ column, descending: s.column === column ? !s.descending : false }))}
      {...(sort.column === column
        ? { sortDirection: sort.descending ? ('descending' as const) : ('ascending' as const) }
        : {})}
    >
      {label}
    </TableHeaderCell>
  );
  const link = (id: string) => {
    const node = nodes.get(id);
    return (
      <span className={styles.cell}>
        <TypeIcon type={node?.type ?? ''} types={types} />
        <Link href={`/ci/${id}`} onClick={(event) => followLink(event, `/ci/${id}`)}>
          {node?.name ?? id}
        </Link>
      </span>
    );
  };
  const rest = rows.length - shown;

  return (
    <div className={styles.tableBox}>
      <Table className={styles.table} sortable>
        <caption className={styles.caption}>
          {selectedGroup ? (
            <>
              {rows.length.toLocaleString('en-US')} relationships into the group of{' '}
              {selectedGroup.count.toLocaleString('en-US')} {selectedGroup.ciType} ·{' '}
              <Link as="button" onClick={onShowAll}>
                Show all relationships
              </Link>
            </>
          ) : (
            tableCaption(result, focusName)
          )}
        </caption>
        <TableHeader>
          <TableRow>
            {header('from', 'From')}
            {header('type', 'Relationship')}
            {header('to', 'To')}
            {header('group', 'Redundancy group')}
            {header('hops', 'Hops')}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.slice(0, shown).map((r) => (
            <TableRow key={r.id}>
              <TableCell>{link(r.from)}</TableCell>
              <TableCell>
                <code className={styles.mono}>{r.type}</code>
              </TableCell>
              <TableCell>{link(r.to)}</TableCell>
              <TableCell>{r.redundancyGroup ?? '—'}</TableCell>
              <TableCell className={styles.numeric}>{r.hops}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      <div className={styles.tableFooter}>
        {rest > 0 ? (
          <Button onClick={() => setShown((n) => n + morePage)}>
            Show {Math.min(rest, morePage).toLocaleString('en-US')} more
          </Button>
        ) : null}
        <Text>
          Showing {Math.min(shown, rows.length).toLocaleString('en-US')} of {rows.length.toLocaleString('en-US')}
        </Text>
      </div>
    </div>
  );
}

/** The CI types shown (none ticked: all), grouped as PRD §9.3 groups them. */
function TypeFilter({
  types,
  value,
  onChange,
  className,
  fillClass,
}: {
  types: CiType[] | null;
  value: string[];
  onChange: (types: string[]) => void;
  className: string;
  fillClass: string;
}) {
  const groups = [...categories, null].map((category) => ({
    category,
    types: (types ?? []).filter((t) =>
      category === null ? !categories.includes(t.category ?? '') : t.category === category,
    ),
  }));
  return (
    <Field label="CI types" className={className}>
      <Dropdown
        className={fillClass}
        multiselect
        placeholder="All types"
        value={value.join(', ')}
        selectedOptions={value}
        onOptionSelect={(_, data) => onChange(data.selectedOptions)}
      >
        {groups
          .filter((g) => g.types.length > 0)
          .map((g) => (
            <OptionGroup key={g.category ?? 'Other'} label={g.category ?? 'Other'}>
              {g.types.map((t) => (
                <Option key={t.name} value={t.name} text={t.name}>
                  {t.name}
                </Option>
              ))}
            </OptionGroup>
          ))}
      </Dropdown>
    </Field>
  );
}

/** Focus CI: any CI, retired ones included (it was asked for), searched by name as you type. */
function FocusPicker({
  focus,
  types,
  className,
  onChange,
}: {
  focus: CiDetail | null;
  types: CiType[] | null;
  className: string;
  onChange: (ci: string | null) => void;
}) {
  const styles = useStyles();
  const [typed, setTyped] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [options, setOptions] = useState<CiSummary[]>([]);
  const [searching, setSearching] = useState(false);
  const request = useRef(0);

  useEffect(() => {
    if (!open) return undefined;
    const current = ++request.current;
    const timer = window.setTimeout(() => {
      const query = new URLSearchParams({ q: typed ?? '', limit: '10', retired: 'true' });
      setSearching(true);
      api<CiPageData>('GET', `/api/v1/cis?${query.toString()}`)
        .then(({ data }) => {
          if (request.current === current) setOptions(data.items);
        })
        .catch(() => {
          if (request.current === current) setOptions([]);
        })
        .finally(() => {
          if (request.current === current) setSearching(false);
        });
    }, 250);
    return () => window.clearTimeout(timer);
  }, [open, typed]);

  return (
    <Field label="Focus CI" required className={className}>
      <Combobox
        clearable
        clearIcon={{ 'aria-label': 'Clear Focus CI' }}
        placeholder="Search CIs by name"
        value={typed ?? focus?.name ?? ''}
        selectedOptions={focus ? [focus.id] : []}
        onChange={(event) => setTyped(event.target.value)}
        onOpenChange={(_, data) => {
          setOpen(data.open);
          if (!data.open) setTyped(null);
        }}
        onOptionSelect={(_, data) => {
          setTyped(null);
          onChange(data.optionValue ?? null);
        }}
      >
        {options.length === 0 ? (
          <Option disabled value="" text="">
            {searching ? 'Searching…' : 'No matching CIs'}
          </Option>
        ) : (
          options.map((o) => (
            <Option key={o.id} value={o.id} text={o.name}>
              <span className={styles.cell}>
                <TypeIcon type={o.type} types={types} />
                {o.name}
                {o.lifecycleState === 'Retired' ? (
                  <Badge appearance="tint" color="subtle" size="small">
                    Retired
                  </Badge>
                ) : null}
              </span>
            </Option>
          ))
        )}
      </Combobox>
    </Field>
  );
}
