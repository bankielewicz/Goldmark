import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';

import { admin, expectAccessible, recordViolations, signIn, toast, useScheme } from './session';

// Slice 6 in a real browser against the Compose stack (DEC-046): screen 5's Relationships tab, the Add and Edit
// panel with several CIs at once and a partial add, the Delete dialog, screen 6's graph explorer on the boards'
// sample (14 CIs, 13 relationships, the second AG replica at hop 4), its table view and both exports, and the
// audit log's new entries; axe in both themes and no CSP or Trusted Types violation. With
// GOLDMARK_E2E_SCREENSHOTS set to a folder, each screen is saved there for a look against the boards.

const shots = process.env.GOLDMARK_E2E_SCREENSHOTS;

async function shot(page: Page, name: string) {
  if (!shots) return;
  mkdirSync(shots, { recursive: true });
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'));
  await page.screenshot({ path: join(shots, `${name}.png`), fullPage: true });
}

/**
 * An API call from the signed-in page itself (its cookies, a fresh anti-forgery token, and the browser's pinned
 * certificate, which Playwright's own request context doesn't have).
 */
async function call(
  page: Page,
  method: 'GET' | 'POST',
  path: string,
  body?: unknown,
  ifMatch?: string,
): Promise<{ status: number; etag: string | null; json: Record<string, unknown> | null }> {
  return page.evaluate(
    async ({ method, path, body, ifMatch }) => {
      const headers: Record<string, string> = { Accept: 'application/json' };
      if (method !== 'GET') {
        const token = (await (await fetch('/api/v1/auth/antiforgery')).json()) as { token: string };
        headers['X-Goldmark-Antiforgery'] = token.token;
        headers['Content-Type'] = 'application/json';
      }
      if (ifMatch) headers['If-Match'] = ifMatch;
      const response = await fetch(path, {
        method,
        headers,
        ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      });
      const text = await response.text();
      return {
        status: response.status,
        etag: response.headers.get('ETag'),
        json: text ? (JSON.parse(text) as Record<string, unknown>) : null,
      };
    },
    { method, path, body, ifMatch },
  );
}

async function post(page: Page, path: string, body: unknown): Promise<string> {
  const response = await call(page, 'POST', path, body);
  expect(response.status, `${path} ${JSON.stringify(body)}`).toBe(201);
  return String(response.json?.id);
}

/** The boards' sample around app-erp-db01 (Graph.dc.html), by name; uplinks are added through the panel. */
async function seed(page: Page): Promise<Record<string, string>> {
  const ids: Record<string, string> = {};
  for (const [type, name] of [
    ['VirtualMachine', 'app-erp-db01'],
    ['Server', 'APP-ERP-DB01'],
    ['SqlInstance', 'SQLPRD01\\ERP'],
    ['SqlInstance', 'SQLPRD02\\ERP'],
    ['SqlAvailabilityGroup', 'AG-ERP'],
    ['BusinessService', 'ERP'],
    ['BusinessService', 'ERP Reporting'],
    ['BusinessService', 'Payroll'],
    ['HypervisorHost', 'esx-mx-014'],
    ['Datastore', 'DS-S3-ERP-01'],
    ['StorageGroup', 'S3'],
    ['StorageArray', 'FS7300-A'],
    ['HypervisorCluster', 'CL-PROD-02'],
    ['NetworkDevice', 'sw-core-01'],
    ['NetworkDevice', 'sw-core-02'],
    ['NetworkDevice', 'sw-core-03'],
  ] as const) {
    ids[name] = await post(page, '/api/v1/cis', { type, name });
  }
  for (const [from, type, to, group] of [
    ['APP-ERP-DB01', 'os_of', 'app-erp-db01', ''],
    ['app-erp-db01', 'runs_on', 'esx-mx-014', ''],
    ['app-erp-db01', 'uses_datastore', 'DS-S3-ERP-01', ''],
    ['ERP', 'depends_on', 'app-erp-db01', ''],
    ['ERP Reporting', 'depends_on', 'app-erp-db01', ''],
    ['Payroll', 'depends_on', 'app-erp-db01', ''],
    ['DS-S3-ERP-01', 'backed_by', 'S3', ''],
    ['esx-mx-014', 'member_of_cluster', 'CL-PROD-02', ''],
    ['SQLPRD01\\ERP', 'hosted_on', 'APP-ERP-DB01', ''],
    ['AG-ERP', 'replica_on', 'SQLPRD01\\ERP', 'AG-ERP'],
    ['AG-ERP', 'replica_on', 'SQLPRD02\\ERP', 'AG-ERP'],
    ['S3', 'part_of_array', 'FS7300-A', ''],
  ] as const) {
    await post(page, '/api/v1/relationships', { type, from: ids[from], to: ids[to], redundancyGroup: group });
  }
  const payroll = ids.Payroll ?? '';
  const ci = await call(page, 'GET', `/api/v1/cis/${payroll}`);
  expect((await call(page, 'POST', `/api/v1/cis/${payroll}/retire`, {}, ci.etag ?? '')).status).toBe(200);
  return ids;
}

