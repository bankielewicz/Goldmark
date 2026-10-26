/**
 * Screen 6's data and words (US-40; DEC-046 items 4, 5, 7, 10; the slice 6 boards `Graph*` and their Notes): the
 * graph read's shape, the view as the address holds it, and the status line, captions and file names. No relative
 * imports: Node's type stripping loads this file in tests/graph-data.test.mjs.
 */

export type GraphDirection = 'both' | 'dependencies' | 'dependents';
export type GraphLayout = 'hierarchical' | 'concentric' | 'force';
export type GraphViewKind = 'graph' | 'table';

export interface GraphNode {
  id: string;
  name: string;
  type: string;
  lifecycleState: string;
  hops: number;
  /** Reached only as a group's member: the graph draws the group instead. */
  grouped: boolean;
}

export interface GraphRelationship {
  id: string;
  type: string;
  from: string;
  to: string;
  redundancyGroup: string | null;
  hops: number;
  /** The group it leads into, if any. */
  group: string | null;
}

export interface GraphGroup {
  id: string;
  hub: string;
  direction: 'dependencies' | 'dependents';
  relationshipType: string;
  ciType: string;
  hops: number;
  count: number;
  members: string[];
}

export interface GraphResult {
  focus: string;
  depth: number;
  direction: GraphDirection;
  nodes: GraphNode[];
  relationships: GraphRelationship[];
  groups: GraphGroup[];
  /** What the graph draws at each hop: a CI on its own or a group counts one; index 0 is the focus CI. */
  countsByHop: number[];
  /** The read stopped before a hop that would pass the limit; the hops returned are complete. */
  truncated: boolean;
}

/** The view as the address holds it (`/graph?ci=…&depth=…`), each value only when it isn't the default. */
export interface GraphView {
  ci: string | null;
  depth: number;
  direction: GraphDirection;
  types: string[];
  relationshipTypes: string[];
  retired: boolean;
  group: boolean;
  layout: GraphLayout;
  /** Null: the default (the graph, or the table on a phone). */
  view: GraphViewKind | null;
}

/** DEC-046 item 10: the graph opens at depth 3; the read takes 1 to 6. */
export const defaultDepth = 3;
export const maxDepth = 6;

/** The most CIs the graph draws (Notes board; a group counts as one). */
export const drawLimit = 500;

/** The graph read's limit (DEC-046 item 7, owner): past it the read stops before the hop. */
export const relationshipLimit = 25_000;

export const layouts: { value: GraphLayout; label: string }[] = [
  { value: 'hierarchical', label: 'Hierarchical' },
  { value: 'concentric', label: 'Concentric' },
  { value: 'force', label: 'Force' },
];

export const directions: { value: GraphDirection; label: string }[] = [
  { value: 'dependencies', label: 'Dependencies' },
  { value: 'dependents', label: 'Dependents' },
  { value: 'both', label: 'Both' },
];

export function readGraphView(search: URLSearchParams): GraphView {
  const depth = Number(search.get('depth'));
  const direction = search.get('direction');
  const layout = search.get('layout');
  const view = search.get('view');
  const ci = search.get('ci');
  return {
    ci:
      ci && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(ci)
        ? ci.toLowerCase()
        : ci
          ? ci
          : null,
    depth: Number.isInteger(depth) && depth >= 1 && depth <= maxDepth ? depth : defaultDepth,
    direction: direction === 'dependencies' || direction === 'dependents' ? direction : 'both',
    types: search.getAll('type'),
    relationshipTypes: search.getAll('relationshipType'),
    retired: search.get('retired') === 'true',
    group: search.get('group') !== 'false',
    layout: layout === 'concentric' || layout === 'force' ? layout : 'hierarchical',
    view: view === 'table' || view === 'graph' ? view : null,
  };
}

/** The address of a view, with only the values that differ from the defaults. */
export function graphAddress(view: GraphView): string {
  const query = new URLSearchParams();
  if (view.ci) query.set('ci', view.ci);
  if (view.depth !== defaultDepth) query.set('depth', String(view.depth));
  if (view.direction !== 'both') query.set('direction', view.direction);
  for (const type of view.types) query.append('type', type);
  for (const type of view.relationshipTypes) query.append('relationshipType', type);
  if (view.retired) query.set('retired', 'true');
  if (!view.group) query.set('group', 'false');
  if (view.layout !== 'hierarchical') query.set('layout', view.layout);
  if (view.view) query.set('view', view.view);
  const text = query.toString();
  return text ? `/graph?${text}` : '/graph';
}

