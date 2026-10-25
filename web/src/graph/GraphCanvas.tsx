import { makeStyles, type Theme } from '@fluentui/react-components';
import cytoscape from 'cytoscape';
import { useEffect, useRef, type RefObject } from 'react';

import { usePreferences } from '../preferences';
import { darkTheme, lightTheme } from '../theme';
import type { CiType, RelationshipType } from '../ui/inventory';
import { ensureCytoscapeStylesheet } from './cytoscapeStylesheet';
import { layeredPositions, type GraphLayout, type GraphResult } from './graphData';

// The type icons as files in the build (Vite never inlines them): src/graph/typeIcons.ts says why.
const iconFiles = import.meta.glob<string>('./icons/*.svg', { eager: true, query: '?url', import: 'default' });

function iconUrl(key: string, theme: 'light' | 'dark'): string {
  return iconFiles[`./icons/${key}-${theme}.svg`] ?? iconFiles[`./icons/generic-${theme}.svg`] ?? '';
}

/** A type's icon key, as TypeIcon finds it: the type's own, or the built-in type's name in kebab case. */
function iconKey(type: string, types: CiType[] | null): string {
  return types?.find((t) => t.name === type)?.icon ?? type.replace(/(?<=[a-z])(?=[A-Z])/g, '-').toLowerCase();
}

const useStyles = makeStyles({
  canvas: { position: 'absolute', inset: 0 },
});

/** What the canvas has selected: a CI by its ID, or a group by its ID. */
export type GraphSelection = { kind: 'ci'; id: string } | { kind: 'group'; id: string } | null;

/** What the page's buttons do to the canvas (Zoom in, Zoom out, Fit, Export PNG). */
export interface GraphCanvasHandle {
  zoom: (factor: number) => void;
  fit: () => void;
  png: () => Blob | null;
}

const groupNode = (id: string) => `group:${id}`;

function plural(type: string): string {
  return type.endsWith('s') ? type : `${type}s`;
}

/** Cytoscape's stylesheet from the theme's tokens (Notes board): Cytoscape styles are JavaScript, not CSS. */
function stylesheet(theme: Theme): cytoscape.StylesheetJson {
  return [
    {
      selector: 'node',
      style: {
        shape: 'round-rectangle',
        width: 144,
        height: 52,
        'background-color': theme.colorNeutralBackground1,
        'border-width': 1,
        'border-color': theme.colorNeutralStroke1,
        label: 'data(label)',
        'text-wrap': 'wrap',
        'text-max-width': '100px',
        'text-valign': 'center',
        'text-halign': 'center',
        'text-justification': 'left',
        'text-margin-x': 14,
        'font-family': 'Segoe UI, -apple-system, BlinkMacSystemFont, Roboto, Helvetica Neue, sans-serif',
        'font-size': 12,
        'line-height': 1.3,
        color: theme.colorNeutralForeground1,
        'background-image': 'data(icon)',
        'background-width': 20,
        'background-height': 20,
        'background-position-x': 12,
        'background-position-y': '50%',
        'background-clip': 'none',
        'background-image-containment': 'over',
      },
    },
    { selector: 'node.focus', style: { 'border-width': 3, 'border-color': theme.colorCompoundBrandStroke } },
    { selector: 'node.retired', style: { color: theme.colorNeutralForeground3 } },
    {
      selector: 'node.group',
      style: { 'border-width': 3, 'border-style': 'double', 'border-color': theme.colorNeutralStroke1 },
    },
    {
      selector: 'node.selected',
      style: {
        'underlay-color': theme.colorStrokeFocus2,
        'underlay-padding': 4,
        'underlay-opacity': 1,
        'underlay-shape': 'round-rectangle',
      },
    },
    {
      selector: 'edge',
      style: {
        width: 1.5,
        'line-color': theme.colorNeutralForeground3,
        'target-arrow-color': theme.colorNeutralForeground3,
        'target-arrow-shape': 'triangle',
        'curve-style': 'bezier',
        label: 'data(label)',
        'font-size': 11,
        color: theme.colorNeutralForeground2,
        'text-background-color': theme.colorNeutralBackground2,
        'text-background-opacity': 1,
        'text-background-padding': '2px',
      },
    },
    { selector: 'edge.none', style: { 'line-style': 'dotted' } },
    { selector: 'edge.redundant', style: { 'line-style': 'dashed' } },
    {
      selector: 'edge.highlight',
      style: {
        'line-color': theme.colorNeutralForeground1,
        'target-arrow-color': theme.colorNeutralForeground1,
        width: 2,
      },
    },
  ];
}

