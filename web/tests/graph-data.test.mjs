// Screen 6's address and words (DEC-046; the slice 6 boards' Notes): the view round-trips through the address with
// only the values that differ from the defaults, and the status line, caption and file names read as the boards
// have them. Runs with the repository's Node (type stripping loads the .ts module).
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { URLSearchParams } from 'node:url';

import {
  exportFileName,
  exportSummary,
  fittingDepth,
  graphAddress,
  graphQuery,
  isEmpty,
  layeredPositions,
  readGraphView,
  statusLine,
  tableCaption,
  tooLarge,
} from '../src/graph/graphData.ts';

const ci = '0192f3a4-7c1e-7d40-9a51-2b7e0c9d1f20';

/** The boards' sample: 14 CIs, 13 relationships, the AG's second replica at hop 4. */
function sample(overrides = {}) {
  const nodes = [
    ['app-erp-db01', 0],
    ['APP-ERP-DB01', 1],
    ['ERP', 1],
    ['ERP Reporting', 1],
    ['esx-mx-014', 1],
    ['DS-S3-ERP-01', 1],
    ['SQLPRD01\\ERP', 2],
    ['S3', 2],
    ['CL-PROD-02', 2],
    ['sw-core-01', 2],
    ['sw-core-02', 2],
    ['AG-ERP', 3],
    ['FS7300-A', 3],
    ['SQLPRD02\\ERP', 4],
  ].map(([name, hops], i) => ({ id: String(i), name, type: 'X', lifecycleState: 'Active', hops, grouped: false }));
  const relationships = Array.from({ length: 13 }, (_, i) => ({
    id: `r${i}`,
    type: 'depends_on',
    from: '0',
    to: String(i + 1),
    redundancyGroup: null,
    hops: 1,
    group: null,
  }));
  return {
    focus: '0',
    depth: 3,
    direction: 'both',
    nodes,
    relationships,
    groups: [],
    countsByHop: [1, 5, 5, 2, 1],
    truncated: false,
    ...overrides,
  };
}

describe('graph address', () => {
  it('holds only the values that differ from the defaults', () => {
    const view = readGraphView(new URLSearchParams(`ci=${ci}`));
    assert.deepEqual(view, {
      ci,
      depth: 3,
      direction: 'both',
      types: [],
      relationshipTypes: [],
      retired: false,
      group: true,
      layout: 'hierarchical',
      view: null,
    });
    assert.equal(graphAddress(view), `/graph?ci=${ci}`);
    assert.equal(graphAddress({ ...view, ci: null }), '/graph');

    const changed = {
      ...view,
      depth: 2,
      direction: 'dependents',
      types: ['VirtualMachine', 'Server'],
      retired: true,
      group: false,
      layout: 'force',
      view: 'table',
    };
    const address = graphAddress(changed);
    assert.equal(
      address,
      `/graph?ci=${ci}&depth=2&direction=dependents&type=VirtualMachine&type=Server&retired=true&group=false&layout=force&view=table`,
    );
    assert.deepEqual(readGraphView(new URLSearchParams(address.slice('/graph?'.length))), changed);
  });

  it('reads an unknown value as its default and passes the read its parameters', () => {
    const view = readGraphView(new URLSearchParams('ci=nope&depth=9&direction=up&layout=tree&view=list&group=no'));
    assert.equal(view.ci, 'nope');
    assert.deepEqual(
      [view.depth, view.direction, view.layout, view.view, view.group],
      [3, 'both', 'hierarchical', null, true],
    );
    assert.equal(
      graphQuery({ ...readGraphView(new URLSearchParams(`ci=${ci}`)), relationshipTypes: ['runs_on'], group: false }),
      `ci=${ci}&depth=3&direction=both&relationshipType=runs_on&group=false`,
    );
  });
});

