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

    await page.getByRole('button', { name: /^Account:/ }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();
    await expect(page.getByText('You’re signed out.')).toBeVisible();
    await page.goto('/users');
    await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  });
});
