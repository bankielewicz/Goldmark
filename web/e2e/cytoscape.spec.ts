import { readFileSync } from 'node:fs';
import { expect, test, type Download, type Page } from '@playwright/test';

import { CONTROL_DIRECTIVES } from './cytoscape-check/app/controls';
import { headerNonce, swapInSampler } from './sampler-swap';

// Cytoscape.js 3.34.4 under the served CSP and Trusted Types (DEC-046 item 3), before screen 6 is
// built on it. The real server sends the real SPA shell (its HTML, its fresh style nonce and its
// headers); only the shell's script is swapped for the sampler's (e2e/cytoscape-check/app), which
// pre-creates Cytoscape's stylesheet with src/graph/cytoscapeStylesheet.ts exactly as the product
// will. The page's own self-check does the CSP checks; this test adds the two downloads and checks
// the final violation list exactly.

declare global {
  interface Window {
    __cytoscapeCheckViolations?: string[];
    __cytoscapeCheckReport?: { pass: boolean; checks: { name: string; pass: boolean; detail: string }[] };
  }
}

const violationDirectives = (page: Page) =>
  page.evaluate(() => (window.__cytoscapeCheckViolations ?? []).map((v) => v.split(' ', 1)[0]).sort());

async function downloadOf(page: Page, button: string): Promise<Download> {
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.getByRole('button', { name: button }).click(),
  ]);
  return download;
}

test('Cytoscape.js works under the served CSP with its stylesheet pre-created', async ({ page }) => {
  test.setTimeout(90_000);
  const errors: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') errors.push(m.text());
  });
  page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`));
  await swapInSampler(page, 'GOLDMARK_CYTOSCAPE_CHECK_DIST', 'e2e/cytoscape-check/build.ts');

  const response = await page.goto('/__cytoscape-check');
  expect(response?.status()).toBe(200);
  const csp = response?.headers()['content-security-policy'] ?? '';
  const nonce = headerNonce(csp);
  expect(nonce, `no style nonce in ${csp}`).toBeTruthy();
  expect(csp).toContain("script-src 'self';");
  expect(csp).toContain("img-src 'self'");
  expect(csp).toContain("require-trusted-types-for 'script'");
  expect(csp).toContain("trusted-types 'none'");
  expect(csp).not.toContain('unsafe-inline');

  await expect(page.getByTestId('self-check')).not.toHaveAttribute('data-status', 'running', { timeout: 30_000 });
  const report = await page.evaluate(() => window.__cytoscapeCheckReport);
  test.info().annotations.push({ type: 'self-check', description: JSON.stringify(report) });
  expect(report?.checks.filter((c) => !c.pass)).toEqual([]);
  expect(report?.pass).toBe(true);

  // One stylesheet with the id, carrying the header's nonce, its value hidden from the DOM.
  const sheet = await page.evaluate(() => {
    const all = document.querySelectorAll<HTMLStyleElement>('#__________cytoscape_stylesheet');
    return { count: all.length, nonce: all[0]?.nonce, attribute: all[0]?.getAttribute('nonce') };
  });
  expect(sheet).toEqual({ count: 1, nonce, attribute: '' });

  // The downloads go through blob: URLs with no violation.
  const png = await downloadOf(page, 'Export PNG');
  expect(png.suggestedFilename()).toBe('graph-check.png');
  expect(
    readFileSync(await png.path())
      .subarray(1, 4)
      .toString('latin1'),
  ).toBe('PNG');
  const csv = await downloadOf(page, 'Export CSV');
  expect(csv.suggestedFilename()).toBe('graph-check.csv');
  expect(readFileSync(await csv.path(), 'utf8')).toContain('app-erp-db01,runs_on,esx-mx-014');

  // Exactly the controls' violations, nothing during normal use or after the downloads.
  expect(await violationDirectives(page)).toEqual([...CONTROL_DIRECTIVES]);
  expect(errors.filter((e) => !/Content Security Policy|Refused to/.test(e))).toEqual([]);
});