/** The nodes and edges Cytoscape draws: CIs on their own, groups as one node each, and one edge into a group. */
function elements(
  result: GraphResult,
  types: CiType[] | null,
  relationshipTypes: RelationshipType[] | null,
  theme: 'light' | 'dark',
): cytoscape.ElementDefinition[] {
  const drawn = new Set(result.nodes.filter((n) => !n.grouped).map((n) => n.id));
  const impact = new Map((relationshipTypes ?? []).map((t) => [t.name, t.impact]));
  const nodes: cytoscape.ElementDefinition[] = result.nodes
    .filter((n) => !n.grouped)
    .map((n) => ({
      group: 'nodes',
      data: {
        id: n.id,
        label: `${n.name}\n${n.type}${n.lifecycleState === 'Retired' ? ' · Retired' : ''}`,
        iconKey: iconKey(n.type, types),
        icon: iconUrl(iconKey(n.type, types), theme),
        hops: n.hops,
      },
      classes: [n.id === result.focus ? 'focus' : '', n.lifecycleState === 'Retired' ? 'retired' : ''].filter(Boolean),
    }));
  const groups: cytoscape.ElementDefinition[] = result.groups.map((g) => ({
    group: 'nodes',
    data: {
      id: groupNode(g.id),
      label: `${g.count.toLocaleString('en-US')} ${plural(g.ciType)}\nGroup · ${g.relationshipType}`,
      iconKey: iconKey(g.ciType, types),
      icon: iconUrl(iconKey(g.ciType, types), theme),
      hops: g.hops,
    },
    classes: ['group'],
  }));
  const edges: cytoscape.ElementDefinition[] = result.relationships
    .filter((r) => r.group === null && drawn.has(r.from) && drawn.has(r.to))
    .map((r) => ({
      group: 'edges',
      data: { id: r.id, source: r.from, target: r.to, label: r.redundancyGroup ?? '' },
      classes: [r.redundancyGroup ? 'redundant' : impact.get(r.type) === 'None' ? 'none' : ''].filter(Boolean),
    }));
  const groupEdges: cytoscape.ElementDefinition[] = result.groups.map((g) => ({
    group: 'edges',
    data: {
      id: `edge:${g.id}`,
      source: g.direction === 'dependents' ? groupNode(g.id) : g.hub,
      target: g.direction === 'dependents' ? g.hub : groupNode(g.id),
      label: '',
    },
    classes: impact.get(g.relationshipType) === 'None' ? ['none'] : [],
  }));
  return [...nodes, ...groups, ...edges, ...groupEdges];
}

/** Lays the graph out; if Cytoscape throws, Concentric instead, so a layout bug never blanks the page. */
function runLayout(cy: cytoscape.Core, layout: GraphLayout, focus: string): void {
  try {
    cy.layout(layoutOptions(layout, cy, focus)).run();
  } catch {
    cy.layout(layoutOptions('concentric', cy, focus)).run();
  }
}

function layoutOptions(layout: GraphLayout, cy: cytoscape.Core, focus: string): cytoscape.LayoutOptions {
  const common = { padding: 32, animate: false, fit: true };
  if (layout === 'concentric') {
    const deepest = Math.max(0, ...cy.nodes().map((n) => Number(n.data('hops'))));
    return {
      ...common,
      name: 'concentric',
      concentric: (node: cytoscape.NodeSingular) => deepest - Number(node.data('hops')),
      levelWidth: () => 1,
      minNodeSpacing: 24,
    };
  }
  if (layout === 'force') return { ...common, name: 'cose', idealEdgeLength: () => 120, nodeRepulsion: () => 600000 };
  // Hierarchical: Goldmark's rows, drawn with Cytoscape's built-in preset layout (graphData.ts says why).
  const positions = layeredPositions(
    cy.nodes().map((n) => n.id()),
    cy.edges().map((e) => [e.source().id(), e.target().id()] as [string, string]),
    focus,
  );
  return {
    ...common,
    name: 'preset',
    positions: Object.fromEntries(positions),
  };
}