/** The graph read's (and the export's) parameters for a view. */
export function graphQuery(view: GraphView): string {
  const query = new URLSearchParams({ ci: view.ci ?? '', depth: String(view.depth), direction: view.direction });
  for (const type of view.types) query.append('type', type);
  for (const type of view.relationshipTypes) query.append('relationshipType', type);
  if (view.retired) query.set('retired', 'true');
  if (!view.group) query.set('group', 'false');
  return query.toString();
}

function count(n: number, one: string, many: string): string {
  return `${n.toLocaleString('en-US')} ${n === 1 ? one : many}`;
}

export function hopsText(n: number): string {
  return count(n, 'hop', 'hops');
}

/** Redundancy-group members past the depth (DEC-046 item 4) are in the graph. */
export function hasGroupMembersPastDepth(result: GraphResult): boolean {
  return result.nodes.some((n) => n.hops > result.depth);
}

/** The CIs the graph draws: on their own, plus one per group. */
export function drawnCount(result: GraphResult): number {
  return result.countsByHop.reduce((sum, n) => sum + n, 0);
}

export function tooLarge(result: GraphResult): boolean {
  return result.truncated || drawnCount(result) > drawLimit;
}

/** The deepest depth below the one asked for whose graph fits the draw limit, or null. */
export function fittingDepth(result: GraphResult): number | null {
  let fits: number | null = null;
  let sum = result.countsByHop[0] ?? 1;
  for (let depth = 1; depth < result.depth && depth < result.countsByHop.length; depth++) {
    sum += result.countsByHop[depth] ?? 0;
    if (sum > drawLimit) break;
    fits = depth;
  }
  return fits;
}

/** The hops the read returned in full: all of them, unless it stopped at the limit. */
export function completeHops(result: GraphResult): number {
  return result.truncated ? Math.max(0, result.countsByHop.length - 1) : result.depth;
}

/** "14 CIs · 13 relationships · up to 3 hops from app-erp-db01, plus redundancy groups". */
export function statusLine(result: GraphResult, focusName: string): string {
  const from = `up to ${hopsText(result.depth)} from ${focusName}`;
  if (result.truncated) return `More than ${count(relationshipLimit, 'relationship', 'relationships')} · ${from}`;
  const grouped = result.nodes.filter((n) => n.grouped).length;
  const cis =
    result.groups.length > 0
      ? `${count(result.nodes.length, 'CI', 'CIs')} (${grouped.toLocaleString('en-US')} in ${count(result.groups.length, 'group', 'groups')})`
      : count(result.nodes.length, 'CI', 'CIs');
  const relationships =
    result.relationships.length === 0
      ? 'no relationships'
      : count(result.relationships.length, 'relationship', 'relationships');
  return `${cis} · ${relationships} · ${from}${hasGroupMembersPastDepth(result) ? ', plus redundancy groups' : ''}`;
}

/** The canvas's accessible name. */
export function canvasLabel(result: GraphResult, focusName: string): string {
  const grouped = result.nodes.filter((n) => n.grouped).length;
  const groups =
    result.groups.length > 0
      ? `, ${grouped.toLocaleString('en-US')} of them in ${count(result.groups.length, 'group', 'groups')}`
      : '';
  return `Graph of ${count(result.nodes.length, 'CI', 'CIs')} around ${focusName}${groups}; the table view lists the same relationships`;
}

/** The table view's caption, saying where a read past the limit stopped. */
export function tableCaption(result: GraphResult, focusName: string): string {
  const rows = count(result.relationships.length, 'relationship', 'relationships');
  if (result.truncated) {
    const complete = completeHops(result);
    return `${rows} within ${hopsText(complete)} of ${focusName}; hop ${complete + 1} would pass the ${relationshipLimit.toLocaleString('en-US')}-relationship limit`;
  }
  return `${rows} within ${hopsText(result.depth)} of ${focusName}${hasGroupMembersPastDepth(result) ? ', plus redundancy groups' : ''}`;
}

/** The export confirmation's "Graph" line: "3 hops from app-erp-db01 · Both · plus redundancy groups · retired CIs left out". */
export function exportSummary(result: GraphResult, view: GraphView, focusName: string): string {
  const parts = [
    `${hopsText(view.depth)} from ${focusName}`,
    directions.find((d) => d.value === view.direction)?.label ?? 'Both',
    ...(hasGroupMembersPastDepth(result) ? ['plus redundancy groups'] : []),
    ...(view.types.length > 0 ? [`CI types: ${view.types.join(', ')}`] : []),
    ...(view.relationshipTypes.length > 0 ? [`relationship types: ${view.relationshipTypes.join(', ')}`] : []),
    view.retired ? 'retired CIs included' : 'retired CIs left out',
  ];
  return parts.join(' · ');
}

