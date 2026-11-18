// In-page qualification of Cytoscape.js under the served CSP and Trusted Types (DEC-046 item 3). It
// runs on load in any browser and shows PASS or FAIL with the details. It covers what depends on the
// CSP: the stylesheet the app pre-creates with the page's nonce, the container rule it carries, a
// graph drawn with type icons served as files (img-src 'self'), the three built-in layouts, the PNG
// export from an untainted canvas, and positive controls proving that the policy is enforced.
// e2e/cytoscape.spec.ts drives the two downloads and checks the final violation list exactly.
import cytoscape from 'cytoscape';

import { CYTOSCAPE_CONTAINER_CLASS, CYTOSCAPE_STYLESHEET_ID } from '../../../src/graph/cytoscapeIds';
import { ensureCytoscapeStylesheet } from '../../../src/graph/cytoscapeStylesheet';
import { CONTROL_DIRECTIVES } from './controls';
import { violations } from './violations';

export interface CheckResult {
  name: string;
  pass: boolean;
  detail: string;
}

export interface SelfCheckReport {
  pass: boolean;
  userAgent: string;
  checks: CheckResult[];
  violations: string[];
}

declare global {
  interface Window {
    __cytoscapeCheckReport?: SelfCheckReport;
  }
}

// Vite emits each icon as a file under /assets/ (assetsInlineLimit is 0), as the product will.
const ICONS = {
  vm: new URL('./icons/vm.svg', import.meta.url).href,
  host: new URL('./icons/host.svg', import.meta.url).href,
  datastore: new URL('./icons/datastore.svg', import.meta.url).href,
  cluster: new URL('./icons/cluster.svg', import.meta.url).href,
};

const ELEMENTS: cytoscape.ElementDefinition[] = [
  { data: { id: 'vm', label: 'app-erp-db01', icon: ICONS.vm } },
  { data: { id: 'host', label: 'esx-mx-014', icon: ICONS.host } },
  { data: { id: 'ds', label: 'DS-S3-ERP-01', icon: ICONS.datastore } },
  { data: { id: 'cl', label: 'CL-PROD-02', icon: ICONS.cluster } },
  { data: { id: 'runs', source: 'vm', target: 'host', type: 'runs_on' } },
  { data: { id: 'uses', source: 'vm', target: 'ds', type: 'uses_datastore' } },
  { data: { id: 'member', source: 'host', target: 'cl', type: 'member_of_cluster' } },
];

const STYLE: cytoscape.StylesheetJson = [
  {
    selector: 'node',
    style: {
      shape: 'round-rectangle',
      width: 144,
      height: 52,
      'background-color': '#ffffff',
      'border-width': 1,
      'border-color': '#d1d1d1',
      'background-image': 'data(icon)',
      'background-width': 16,
      'background-height': 16,
      'background-position-x': 12,
      label: 'data(label)',
      'font-size': 13,
      color: '#242424',
      'text-valign': 'center',
      'text-halign': 'center',
    },
  },
  {
    selector: 'edge',
    style: {
      width: 1.5,
      'line-color': '#616161',
      'target-arrow-color': '#616161',
      'target-arrow-shape': 'triangle',
      'curve-style': 'bezier',
    },
  },
  { selector: 'edge[type = "member_of_cluster"]', style: { 'line-style': 'dotted' } },
  { selector: 'node:selected', style: { 'border-width': 3, 'border-color': '#8a5a00' } },
];

const nextFrame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

