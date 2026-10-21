import AxeBuilder from '@axe-core/playwright';
import { createHmac } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';

// Slice 3 in a real browser against the Compose stack (tests/smoke/compose-smoke.sh completes
// first-run setup through the API first and passes the administrator's credentials): sign-in with a
// TOTP code, the app shell, Users & roles and the two-factor page, each checked with axe in both
// themes (NFR-A11Y-3) and with no CSP or Trusted Types violation (DEC-037).

const admin = {
  userName: process.env.GOLDMARK_E2E_ADMIN_USER ?? '',
  password: process.env.GOLDMARK_E2E_ADMIN_PASSWORD ?? '',
  totpKey: process.env.GOLDMARK_E2E_ADMIN_TOTP_KEY ?? '',
};

declare global {
  interface Window {
    __cspViolations?: string[];
  }
}

function base32(text: string): Buffer {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = 0;
  let value = 0;
  const bytes: number[] = [];
  for (const c of text.replace(/\s/g, '')) {
    value = (value << 5) | alphabet.indexOf(c);
    bits += 5;
    if (bits >= 8) {
      bytes.push((value >>> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  return Buffer.from(bytes);
}

/** RFC 6238: the 6-digit code for a 30-second step. */
function totp(key: string, step: number): string {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const hash = createHmac('sha1', base32(key)).update(counter).digest();
  const offset = (hash[hash.length - 1] ?? 0) & 0x0f;
  const binary = hash.readUInt32BE(offset) & 0x7fffffff;
  return String(binary % 1_000_000).padStart(6, '0');
}

async function recordViolations(page: Page) {
  await page.addInitScript(() => {
    window.__cspViolations = [];
    document.addEventListener('securitypolicyviolation', (e) => {
      window.__cspViolations?.push(`${e.effectiveDirective} blocked ${e.blockedURI || '(inline)'}`);
    });
  });
}

async function expectAccessible(page: Page, what: string) {
  // Measure the settled page: Fluent's drawers and dialogs fade in (Web Animations).
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'));
  const results = await new AxeBuilder({ page })
    // Fluent's focus management (Tabster) adds focus sentinels: <i tabindex="0" role="none"
    // aria-hidden="true" data-tabster-dummy> elements that hand focus on at once. Only they are excluded.
    .exclude('[data-tabster-dummy]')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(
    serious.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
    `${what}: axe`,
  ).toEqual([]);
  expect(await page.evaluate(() => window.__cspViolations), `${what}: CSP`).toEqual([]);
}

// The newest time step a sign-in in this run used. The server accepts a step once, and only newer
// ones after it, so the next sign-in starts after it.
let lastStep = 0;

async function signIn(page: Page) {
  await page.goto('/sign-in');
  await page.getByRole('textbox', { name: 'Username' }).fill(admin.userName);
  await page.getByRole('textbox', { name: 'Password', exact: true }).fill(admin.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Two-factor authentication' })).toBeVisible();
  // The current step or a newer one than the last sign-in used. The server accepts one step either
  // side of its own, so a step two ahead waits for the next 30-second boundary.
  const first = Math.max(Math.floor(Date.now() / 30000), lastStep + 1);
  const wait = (first - 1) * 30000 - Date.now();
  if (wait > 0) await page.waitForTimeout(wait + 500);
  // That step, or the next if the setup through the API already used it (a code works once).
  for (const step of [first, first + 1]) {
    lastStep = step;
    await page.getByRole('textbox', { name: '6-digit code' }).fill(totp(admin.totpKey, step));
    await page.getByRole('button', { name: 'Verify' }).click();
    const outcome = await Promise.race([
      page
        .getByRole('heading', { level: 1, name: 'Dashboard' })
        .waitFor()
        .then(() => 'in'),
      page
        .getByText('That code didn’t work')
        .waitFor()
        .then(() => 'retry'),
    ]);
    if (outcome === 'in') return;
  }
  throw new Error('Could not sign in with a TOTP code');
}

test.describe('slice 3 screens', () => {
  test.use({ reducedMotion: 'reduce' });
  test.skip(!admin.userName || !admin.password || !admin.totpKey, 'needs GOLDMARK_E2E_ADMIN_* from compose-smoke.sh');

  test('sign-in, setup-complete and invalid-link pages are accessible in light and dark', async ({ page }) => {
    await recordViolations(page);
    for (const scheme of ['light', 'dark'] as const) {
      await page.emulateMedia({ colorScheme: scheme });
      await page.goto('/sign-in');
      await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
      await expectAccessible(page, `sign-in (${scheme})`);

      await page.goto('/setup');
      await expect(page.getByRole('heading', { level: 1, name: 'Setup is already complete' })).toBeVisible();
      await expectAccessible(page, `setup complete (${scheme})`);

      await page.goto('/invite#t=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA');
      await expect(page.getByRole('heading', { level: 1, name: 'This link can’t be used' })).toBeVisible();
      await expectAccessible(page, `invalid link (${scheme})`);
    }
  });

  test('a wrong password says only "incorrect"', async ({ page }) => {
    await recordViolations(page);
    await page.goto('/sign-in');
    await page.getByRole('textbox', { name: 'Username' }).fill(admin.userName);
    await page.getByRole('textbox', { name: 'Password', exact: true }).fill('not the right password');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByText('Username or password is incorrect.')).toBeVisible();
    await expectAccessible(page, 'sign-in error');
  });

  test('the administrator signs in with a code and uses Users & roles and the two-factor page', async ({ page }) => {
    await recordViolations(page);
    await signIn(page);
    await expectAccessible(page, 'dashboard');

    await page.getByRole('link', { name: 'Users & roles' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'Users & roles' })).toBeVisible();
    await expect(page.getByRole('grid', { name: 'Users' }).or(page.getByText('You’re the only user.'))).toBeVisible();
    await expectAccessible(page, 'users');

    await page.getByRole('button', { name: 'Add user' }).first().click();
    await expect(page.getByRole('heading', { name: 'Add user' })).toBeVisible();
    await expectAccessible(page, 'add user panel');
    await page.getByRole('button', { name: 'Cancel' }).click();

    await page.emulateMedia({ colorScheme: 'dark' });
    await page.getByRole('button', { name: /^Account:/ }).click();
    await page.getByRole('menuitem', { name: 'Two-factor authentication' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Two-factor authentication' })).toBeVisible();
    await expect(page.getByText('Two-factor authentication is on')).toBeVisible();
    await expectAccessible(page, 'two-factor page');

    // Sent here after a lost key ring (DEC-038 item 5; the server side has its own test): a transient
    // failure keeps the way to set up the authenticator again, with Retry (Codex, PR #24).
    const enrolKey = '**/api/v1/enrolment/authenticator';
    const enrolQr = '**/api/v1/enrolment/authenticator.svg*';
    await page.route(enrolKey, (route) => route.abort());
    await page.goto('/account/two-factor?authenticator=new');
    await expect(page.getByText(/couldn’t start setting up your authenticator app again/)).toBeVisible();
    await page.unroute(enrolKey);
    await page.route(enrolKey, (route) => route.fulfill({ json: { key: 'JBSWY3DPEHPK3PXP' } }));
    await page.route(enrolQr, (route) =>
      route.fulfill({
        contentType: 'image/svg+xml',
        body: '<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"/>',
      }),
    );
    await page.getByRole('button', { name: 'Retry' }).click();
    await expect(page.getByText(/can no longer check codes from your authenticator app/)).toBeVisible();
    await page.getByRole('button', { name: 'Cancel' }).click();
    await page.unroute(enrolKey);
    await page.unroute(enrolQr);

    // A sign-out that never reaches the server leaves the session alive, so it must not say "signed
    // out" (Codex, PR #24): the person stays signed in, sees why, and can retry.
    await page.route('**/api/v1/auth/sign-out', (route) => route.abort());
    await page.getByRole('button', { name: /^Account:/ }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();
    await expect(page.getByText(/You’re still signed in/)).toBeVisible();
    await expectAccessible(page, 'sign-out failed');
    await page.reload();
    await expect(page.getByRole('heading', { level: 1, name: 'Two-factor authentication' })).toBeVisible();
    await page.getByRole('button', { name: /^Account:/ }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();
    await expect(page.getByText(/You’re still signed in/)).toBeVisible();
    await page.unroute('**/api/v1/auth/sign-out');
    await page.getByRole('button', { name: 'Retry' }).click();
    await expect(page.getByText('You’re signed out.')).toBeVisible();
    await page.goto('/users');
    await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  });
});

test.describe('slice 4 screen', () => {
  test.use({ reducedMotion: 'reduce', acceptDownloads: true });
  // A sign-in may wait up to 30 seconds for a TOTP step newer than the previous test's.
  test.describe.configure({ timeout: 120_000 });
  test.skip(!admin.userName || !admin.password || !admin.totpKey, 'needs GOLDMARK_E2E_ADMIN_* from compose-smoke.sh');

  test('the administrator reads, filters and exports the audit log', async ({ page }) => {
    await recordViolations(page);
    await signIn(page);

    await page.getByRole('link', { name: 'Audit log' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'Audit log' })).toBeVisible();
    await expect(page.getByText(/The audit log is append-only/)).toBeVisible();
    await expect(page.getByText(/^Showing \d+ of [\d,]+ entr(y|ies)$/)).toBeVisible();

    // This sign-in is the newest entry; its details open by keyboard-reachable button.
    const first = page.getByRole('button', { name: /^Show details for the entry at/ }).first();
    await first.click();
    await expect(first).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByText('Entry number')).toBeVisible();
    await expect(page.getByText('auth.sign-in.succeeded').first()).toBeVisible();
    await expectAccessible(page, 'audit log');

    // Filters live in the page address.
    await page.getByRole('combobox', { name: 'Action' }).click();
    // A multiselect Dropdown lists its options as menu item checkboxes.
    await page.getByRole('menuitemcheckbox', { name: 'Signed in', exact: true }).click();
    await page.keyboard.press('Escape');
    await expect(page).toHaveURL(/action=auth\.sign-in\.succeeded/);
    await expect(page.getByRole('button', { name: 'Clear filters' })).toBeVisible();
    await page.emulateMedia({ colorScheme: 'dark' });
    await expectAccessible(page, 'audit log, filtered (dark)');

    // An export downloads the CSV and is recorded in the log itself.
    await page.getByRole('button', { name: 'Export CSV…' }).click();
    await expect(page.getByRole('dialog', { name: /^Export [\d,]+ entr(y|ies)\?$/ })).toBeVisible();
    await expect(page.getByText('The export is recorded in the audit log.')).toBeVisible();
    await expectAccessible(page, 'export dialog (dark)');
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Export', exact: true }).click();
    expect((await download).suggestedFilename()).toMatch(/^goldmark-audit-\d{8}\.csv$/);
    await expect(page.getByText(/^Exported [\d,]+ entr(y|ies)$/)).toBeVisible();

    await page.goto('/audit');
    await expect(page.getByRole('row', { name: /Audit log exported/ }).first()).toBeVisible();

    // An unknown range in the address, even an Object property name, falls back to the default.
    await page.goto('/audit?range=constructor');
    await expect(page.getByRole('heading', { level: 1, name: 'Audit log' })).toBeVisible();
    await expect(page.getByText(/^Showing \d+ of [\d,]+ entr(y|ies)$/)).toBeVisible();
  });

  test('an answer for a replaced list never reaches the new one', async ({ page }) => {
    // Codex, PR #32: list answers are held and fulfilled with sample entries, so the races are exact.
    await signIn(page);
    const entry = (id: number, label: string) => ({
      id,
      at: '2026-10-20T07:58:12Z',
      actor: { type: 'System', id: null, userName: null, displayName: null, removed: false },
      action: 'audit.retention.purged',
      label,
      outcome: 'Succeeded',
      target: null,
      ip: null,
      details: null,
    });
    const isList = (url: URL) => url.pathname === '/api/v1/audit';

    // 1. A first page still in flight must not replace the empty state of an incomplete custom range.
    const firstArrived = deferred();
    const firstHeld = deferred();
    await page.route(isList, async (route) => {
      firstArrived.resolve();
      await firstHeld.promise;
      await route.fulfill({ json: { entries: [entry(9001, 'Stale entry')], total: 1, next: null } });
    });
    await page.goto('/audit');
    await firstArrived.promise;
    await page.getByRole('combobox', { name: 'Time range (UTC)' }).click();
    await page.getByRole('option', { name: 'Custom range…' }).click();
    await page.getByLabel('From (UTC)').fill('2026-10-01T00:00');
    await expect(page.getByText('No entries match these filters.')).toBeVisible();
    const stale = page.waitForResponse((response) => isList(new URL(response.url())));
    firstHeld.resolve();
    await stale;
    await page.waitForTimeout(500);
    await expect(page.getByRole('row', { name: /Stale entry/ })).toHaveCount(0);
    await expect(page.getByText('No entries match these filters.')).toBeVisible();
    await page.unroute(isList);

    // 2. A "Load more" still in flight belongs to the list it continued: the new list's button works.
    const moreArrived = deferred();
    const moreHeld = deferred();
    await page.route(isList, async (route) => {
      const query = new URL(route.request().url()).searchParams;
      if (query.has('cursor')) {
        moreArrived.resolve();
        await moreHeld.promise;
        await route.fulfill({ json: { entries: [entry(8001, 'Stale continuation')], total: 2, next: null } });
      } else if (query.get('outcome') === 'Failed') {
        await route.fulfill({ json: { entries: [entry(7002, 'New list entry')], total: 2, next: 7002 } });
      } else {
        await route.fulfill({ json: { entries: [entry(7001, 'Old list entry')], total: 2, next: 7001 } });
      }
    });
    await page.goto('/audit');
    await expect(page.getByRole('row', { name: /Old list entry/ })).toBeVisible();
    await page.getByRole('button', { name: 'Load more' }).click();
    await moreArrived.promise;
    await page.getByRole('combobox', { name: 'Outcome' }).click();
    await page.getByRole('option', { name: 'Failed' }).click();
    await expect(page.getByRole('row', { name: /New list entry/ })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Load more' })).toBeEnabled();
    const continuation = page.waitForResponse((response) => new URL(response.url()).searchParams.has('cursor'));
    moreHeld.resolve();
    await continuation;
    await page.waitForTimeout(500);
    await expect(page.getByRole('row', { name: /Stale continuation/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Load more' })).toBeEnabled();
    await page.unroute(isList);
  });
});

/** A promise the test resolves itself, to hold a request until the page is in the state under test. */
function deferred() {
  let resolve = () => {};
  const promise = new Promise<void>((done) => {
    resolve = done;
  });
  return { promise, resolve: () => resolve() };
}
