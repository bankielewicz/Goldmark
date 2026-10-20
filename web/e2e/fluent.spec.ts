import { existsSync, readFileSync } from 'node:fs';
import { basename, extname, join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';

import { CONTROL_DIRECTIVES } from './fluent-check/app/controls';

// Fluent UI v9 under the served CSP and Trusted Types (DEC-017, DEC-037).
//
// Default (CI) mode: the real server sends the real SPA shell (its HTML, its fresh style
// nonce and its headers); only the shell's script is swapped for the sampler's, which reads
// the nonce exactly as the product does (src/styleNonce.ts). Qualification mode
// (GOLDMARK_FLUENT_CHECK_DIRECT=1): GOLDMARK_BASE_URL is a real server whose web root is the
// sampler build, so nothing is swapped. The page's own self-check (fluent-check/app/selfcheck.tsx)
// does the CSP checks; this test adds keyboard and pointer behaviour and checks the final
// violation list exactly.

declare global {
  interface Window {
    __fluentCheckViolations?: string[];
    __fluentCheckReport?: { pass: boolean; checks: { name: string; pass: boolean; detail: string }[] };
  }
}

const direct = process.env.GOLDMARK_FLUENT_CHECK_DIRECT === '1';

const CONTENT_TYPES: Record<string, string> = {
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.woff2': 'font/woff2',
  '.woff': 'font/woff',
  '.ttf': 'font/ttf',
};

/** Serves the sampler's assets; the shell's entry script is answered with the sampler's entry. */
async function swapInSampler(page: Page) {
  const dist = process.env.GOLDMARK_FLUENT_CHECK_DIST;
  if (!dist || !existsSync(join(dist, 'index.html'))) {
    throw new Error('The Fluent UI sampler was not built (e2e/fluent-check/build.ts is the global setup).');
  }
  const entry = readFileSync(join(dist, 'index.html'), 'utf8').match(
    /<script type="module" crossorigin src="\/assets\/([^"]+\.js)">/,
  )?.[1];
  if (!entry) throw new Error('No module entry in the sampler build.');
  const assets = join(dist, 'assets');

  await page.route('**/assets/*', async (route) => {
    const name = basename(new URL(route.request().url()).pathname);
    const own = existsSync(join(assets, name)) ? name : route.request().resourceType() === 'script' ? entry : null;
    const contentType = own ? CONTENT_TYPES[extname(own)] : undefined;
    if (!own || !contentType) {
      await route.continue();
      return;
    }
    await route.fulfill({
      status: 200,
      headers: { 'content-type': contentType, 'x-content-type-options': 'nosniff' },
      body: readFileSync(join(assets, own)),
    });
  });
}

const headerNonce = (csp: string) => csp.match(/(?:^|; )style-src 'self' 'nonce-([A-Za-z0-9_-]{22,})';/)?.[1];

const violationDirectives = (page: Page) =>
  page.evaluate(() => (window.__fluentCheckViolations ?? []).map((v) => v.split(' ', 1)[0]).sort());

async function expectFocusTrapped(page: Page, container: string) {
  for (const key of ['Tab', 'Tab', 'Tab', 'Tab', 'Tab', 'Shift+Tab', 'Shift+Tab', 'Shift+Tab', 'Shift+Tab']) {
    await page.keyboard.press(key);
    expect(
      await page.evaluate(
        (selector) => document.querySelector(selector)?.contains(document.activeElement) ?? false,
        container,
      ),
      `focus left ${container} after ${key}`,
    ).toBe(true);
  }
}