async function waitFor<T>(what: string, probe: () => T | null | undefined | false, timeoutMs = 4000): Promise<T> {
  const deadline = performance.now() + timeoutMs;
  for (;;) {
    const value = probe();
    if (value) return value;
    if (performance.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
}

const directive = (violation: string) => violation.split(' ', 1)[0] ?? '';

function element<K extends keyof HTMLElementTagNameMap>(tag: K, text?: string): HTMLElementTagNameMap[K] {
  const created = document.createElement(tag);
  if (text !== undefined) created.textContent = text;
  return created;
}

/** A sized container for a graph. CSSOM sizing (element.style) is allowed; a style attribute isn't. */
function graphContainer(testId: string): HTMLDivElement {
  const container = element('div');
  container.dataset.testid = testId;
  container.style.width = '640px';
  container.style.height = '360px';
  container.style.border = '1px solid #e0e0e0';
  return container;
}

/** Downloads a Blob through an <a download> on a blob: URL, as screen 6's Export PNG will. */
function download(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const link = element('a');
  link.href = url;
  link.download = name;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

const csvOf = (cy: cytoscape.Core) =>
  ['From,Relationship,To']
    .concat(cy.edges().map((e) => `${e.source().data('label')},${e.data('type')},${e.target().data('label')}`))
    .join('\r\n');

async function runLayout(cy: cytoscape.Core, options: cytoscape.LayoutOptions) {
  const layout = cy.layout(options);
  const stopped = layout.promiseOn('layoutstop');
  layout.run();
  await stopped;
}

export async function runSelfCheck(): Promise<SelfCheckReport> {
  const root = document.getElementById('root') ?? document.body;
  const status = element('section');
  status.dataset.testid = 'self-check';
  status.dataset.status = 'running';
  const heading = element('h2', 'Self-check: RUNNING');
  const details = element('pre');
  status.append(heading, details);
  const container = graphContainer('graph');
  const second = graphContainer('second-graph');
  const exportPng = element('button', 'Export PNG');
  const exportCsv = element('button', 'Export CSV');
  root.append(element('h1', 'Cytoscape.js check'), status, exportPng, exportCsv, container, second);

  const checks: CheckResult[] = [];
  const check = async (name: string, body: () => Promise<string> | string) => {
    try {
      checks.push({ name, pass: true, detail: await body() });
    } catch (e) {
      checks.push({ name, pass: false, detail: e instanceof Error ? e.message : String(e) });
    }
  };
  const expect = (condition: boolean, message: string) => {
    if (!condition) throw new Error(message);
  };

  const nonce = document.querySelector<HTMLMetaElement>('meta[property="csp-nonce"]')?.nonce ?? '';
  let cy: cytoscape.Core | undefined;

  await check('the pre-created stylesheet carries the page nonce', () => {
    expect(nonce.length >= 22, `page nonce has ${nonce.length} characters`);
    const style = ensureCytoscapeStylesheet();
    expect(style.nonce === nonce, 'the stylesheet does not carry the page nonce');
    expect(style.sheet !== null, 'the stylesheet was not applied');
    expect(document.head.firstElementChild === style, 'the stylesheet is not first in <head>');
    return `<style id="${CYTOSCAPE_STYLESHEET_ID}"> with the page nonce, applied`;
  });

  await check('a graph draws, and Cytoscape adds no stylesheet of its own', async () => {
    cy = cytoscape({ container, elements: ELEMENTS, style: STYLE, layout: { name: 'breadthfirst', directed: true } });
    cytoscape({ container: second, elements: ELEMENTS.slice(0, 2), style: STYLE, layout: { name: 'grid' } });
    await nextFrame();
    const sheets = document.querySelectorAll(`#${CYTOSCAPE_STYLESHEET_ID}`);
    expect(sheets.length === 1, `${sheets.length} elements with the stylesheet's id`);
    expect(container.classList.contains(CYTOSCAPE_CONTAINER_CLASS), 'the container lacks its class');
    expect(getComputedStyle(container).position === 'relative', 'the container rule is not applied');
    expect(container.querySelectorAll('canvas').length > 0, 'no canvas in the container');
    expect(cy.nodes().length === 4 && cy.edges().length === 3, 'elements missing');
    return 'two graphs, one stylesheet, the container positioned, canvases drawn';
  });

  await check('type icons load as files from this origin', async () => {
    const wanted = Object.values(ICONS);
    expect(
      wanted.every((url) => new URL(url).origin === location.origin && !url.startsWith('data:')),
      'an icon is not a same-origin file',
    );
    await waitFor('the icons to load', () =>
      wanted.every((url) =>
        performance.getEntriesByName(url).some((e) => (e as PerformanceResourceTiming).responseEnd > 0),
      ),
    );
    return `${wanted.length} icons loaded from ${location.origin}/assets/`;
  });

  await check('the built-in layouts run', async () => {
    if (!cy) throw new Error('no graph');
    await runLayout(cy, { name: 'breadthfirst', directed: true, animate: true, animationDuration: 150 });
    await runLayout(cy, { name: 'concentric', animate: false });
    await runLayout(cy, { name: 'cose', animate: false });
    await runLayout(cy, { name: 'breadthfirst', directed: true, animate: false });
    return 'breadthfirst (animated), concentric, cose';
  });

  await check('selection, zoom and fit', () => {
    if (!cy) throw new Error('no graph');
    cy.$('#vm').select();
    cy.zoom(1.5);
    cy.fit(undefined, 24);
    expect(cy.$(':selected').length === 1, 'nothing selected');
    return 'selected app-erp-db01, zoomed, fitted';
  });

  await check('PNG export from an untainted canvas', async () => {
    if (!cy) throw new Error('no graph');
    const blob = await cy.png({ output: 'blob-promise', full: true, bg: '#ffffff', scale: 1 });
    expect(blob.type === 'image/png' && blob.size > 1000, `PNG of type ${blob.type}, ${blob.size} bytes`);
    return `image/png, ${blob.size} bytes`;
  });

  await check('no violations during normal use', () => {
    expect(violations.length === 0, violations.join('; '));
    return '0 violations';
  });

  await check('positive controls: the policy is enforced', async () => {
    const before = violations.length;
    // What Cytoscape would do without the pre-created element: a <style> without the nonce.
    const unnonced = element('style', `.${CYTOSCAPE_CONTAINER_CLASS} { outline: 3px solid rgb(255, 0, 0); }`);
    document.head.append(unnonced);
    // What a type icon inlined as a data: URI would do.
    const inlined = new Image();
    const refused = new Promise<boolean>((resolve) => {
      inlined.onerror = () => resolve(true);
      inlined.onload = () => resolve(false);
    });
    inlined.src = 'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22/%3E';
    const imageRefused = await refused;
    await waitFor('control violations', () => violations.length - before >= CONTROL_DIRECTIVES.length, 3000).catch(
      () => undefined,
    );
    await nextFrame();
    const caused = violations.slice(before).map(directive).sort();
    const results = {
      unnoncedBlocked: unnonced.sheet === null,
      imageRefused,
      containerNotRed: getComputedStyle(container).outlineColor !== 'rgb(255, 0, 0)',
    };
    unnonced.remove();
    const failed = Object.entries(results).filter(([, ok]) => !ok);
    expect(failed.length === 0, `failed: ${failed.map(([name]) => name).join(', ')}`);
    expect(
      JSON.stringify(caused) === JSON.stringify([...CONTROL_DIRECTIVES]),
      `violations ${JSON.stringify(caused)}, expected ${JSON.stringify(CONTROL_DIRECTIVES)}`,
    );
    return 'a <style> without the nonce and a data: image are refused';
  });

  const graph = cy;
  if (graph) {
    exportPng.addEventListener('click', () => {
      void graph
        .png({ output: 'blob-promise', full: true, bg: '#ffffff' })
        .then((blob) => download(blob, 'graph-check.png'));
    });
    exportCsv.addEventListener('click', () => {
      download(new Blob([csvOf(graph)], { type: 'text/csv;charset=utf-8' }), 'graph-check.csv');
    });
  }

  const report: SelfCheckReport = {
    pass: checks.every((c) => c.pass),
    userAgent: navigator.userAgent,
    checks,
    violations: [...violations],
  };
  window.__cytoscapeCheckReport = report;
  heading.textContent = `Self-check: ${report.pass ? 'PASS' : 'FAIL'}`;
  details.textContent = JSON.stringify(report, null, 2);
  status.dataset.status = report.pass ? 'pass' : 'fail';
  return report;
}