describe('graph words', () => {
  it('reads as the boards do', () => {
    const result = sample();
    assert.equal(
      statusLine(result, 'app-erp-db01'),
      '14 CIs · 13 relationships · up to 3 hops from app-erp-db01, plus redundancy groups',
    );
    assert.equal(
      tableCaption(result, 'app-erp-db01'),
      '13 relationships within 3 hops of app-erp-db01, plus redundancy groups',
    );
    assert.equal(
      exportSummary(result, readGraphView(new URLSearchParams(`ci=${ci}`)), 'app-erp-db01'),
      '3 hops from app-erp-db01 · Both · plus redundancy groups · retired CIs left out',
    );
    assert.equal(exportFileName('app-erp-db01', 3, 'png'), 'graph-app-erp-db01-3-hops.png');
    assert.equal(exportFileName('SQLPRD01\\ERP', 2, 'csv'), 'graph-SQLPRD01-ERP-2-hops.csv');
    assert.equal(
      statusLine(sample({ nodes: sample().nodes.slice(0, 1), relationships: [], countsByHop: [1] }), 'hcx-ma-01'),
      '1 CI · no relationships · up to 3 hops from hcx-ma-01',
    );
  });

  it('is too large past 500 drawn, and offers the deepest depth that fits', () => {
    assert.equal(tooLarge(sample()), false);
    const large = sample({ countsByHop: [1, 5, 300, 225] });
    assert.equal(tooLarge(large), true);
    assert.equal(fittingDepth(large), 2);
    assert.equal(fittingDepth(sample({ countsByHop: [1, 600] })), null);
  });

  it('past the read limit, says where it stopped', () => {
    const truncated = sample({ countsByHop: [1, 5, 5], truncated: true });
    assert.equal(tooLarge(truncated), true);
    assert.equal(statusLine(truncated, 'CL-PROD-02'), 'More than 25,000 relationships · up to 3 hops from CL-PROD-02');
    assert.equal(
      tableCaption(truncated, 'CL-PROD-02'),
      '13 relationships within 2 hops of CL-PROD-02; hop 3 would pass the 25,000-relationship limit',
    );
    assert.equal(isEmpty(truncated), false);
  });

  it('stopped before hop 1, is too large, not empty', () => {
    const none = sample({ nodes: sample().nodes.slice(0, 1), relationships: [], countsByHop: [1] });
    assert.equal(isEmpty(none), true);
    const stopped = { ...none, truncated: true };
    assert.equal(isEmpty(stopped), false);
    assert.equal(tooLarge(stopped), true);
    assert.equal(fittingDepth(stopped), null);
    assert.equal(
      tableCaption(stopped, 'esx-big'),
      'No relationships: hop 1 from esx-big would pass the 25,000-relationship limit',
    );
  });
});

describe('the Hierarchical layout', () => {
  it('puts the boards’ sample in the boards’ rows: dependents above, dependencies below, arrows downward', () => {
    const edges = [
      ['srv', 'vm'],
      ['vm', 'esx'],
      ['vm', 'ds'],
      ['erp', 'vm'],
      ['erprpt', 'vm'],
      ['ds', 's3'],
      ['esx', 'cl'],
      ['esx', 'sw1'],
      ['esx', 'sw2'],
      ['sql1', 'srv'],
      ['ag', 'sql1'],
      ['s3', 'fs'],
      ['ag', 'sql2'],
    ];
    const ids = [...new Set(edges.flat())];
    const positions = layeredPositions(ids, edges, 'vm', { x: 100, y: 10 });
    const rows = {};
    for (const [id, p] of positions) (rows[p.y] ??= []).push(id);
    assert.deepEqual(
      Object.keys(rows)
        .map(Number)
        .sort((a, b) => a - b)
        .map((y) => rows[y].sort().join(',')),
      ['ag', 'sql1,sql2', 'erp,erprpt,srv', 'vm', 'ds,esx', 'cl,s3,sw1,sw2', 'fs'],
    );
    for (const [from, to] of edges) assert.ok(positions.get(from).y < positions.get(to).y, `${from} above ${to}`);
    const row = (y) => [...positions].filter(([, p]) => p.y === y).map(([, p]) => p.x);
    assert.equal(new Set(row(20)).size, 4, 'no two CIs in a row share a place');
  });

  it('wraps a wide row onto more lines, below the focus for dependencies and above it for dependents', () => {
    const vms = Array.from({ length: 12 }, (_, i) => `vm${i}`);
    const edges = [...vms.map((vm) => [vm, 'host']), ['host', 'cluster']];
    const positions = layeredPositions([...vms, 'host', 'cluster'], edges, 'host', { x: 100, y: 10 });
    const lines = [...new Set(vms.map((vm) => positions.get(vm).y))].sort((a, b) => a - b);
    assert.deepEqual(lines, [-20, -10]);
    assert.equal(vms.filter((vm) => positions.get(vm).y === -10).length, 8, 'the nearest line holds eight');
    assert.equal(positions.get('host').y, 0);
    assert.equal(positions.get('cluster').y, 10);
  });
});