/**
 * Screen 6's canvas (DEC-046 item 3): Cytoscape.js with its built-in layouts only, its stylesheet element created
 * first with the page's nonce, the type icons as same-origin files, restyled when the theme changes. Only the canvas
 * element carries role img; the page puts the table link and the zoom buttons beside it.
 */
export function GraphCanvas({
  result,
  layout,
  types,
  relationshipTypes,
  label,
  selection,
  handle,
  onSelect,
  onOpen,
}: {
  result: GraphResult;
  layout: GraphLayout;
  types: CiType[] | null;
  relationshipTypes: RelationshipType[] | null;
  label: string;
  selection: GraphSelection;
  handle: RefObject<GraphCanvasHandle | null>;
  onSelect: (selection: GraphSelection) => void;
  onOpen: (ciId: string) => void;
}) {
  const styles = useStyles();
  const { theme } = usePreferences();
  const container = useRef<HTMLDivElement>(null);
  const cy = useRef<cytoscape.Core | null>(null);
  const callbacks = useRef({ onSelect, onOpen });
  callbacks.current = { onSelect, onOpen };
  // Read when a graph is first drawn; their own effects below change them in place.
  const current = useRef({ theme, layout });
  current.current = { theme, layout };

  // A new graph: a new Cytoscape instance, laid out once.
  useEffect(() => {
    if (!container.current) return undefined;
    ensureCytoscapeStylesheet();
    const { theme: shown, layout: laidOut } = current.current;
    const instance = cytoscape({
      container: container.current,
      elements: elements(result, types, relationshipTypes, shown),
      style: stylesheet(shown === 'dark' ? darkTheme : lightTheme),
      boxSelectionEnabled: false,
      autounselectify: true,
      minZoom: 0.1,
      maxZoom: 3,
    });
    runLayout(instance, laidOut, result.focus);
    instance.on('tap', 'node', (event) => {
      const id = event.target.id() as string;
      callbacks.current.onSelect(
        id.startsWith('group:') ? { kind: 'group', id: id.slice('group:'.length) } : { kind: 'ci', id },
      );
    });
    instance.on('tap', (event) => {
      if (event.target === instance) callbacks.current.onSelect(null);
    });
    instance.on('dbltap', 'node', (event) => {
      const id = event.target.id() as string;
      if (!id.startsWith('group:')) callbacks.current.onOpen(id);
    });
    cy.current = instance;
    handle.current = {
      zoom: (factor) => {
        const level = instance.zoom() * factor;
        instance.zoom({ level, renderedPosition: { x: instance.width() / 2, y: instance.height() / 2 } });
      },
      fit: () => instance.fit(undefined, 32),
      png: () =>
        instance.png({
          output: 'blob',
          full: true,
          scale: 2,
          bg: (current.current.theme === 'dark' ? darkTheme : lightTheme).colorNeutralBackground2,
        }),
    };
    return () => {
      handle.current = null;
      cy.current = null;
      instance.destroy();
    };
  }, [result, types, relationshipTypes, handle]);

  // Another layout: the same elements, laid out again.
  useEffect(() => {
    const instance = cy.current;
    if (instance) runLayout(instance, layout, result.focus);
  }, [layout, result.focus]);

  // The theme: Cytoscape's colours and the icons' files.
  useEffect(() => {
    const instance = cy.current;
    if (!instance) return;
    instance.style(stylesheet(theme === 'dark' ? darkTheme : lightTheme));
    instance.nodes().forEach((node) => {
      node.data('icon', iconUrl(node.data('iconKey') as string, theme));
    });
  }, [theme]);

  // The selection: the CI or group ringed, its relationships drawn in the stronger colour.
  useEffect(() => {
    const instance = cy.current;
    if (!instance) return;
    instance.elements().removeClass('selected highlight');
    if (!selection) return;
    const node = instance.getElementById(selection.kind === 'group' ? groupNode(selection.id) : selection.id);
    // The focus CI has its own border; the ring marks another CI chosen on the canvas.
    if (selection.id !== result.focus) node.addClass('selected');
    node.connectedEdges().addClass('highlight');
  }, [selection, result]);

  return <div ref={container} role="img" aria-label={label} className={styles.canvas} />;
}