test('Fluent UI v9 works under the served CSP with a per-response style nonce', async ({ page }) => {
  test.setTimeout(90_000);
  const errors: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') errors.push(m.text());
  });
  page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`));
  if (!direct) await swapInSampler(page);

  const response = await page.goto(direct ? '/' : '/__fluent-check');
  expect(response?.status()).toBe(200);
  expect(response?.headers()['cache-control']).toBe('no-store');
  const csp = response?.headers()['content-security-policy'] ?? '';
  const nonce = headerNonce(csp);
  expect(nonce, `no style nonce in ${csp}`).toBeTruthy();
  expect(csp).toContain("script-src 'self';");
  expect(csp).toContain("require-trusted-types-for 'script'");
  expect(csp).toContain("trusted-types 'none'");
  expect(csp).not.toContain('unsafe-inline');
  expect(csp.match(/'nonce-/g)).toHaveLength(1);

  // The page's self-check: nonce, applied styles, keyframes, fonts, theme switch, portals,
  // right-to-left, zero violations in normal use, then the positive controls.
  await expect(page.getByTestId('self-check')).not.toHaveAttribute('data-status', 'running', { timeout: 30_000 });
  const report = await page.evaluate(() => window.__fluentCheckReport);
  test.info().annotations.push({ type: 'self-check', description: JSON.stringify(report) });
  expect(report?.checks.filter((c) => !c.pass)).toEqual([]);
  expect(report?.pass).toBe(true);

  // The header, the page's nonce attribute and every Griffel style element agree.
  const onPage = await page.evaluate(() => ({
    meta: document.querySelector<HTMLMetaElement>('meta[property="csp-nonce"]')?.nonce,
    styles: [...document.querySelectorAll('style')].map((s) => s.nonce),
  }));
  expect(onPage.meta).toBe(nonce);
  expect(onPage.styles.length).toBeGreaterThan(0);
  expect(new Set(onPage.styles)).toEqual(new Set([nonce]));

  // Keyboard: the dialog and the drawer trap focus and give it back.
  const openDialog = page.getByRole('button', { name: 'Open dialog' });
  await openDialog.click();
  await expect(page.getByRole('dialog', { name: 'Retire CI' })).toBeVisible();
  await expectFocusTrapped(page, '[data-testid="dialog-surface"]');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(openDialog).toBeFocused();

  const openDrawer = page.getByRole('button', { name: 'Open drawer' });
  await openDrawer.click();
  await expect(page.getByTestId('drawer')).toBeVisible();
  await expectFocusTrapped(page, '[data-testid="drawer"]');
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('drawer')).toBeHidden();
  await expect(openDrawer).toBeFocused();

  const openMenu = page.getByRole('button', { name: 'Open menu' });
  await openMenu.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('menuitem', { name: 'Edit' })).toBeFocused();
  await page.keyboard.press('ArrowDown');
  await expect(page.getByRole('menuitem', { name: 'Retire' })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(openMenu).toBeFocused();

  // Pointer: tooltip, combobox, sortable grid.
  await page.getByRole('button', { name: 'Tooltip target' }).hover();
  await expect(page.getByText('Tooltip content')).toBeVisible();
  await page.mouse.move(0, 0);
  await page.getByRole('combobox').click();
  await page.getByRole('option', { name: 'VirtualMachine' }).click();
  await expect(page.getByRole('combobox')).toHaveValue('VirtualMachine');
  await page.getByRole('columnheader', { name: 'Name' }).click();
  await expect(page.getByRole('columnheader', { name: 'Name' })).toHaveAttribute('aria-sort', /ascending|descending/);

  // Exactly the controls' violations, nothing during normal use or later. The controls also
  // log their own CSP errors; anything else on the console is a failure.
  expect(await violationDirectives(page)).toEqual([...CONTROL_DIRECTIVES]);
  expect(errors.filter((e) => !/Content Security Policy|TrustedTypePolicy/.test(e))).toEqual([]);

  // A reload is a new response with a new nonce.
  const again = await page.reload();
  const second = headerNonce(again?.headers()['content-security-policy'] ?? '');
  expect(second).toBeTruthy();
  expect(second).not.toBe(nonce);
  expect(await page.evaluate(() => document.querySelector<HTMLMetaElement>('meta[property="csp-nonce"]')?.nonce)).toBe(
    second,
  );
});