/** A CI's name in a file name, as the server writes it: ASCII letters and digits, other runs one hyphen. */
export function fileStem(name: string): string {
  const stem = name
    .replace(/[^A-Za-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 100)
    .replace(/-+$/, '');
  return stem || 'ci';
}

/** "graph-app-erp-db01-3-hops.png" (Notes board: export file names say what they hold). */
export function exportFileName(focusName: string, depth: number, extension: 'png' | 'csv'): string {
  return `graph-${fileStem(focusName)}-${depth}-hops.${extension}`;
}

/**
 * The Hierarchical layout's positions (Notes board: dependents above the focus CI, dependencies below it, so arrows
 * read top-down). Each CI's row: 0 for the focus, +n for a CI n relationships down its dependencies, -n for one n up
 * its dependents, and a CI reached neither way (a redundancy group's other member) one row below the CI that depends
 * on it. In a row, CIs are ordered by where their neighbours already stand, nearest rows first, then centred.
 * Computed here and drawn with Cytoscape's built-in `preset` layout (DEC-046 item 16): `breadthfirst` grows one way
 * from its roots, so it can't put dependents above the focus and dependencies below in one run; and its `maximal`
 * option, which would keep arrows downward, fails on the boards' sample in 3.34.4 (a TypeError in
 * `getWeightedPercent`, reproduced headless in Node).
 */
export function layeredPositions(
  ids: string[],
  edges: [string, string][],
  focus: string,
  spacing: { x: number; y: number } = { x: 200, y: 110 },
  perLine = 8,
): Map<string, { x: number; y: number }> {
  const out = new Map<string, string[]>(ids.map((id) => [id, []]));
  const back = new Map<string, string[]>(ids.map((id) => [id, []]));
  for (const [from, to] of edges) {
    out.get(from)?.push(to);
    back.get(to)?.push(from);
  }
  const row = new Map<string, number>([[focus, 0]]);
  for (const [links, step] of [
    [out, 1],
    [back, -1],
  ] as const) {
    const queue = [focus];
    for (let at = queue.shift(); at !== undefined; at = queue.shift()) {
      for (const next of links.get(at) ?? []) {
        if (row.has(next)) continue;
        row.set(next, (row.get(at) ?? 0) + step);
        queue.push(next);
      }
    }
  }
  for (let changed = true; changed;) {
    changed = false;
    for (const id of ids) {
      if (row.has(id)) continue;
      const parent = (back.get(id) ?? []).find((p) => row.has(p));
      const child = (out.get(id) ?? []).find((c) => row.has(c));
      if (parent !== undefined) row.set(id, (row.get(parent) ?? 0) + 1);
      else if (child !== undefined) row.set(id, (row.get(child) ?? 0) - 1);
      else continue;
      changed = true;
    }
  }
  for (const id of ids) if (!row.has(id)) row.set(id, 0);

  const rows = new Map<number, string[]>();
  for (const [id, r] of row) rows.set(r, [...(rows.get(r) ?? []), id]);
  // A layer wider than `perLine` CIs wraps onto more lines, so a host's VMs with grouping off stay readable; the
  // layers further out move away from the focus to make room.
  const lines = (r: number) => Math.max(1, Math.ceil((rows.get(r)?.length ?? 0) / perLine));
  const keys = [...rows.keys()];
  const top = new Map<number, number>([[0, 0]]);
  for (let r = 1; r <= Math.max(0, ...keys); r++) top.set(r, (top.get(r - 1) ?? 0) + lines(r - 1) * spacing.y);
  for (let r = -1; r >= Math.min(0, ...keys); r--) top.set(r, (top.get(r + 1) ?? 0) - lines(r) * spacing.y);
  const order = keys.sort((a, b) => Math.abs(a) - Math.abs(b) || b - a);
  const positions = new Map<string, { x: number; y: number }>();
  for (const r of order) {
    const members = rows.get(r) ?? [];
    const placed = (id: string) => {
      const xs = [...(out.get(id) ?? []), ...(back.get(id) ?? [])].flatMap((n) => {
        const p = positions.get(n);
        return p ? [p.x] : [];
      });
      return xs.length > 0 ? xs.reduce((sum, x) => sum + x, 0) / xs.length : 0;
    };
    const sorted = members
      .map((id) => ({ id, x: placed(id) }))
      .sort((a, b) => a.x - b.x || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
    for (let line = 0; line * perLine < sorted.length; line++) {
      const chunk = sorted.slice(line * perLine, (line + 1) * perLine);
      // Above the focus the last line is the nearest one, so lines stack upward from it.
      const y = (top.get(r) ?? 0) + (r < 0 ? lines(r) - 1 - line : line) * spacing.y;
      chunk.forEach((m, i) => positions.set(m.id, { x: (i - (chunk.length - 1) / 2) * spacing.x, y }));
    }
  }
  return positions;
}
