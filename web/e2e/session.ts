import AxeBuilder from '@axe-core/playwright';
import { createHmac } from 'node:crypto';
import { expect, type Page } from '@playwright/test';

// Shared by the specs that sign in (auth.spec.ts, relationships.spec.ts): the administrator the Compose smoke
// set up, its TOTP codes (one module, so every spec in the worker moves past the steps already used), the
// axe and CSP check, and the colour scheme switch.

export const admin = {
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
export function totp(key: string, step: number): string {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const hash = createHmac('sha1', base32(key)).update(counter).digest();
  const offset = (hash[hash.length - 1] ?? 0) & 0x0f;
  const binary = hash.readUInt32BE(offset) & 0x7fffffff;
  return String(binary % 1_000_000).padStart(6, '0');
}

export async function recordViolations(page: Page) {
  await page.addInitScript(() => {
    window.__cspViolations = [];
    document.addEventListener('securitypolicyviolation', (e) => {
      window.__cspViolations?.push(`${e.effectiveDirective} blocked ${e.blockedURI || '(inline)'}`);
    });
  });
}

/** The arrow at the right of a Fluent Combobox (the class Fluent gives its `expandIcon` slot). */
export const COMBOBOX_ARROW = '.fui-Combobox__expandIcon';

/** The inline navigation: Fluent's NavDrawer rendered as an inline Drawer (the classes Fluent gives the two). */
export const INLINE_NAV = '.fui-NavDrawer.fui-InlineDrawer';

/** The inline navigation's width on the boards (DEC-039; `src/shell/AppShell.tsx`). */
export const INLINE_NAV_WIDTH = 260;

export async function expectAccessible(page: Page, what: string) {
  // Measure the settled page: Fluent's drawers and dialogs fade in (Web Animations).
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'));
  // The inline navigation keeps its width on every screen. As a flex item that hides its overflow it once shrank
  // beside wide content, to nothing beside a table: still in the accessibility tree, so axe and role queries saw
  // nothing wrong, and out of a pointer's reach. history.spec.ts checks that the classes still exist.
  // None below 1100 px (the rail), one from there, and never another width. Each look is one reading of the
  // page; it is repeated until it settles, because just after the window narrows past 1100 px the inline
  // navigation is still on its way out and can measure 0 for a moment (seen once in a run). One that stays
  // collapsed still fails, after the wait.
  await expect
    .poll(
      async () =>
        (
          await page
            .locator(INLINE_NAV)
            .evaluateAll((navs) => navs.map((nav) => Math.round(nav.getBoundingClientRect().width)))
        ).filter((width) => width !== INLINE_NAV_WIDTH),
      { message: `${what}: the inline navigation's width` },
    )
    .toEqual([]);
  // No Combobox arrow on the screen is a Tab stop (DEC-040, src/ui/combobox.ts). This reads the
  // rendered attribute, so it also sees what the source check (tests/combobox-expand-icon.test.mjs)
  // can't: a later property that overrides the first. auth.spec.ts checks that the class still exists.
  expect(
    await page.locator(`${COMBOBOX_ARROW}:not([tabindex="-1"])`).count(),
    `${what}: Combobox arrows in the Tab order`,
  ).toBe(0);
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

/** Switches the colour scheme and waits until the app has applied it (the theme follows the system). */
export async function useScheme(page: Page, scheme: 'light' | 'dark') {
  await page.emulateMedia({ colorScheme: scheme });
  await page.waitForFunction((wanted) => document.documentElement.style.colorScheme === wanted, scheme);
  // Fluent fades some colours to the new theme (the navigation's items): measure after that.
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'));
}

/** A toast by its title. Fluent repeats the text in an aria-live announcer, so text alone matches twice. */
export function toast(page: Page, title: string) {
  return page.getByRole('listitem', { name: title, exact: true });
}

// The newest time step a sign-in in this run used. The server accepts a step once, and only newer
// ones after it, so the next sign-in starts after it.
let lastStep = 0;

export async function signIn(page: Page) {
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