test.describe('slice 6 screens', () => {
  test.use({ reducedMotion: 'reduce' });
  test.describe.configure({ timeout: 240_000 });
  test.skip(!admin.userName || !admin.password || !admin.totpKey, 'needs GOLDMARK_E2E_ADMIN_* from compose-smoke.sh');

  test('an administrator adds several CIs, edits, deletes, and explores the graph and its exports', async ({
    page,
  }) => {
    await recordViolations(page);
    // What the page itself reports, for the failure message.
    const problems: string[] = [];
    page.on('pageerror', (error) => problems.push(`page error: ${error.message}`));
    page.on('console', (message) => {
      if (message.type() === 'error' || message.type() === 'warning')
        problems.push(`${message.type()}: ${message.text()}`);
    });
    await signIn(page);
    const ids = await seed(page);

    // Screen 5's Relationships tab: both sections, retired CIs marked, the count with the tab.
    await page.goto(`/ci/${ids['app-erp-db01']}?tab=relationships`);
    await expect(page.getByRole('heading', { level: 1, name: 'app-erp-db01' })).toBeVisible();
    await expect(page.getByRole('tab', { name: /Relationships\s*6/ })).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('heading', { level: 2, name: 'Depends on (2)' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Depended on by (4)' })).toBeVisible();
    await expect(page.getByRole('row', { name: /Payroll.*Retired/ })).toBeVisible();
    await expect(page.getByRole('link', { name: 'View in graph' })).toHaveAttribute(
      'href',
      `/graph?ci=${ids['app-erp-db01']}`,
    );
    await expectAccessible(page, 'Relationships tab');
    await shot(page, 'rels');
    await useScheme(page, 'dark');
    await expectAccessible(page, 'Relationships tab (dark)');
    await shot(page, 'rels-dark');
    await useScheme(page, 'light');

    // Add relationship to esx-mx-014: uplinks_to two switches at once, with a redundancy group.
    await page.goto(`/ci/${ids['esx-mx-014']}?tab=relationships`);
    await page.getByRole('button', { name: 'Add relationship' }).click();
    const add = page.getByRole('dialog', { name: 'Add relationship to esx-mx-014' });
    await expect(add).toBeVisible();
    await add.getByRole('combobox', { name: /^Relationship type/ }).click();
    await page.getByRole('option', { name: /^uplinks_to/ }).click();
    await expect(add.getByText('With uplinks_to, a HypervisorHost is always the dependent.')).toBeVisible();
    const picker = add.getByRole('combobox', { name: /^Depends on/ });
    for (const name of ['sw-core-01', 'sw-core-02']) {
      await picker.click();
      await picker.pressSequentially(name);
      await page.getByRole('option', { name: new RegExp(`^${name}`) }).click();
    }
    await add.getByRole('combobox', { name: 'Redundancy group' }).fill('vSwitch0');
    await expect(
      add.getByText('esx-mx-014 uplinks_to 2 CIs: esx-mx-014 depends on sw-core-01 and sw-core-02.'),
    ).toBeVisible();
    await expectAccessible(page, 'Add relationship panel');
    await shot(page, 'add-groups');
    await add.getByRole('button', { name: 'Add', exact: true }).click();
    await expect(toast(page, '2 relationships added')).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Depends on (3)' })).toBeVisible();

    // A partial add: sw-core-02 again and sw-core-03; the panel stays open and Cancel reads Close.
    await page.getByRole('button', { name: 'Add relationship' }).click();
    await add.getByRole('combobox', { name: /^Relationship type/ }).click();
    await page.getByRole('option', { name: /^uplinks_to/ }).click();
    for (const name of ['sw-core-02', 'sw-core-03']) {
      await picker.click();
      await picker.pressSequentially(name);
      await page.getByRole('option', { name: new RegExp(`^${name}`) }).click();
    }
    await add.getByRole('combobox', { name: 'Redundancy group' }).click();
    await expect(page.getByRole('option', { name: /vSwitch0/ })).toBeVisible();
    await page.getByRole('option', { name: /vSwitch0/ }).click();
    await add.getByRole('button', { name: 'Add', exact: true }).click();
    await expect(add.getByText('1 of 2 relationships added.')).toBeVisible();
    await expect(add.getByText(/sw-core-02: this relationship already exists\./)).toBeVisible();
    await shot(page, 'add-partial');
    // The footer's Close, not the header's. The tab behind the panel has already refreshed.
    await add.getByRole('button', { name: 'Close', exact: true }).last().click();
    await expect(add).toHaveCount(0);
    await expect(page.getByRole('heading', { level: 2, name: 'Depends on (4)' })).toBeVisible();

    // Edit the group of one uplink, then delete another.
    await page.getByRole('button', { name: 'Edit uplinks_to sw-core-03' }).click();
    const edit = page.getByRole('dialog', { name: 'Edit uplinks_to sw-core-03' });
    await expect(edit).toBeVisible();
    await expectAccessible(page, 'Edit relationship panel');
    await edit.getByRole('combobox', { name: 'Redundancy group' }).fill('vSwitch1');
    await edit.getByRole('button', { name: 'Save' }).click();
    await expect(toast(page, 'Changes saved')).toBeVisible();
    await expect(page.getByRole('row', { name: /sw-core-03.*vSwitch1/ })).toBeVisible();
    await page.getByRole('button', { name: 'Delete uplinks_to sw-core-03' }).click();
    const remove = page.getByRole('alertdialog', { name: 'Delete uplinks_to sw-core-03?' });
    await expect(remove.getByText('esx-mx-014 will no longer depend on sw-core-03.', { exact: false })).toBeVisible();
    await expectAccessible(page, 'Delete relationship dialog');
    await shot(page, 'delete');
    await remove.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(toast(page, 'Relationship deleted')).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Depends on (3)' })).toBeVisible();

    // Screen 6 from View in graph: the boards' sample, 3 hops plus the AG's other replica at hop 4.
    await page.goto(`/ci/${ids['app-erp-db01']}`);
    await page.getByRole('link', { name: 'View in graph' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Graph explorer' })).toBeVisible();
    const status = '14 CIs · 13 relationships · up to 3 hops from app-erp-db01, plus redundancy groups';
    await page.waitForTimeout(2000);
    await shot(page, 'graph-first');
    await expect(page.getByText(status), problems.join('\n')).toBeVisible();
    await expect(page.getByRole('img', { name: /^Graph of 14 CIs around app-erp-db01/ })).toBeVisible();
    await expect(page.getByRole('complementary', { name: 'app-erp-db01' })).toBeVisible();
    await expectAccessible(page, 'graph');
    await shot(page, 'graph');
    await useScheme(page, 'dark');
    await expectAccessible(page, 'graph (dark)');
    await shot(page, 'graph-dark');
    await useScheme(page, 'light');

    const png = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Export PNG' }).click();
    expect((await png).suggestedFilename()).toBe('graph-app-erp-db01-3-hops.png');

    // The table view: every relationship, the second replica at hop 4.
    await page.getByRole('button', { name: 'Switch to table view (same relationships)' }).click();
    await expect(page).toHaveURL(/view=table/);
    await expect(
      page.getByText('13 relationships within 3 hops of app-erp-db01, plus redundancy groups'),
    ).toBeVisible();
    await expect(page.getByRole('row', { name: /AG-ERP replica_on SQLPRD02\\ERP AG-ERP 4/ })).toBeVisible();
    await expect(page.getByText('Showing 13 of 13')).toBeVisible();
    await expectAccessible(page, 'graph table view');
    await shot(page, 'graph-table');

    // Export CSV…: screen 16's confirmation, then the server's file.
    await page.getByRole('button', { name: 'Export CSV…' }).click();
    const confirm = page.getByRole('dialog', { name: 'Export 13 relationships?' });
    await expect(
      confirm.getByText('3 hops from app-erp-db01 · Both · plus redundancy groups · retired CIs left out'),
    ).toBeVisible();
    await expect(confirm.getByText('The export is recorded in the audit log.')).toBeVisible();
    await expectAccessible(page, 'export dialog');
    await shot(page, 'graph-export');
    const csv = page.waitForEvent('download');
    await confirm.getByRole('button', { name: 'Export', exact: true }).click();
    expect((await csv).suggestedFilename()).toBe('graph-app-erp-db01-3-hops.csv');

    // The audit log names the relationships it recorded, and the export.
    await page.goto('/audit?action=relationship.deleted');
    await expect(page.getByText('esx-mx-014 uplinks_to sw-core-03')).toBeVisible();
    await page
      .getByRole('button', { name: /^Show details for entry/ })
      .first()
      .click();
    await expect(page.getByRole('button', { name: 'Show all entries about this relationship' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Open sw-core-03' })).toBeVisible();
    await expectAccessible(page, 'audit log, relationship entry');
    await shot(page, 'audit');
    await page.goto('/audit?action=relationship.exported');
    await expect(page.getByRole('cell', { name: 'Relationships exported' }).first()).toBeVisible();

    // Show retired: Payroll comes back, marked; and a host's 12 VMs drawn as one counted group.
    await page.goto(`/graph?ci=${ids['app-erp-db01']}&retired=true`);
    await expect(
      page.getByText('15 CIs · 14 relationships · up to 3 hops from app-erp-db01, plus redundancy groups'),
    ).toBeVisible();
    await shot(page, 'graph-retired');
    const host = await post(page, '/api/v1/cis', { type: 'HypervisorHost', name: 'esx-mx-020' });
    for (let i = 1; i <= 12; i++) {
      const vm = await post(page, '/api/v1/cis', {
        type: 'VirtualMachine',
        name: `vdi-mx-${String(i).padStart(2, '0')}`,
      });
      await post(page, '/api/v1/relationships', { type: 'runs_on', from: vm, to: host });
    }
    await page.goto(`/graph?ci=${host}`);
    await expect(
      page.getByText('13 CIs (12 in 1 group) · 12 relationships · up to 3 hops from esx-mx-020'),
    ).toBeVisible();
    await expectAccessible(page, 'graph with a group');
    await shot(page, 'graph-groups');
    // Grouping off: the 12 VMs drawn one by one in Hierarchical's row under their host.
    await page.goto(`/graph?ci=${host}&group=false`);
    await expect(page.getByText('13 CIs · 12 relationships · up to 3 hops from esx-mx-020')).toBeVisible();
    await shot(page, 'graph-flat');

    // No focus, and a CI that doesn't exist.
    await page.goto('/graph');
    await expect(page.getByRole('heading', { level: 2, name: 'Choose a CI to explore' })).toBeVisible();
    await page.goto('/graph?ci=0192f3a4-7c1e-7d40-9a51-2b7e0c9d1f99');
    await expect(page.getByRole('heading', { level: 2, name: 'We can’t find that CI' })).toBeVisible();
    await expectAccessible(page, 'graph, not found');
  });

  test('each section of the tab pages on its own, so a second Show more never sends the same page twice', async ({
    page,
  }) => {
    await signIn(page);
    // A CI with 51 relationships each way: 50 a section, then "Show 1 more".
    const pager = await post(page, '/api/v1/cis', { type: 'BusinessService', name: 'pager' });
    for (let start = 1; start <= 51; start += 17) {
      await Promise.all(
        Array.from({ length: Math.min(17, 52 - start) }, async (_, i) => {
          const n = String(start + i).padStart(2, '0');
          const dependency = await post(page, '/api/v1/cis', { type: 'BusinessService', name: `pager-dep-${n}` });
          const dependent = await post(page, '/api/v1/cis', { type: 'BusinessService', name: `pager-svc-${n}` });
          await post(page, '/api/v1/relationships', { type: 'depends_on', from: pager, to: dependency });
          await post(page, '/api/v1/relationships', { type: 'depends_on', from: dependent, to: pager });
        }),
      );
    }
    await page.goto(`/ci/${pager}?tab=relationships`);
    const dependencies = page.getByRole('region', { name: 'Depends on (51)' });
    const dependents = page.getByRole('region', { name: 'Depended on by (51)' });
    await expect(dependencies.getByRole('row')).toHaveCount(51);

    // The next pages answer slowly, so both are still on their way when the second button is pressed.
    await page.route(/\/api\/v1\/cis\/[^/]+\/relationships\?.*cursor=/, async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 2_500));
      await route.continue();
    });
    await dependencies.getByRole('button', { name: /Show 1 more/ }).click();
    await dependents.getByRole('button', { name: /Show 1 more/ }).click();
    await expect(dependencies.getByRole('button', { name: /Show 1 more/ })).toBeDisabled();
    await expect(dependents.getByRole('button', { name: /Show 1 more/ })).toBeDisabled();

    // Each section ends with its 51 rows (and its header row), every one once.
    for (const section of [dependencies, dependents]) {
      await expect(section.getByRole('row')).toHaveCount(52);
      await expect(section.getByRole('button', { name: /more/ })).toHaveCount(0);
      const names = await section.getByRole('link').allTextContents();
      expect(new Set(names).size).toBe(51);
    }
  });
});
