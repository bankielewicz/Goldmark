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

async function signIn(page: Page) {
  await page.goto('/sign-in');
  await page.getByRole('textbox', { name: 'Username' }).fill(admin.userName);
  await page.getByRole('textbox', { name: 'Password', exact: true }).fill(admin.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Two-factor authentication' })).toBeVisible();
  // The current step, or the next if that code was already used (a code works once).
  const now = Math.floor(Date.now() / 30000);
  for (const step of [now, now + 1]) {
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
});
