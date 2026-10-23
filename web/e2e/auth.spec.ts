import AxeBuilder from '@axe-core/playwright';
import { createHmac } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';

// Slices 3, 4 and 4a in a real browser against the Compose stack (tests/smoke/compose-smoke.sh
// completes first-run setup through the API first and passes the administrator's credentials):
// sign-in with a TOTP code, the app shell, Users & roles, the two-factor page, the audit log and a
// password reset end to end, each checked with axe in both themes (NFR-A11Y-3) and with no CSP or
// Trusted Types violation (DEC-037).

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

/** Switches the colour scheme and waits until the app has applied it (the theme follows the system). */
async function useScheme(page: Page, scheme: 'light' | 'dark') {
  await page.emulateMedia({ colorScheme: scheme });
  await page.waitForFunction((wanted) => document.documentElement.style.colorScheme === wanted, scheme);
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
    await page.getByRole('button', { name: /Account:/ }).click();
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
    await page.getByRole('button', { name: /Account:/ }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();
    await expect(page.getByText(/You’re still signed in/)).toBeVisible();
    await expectAccessible(page, 'sign-out failed');
    await page.reload();
    await expect(page.getByRole('heading', { level: 1, name: 'Two-factor authentication' })).toBeVisible();
    await page.getByRole('button', { name: /Account:/ }).click();
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
    const first = page.getByRole('button', { name: /^Show details for entry \d+: Signed in at / }).first();
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

    // A year the server refuses, as a date field passes through while a year is typed: the field
    // says so and nothing is requested (no error bar).
    await page.goto('/audit?range=custom&from=0202-10-01T00:00&to=2026-10-02T00:00');
    await expect(page.getByText('Use a year from 2000 to 9998.')).toBeVisible();
    await expect(page.getByText('Couldn’t load the audit log.')).toHaveCount(0);
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

test.describe('slice 4a screens', () => {
  test.use({ reducedMotion: 'reduce' });
  // Two sign-ins and two codes from a new authenticator, each waiting for a fresh TOTP step.
  test.describe.configure({ timeout: 240_000 });
  test.skip(!admin.userName || !admin.password || !admin.totpKey, 'needs GOLDMARK_E2E_ADMIN_* from compose-smoke.sh');

  test('an administrator resets a password and the person sets a new one with their authenticator', async ({
    page,
    browser,
  }) => {
    await recordViolations(page);
    await page.goto('/sign-in');
    await expect(page.getByText('Forgot your password? Ask an administrator to reset it.')).toBeVisible();
    await signIn(page);

    // Change password from the account menu: the dialog and its checks (nothing is sent).
    await page.getByRole('button', { name: /Account:/ }).click();
    await page.getByRole('menuitem', { name: 'Change password…' }).click();
    const change = page.getByRole('dialog', { name: 'Change your password' });
    await expect(change).toBeVisible();
    await change.getByRole('textbox', { name: 'New password', exact: true }).fill('a long new passphrase 2026');
    await change.getByRole('textbox', { name: 'Confirm new password' }).fill('a long new passphrase 2027');
    await change.getByRole('button', { name: 'Change password' }).click();
    await expect(change.getByText('Enter your current password.')).toBeVisible();
    await expect(change.getByText('The passwords don’t match.')).toBeVisible();
    await expectAccessible(page, 'change password dialog');
    await change.getByRole('button', { name: 'Cancel' }).click();

    // A link opened before another Admin approves it (DEC-042) shows no code field.
    await page.route('**/api/v1/links/check', (route) => route.fulfill({ json: { status: 'pending' } }));
    await page.goto('/invite#t=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA');
    await expect(page.getByRole('heading', { level: 1, name: 'This link isn’t active yet' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Code' })).toHaveCount(0);
    await expectAccessible(page, 'link before approval');
    await page.unroute('**/api/v1/links/check');

    // An invitation: with one Admin, they get both the link and the code.
    await page.goto('/users');
    await page.getByRole('button', { name: 'Add user' }).first().click();
    await page.getByRole('textbox', { name: 'Display name' }).fill('Sam Rivera');
    await page.getByRole('textbox', { name: 'Username' }).fill('sam.rivera');
    await page.getByRole('button', { name: 'Create invitation' }).click();
    await expect(page.getByRole('heading', { name: 'Invitation for Sam Rivera' })).toBeVisible();
    await expect(page.getByText(/You’re the only Admin, so you get both the link and the code/)).toBeVisible();
    const invitation = await shownOnce(page);
    await page.getByRole('checkbox', { name: /^I sent the link/ }).check();
    await page.getByRole('button', { name: 'Done' }).click();

    const context = await browser.newContext({ baseURL: process.env.GOLDMARK_BASE_URL ?? '', reducedMotion: 'reduce' });
    const person = await context.newPage();
    await recordViolations(person);
    await person.goto(invitation.url);
    await person.getByRole('textbox', { name: 'Code' }).fill(invitation.code);
    await person.getByRole('button', { name: 'Continue' }).click();
    const firstPassword = 'sam rivera likes long walks 93';
    await person.getByRole('textbox', { name: 'Password', exact: true }).fill(firstPassword);
    await person.getByRole('textbox', { name: 'Confirm password' }).fill(firstPassword);
    await person.getByRole('button', { name: 'Continue' }).click();
    const alt = await person.getByRole('img', { name: /^QR code for your authenticator app/ }).getAttribute('alt');
    const key = (alt ?? '').split('The same key as text: ')[1] ?? '';
    const enrolled = Math.floor(Date.now() / 30000);
    await person.getByRole('textbox', { name: '6-digit code from the app' }).fill(totp(key, enrolled));
    await person.getByRole('button', { name: 'Verify' }).click();
    await person.getByRole('checkbox', { name: 'I have stored these codes safely' }).check();
    await person.getByRole('button', { name: 'Finish' }).click();
    await person.getByRole('button', { name: 'Go to Goldmark' }).click();
    await expect(person.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible();

    // The administrator resets Sam's password: the dialog, with and without two-factor, then the link.
    await page.reload();
    await page.getByRole('button', { name: 'Actions for Sam Rivera' }).click();
    await page.getByRole('menuitem', { name: 'Reset password…' }).click();
    const reset = page.getByRole('alertdialog', { name: 'Reset the password for Sam Rivera?' });
    await expect(reset).toBeVisible();
    await expectAccessible(page, 'reset password dialog');
    await reset.getByRole('checkbox', { name: /^Also reset two-factor/ }).check();
    await expect(reset.getByText(/their authenticator and recovery codes are removed/)).toBeVisible();
    await useScheme(page, 'dark');
    await expectAccessible(page, 'reset password dialog, two-factor too (dark)');
    await useScheme(page, 'light');
    await reset.getByRole('checkbox', { name: /^Also reset two-factor/ }).uncheck();
    await reset.getByRole('button', { name: 'Reset password' }).click();
    await expect(page.getByRole('heading', { name: 'Password reset link for Sam Rivera' })).toBeVisible();
    await expectAccessible(page, 'password reset link');
    const link = await shownOnce(page);
    expect(link.url).toContain('/password-reset#t=');
    await page.getByRole('checkbox', { name: /^I sent the link/ }).check();
    await page.getByRole('button', { name: 'Done' }).click();
    await expect(page.getByText(/^Active · password reset link expires in/)).toBeVisible();

    // Sam was signed out; the link, the code, a new password, then their own authenticator.
    await person.goto('/');
    await expect(person.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
    await person.goto(link.url);
    await person.getByRole('textbox', { name: 'Code' }).fill(link.code);
    await person.getByRole('button', { name: 'Continue' }).click();
    await expect(person.getByText('Choose a new password, Sam Rivera')).toBeVisible();
    const newPassword = 'a brand new passphrase 2026';
    await person.getByRole('textbox', { name: 'New password', exact: true }).fill(newPassword);
    await person.getByRole('textbox', { name: 'Confirm new password' }).fill(newPassword);
    await expectAccessible(person, 'link page, new password');
    await person.getByRole('button', { name: 'Continue' }).click();
    await expect(person.getByRole('heading', { level: 1, name: 'Confirm with your authenticator' })).toBeVisible();
    await useScheme(person, 'dark');
    await expectAccessible(person, 'link page, confirm with the authenticator (dark)');
    // A step after the enrolment's (a code works once); the server accepts one step ahead.
    const confirm = Math.max(Math.floor(Date.now() / 30000), enrolled + 1);
    await person.getByRole('textbox', { name: '6-digit code' }).fill(totp(key, confirm));
    await person.getByRole('button', { name: 'Change password' }).click();
    await expect(person.getByRole('heading', { level: 1, name: 'Your new password is set' })).toBeVisible();
    await expectAccessible(person, 'password reset done (dark)');
    await person.getByRole('button', { name: 'Go to Goldmark' }).click();
    await expect(person.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible();
    await context.close();

    await page.goto('/audit');
    await expect(page.getByRole('row', { name: /Password reset completed/ }).first()).toBeVisible();

    // The addendum (DEC-042 item 9), with answers shaped by hand: one Admin can't make a request that
    // needs another's approval. The nav counts requests to approve; the requester's own ended request
    // says so and offers "Ask again…".
    await page.route('**/api/v1/users/approvals', (route) => route.fulfill({ json: { waiting: 2 } }));
    // The real list, read by the page itself: only the browser trusts the stack's self-signed
    // certificate (pinned by its key), so route.fetch() from the test runner would be refused.
    const body = await page.evaluate(
      async () =>
        (await (await fetch('/api/v1/users', { headers: { Accept: 'application/json' } })).json()) as {
          users: { id: string; userName: string; displayName: string; account: { link: unknown } }[];
        },
    );
    const me = body.users.find((u) => u.userName === admin.userName);
    const sam = body.users.find((u) => u.userName === 'sam.rivera');
    if (me && sam) {
      sam.account.link = {
        purpose: 'combined-reset',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        expired: true,
        awaitingApproval: true,
        requestedBy: {
          id: me.id,
          displayName: me.displayName,
          askedAt: new Date(Date.now() - 86_460_000).toISOString(),
        },
      };
    }
    await page.route('**/api/v1/users', (route) => route.fulfill({ json: body }));
    await page.goto('/users');
    const nav = page.getByRole('link', { name: 'Users & roles, 2 requests need your approval' });
    await expect(nav).toBeVisible();
    await expect(page.getByText('Password reset approval request ended')).toBeVisible();
    await page.getByRole('button', { name: 'Actions for Sam Rivera' }).click();
    await expect(page.getByRole('menuitem', { name: 'Ask again…' })).toBeVisible();
    await expectAccessible(page, 'requester rows and nav count');
    await page.keyboard.press('Escape');
    await useScheme(page, 'dark');
    await expectAccessible(page, 'nav count (dark)');
    await page.setViewportSize({ width: 1024, height: 768 });
    await expect(nav).toBeVisible();
    await expectAccessible(page, 'nav count on the 1024 rail (dark)');
    await page.unroute('**/api/v1/users');
    await page.unroute('**/api/v1/users/approvals');
  });
});

/** The shown-once panel's link and code (screen 17). */
async function shownOnce(page: Page): Promise<{ url: string; code: string }> {
  const url = (await page.getByText(/^https:\/\/.+#t=/).textContent()) ?? '';
  const code = (await page.getByText(/^[A-Z0-9]{3,5}-[A-Z0-9]{3,5}$/).textContent()) ?? '';
  expect(url, 'link').not.toBe('');
  expect(code, 'code').not.toBe('');
  return { url, code };
}

/** A promise the test resolves itself, to hold a request until the page is in the state under test. */
function deferred() {
  let resolve = () => {};
  const promise = new Promise<void>((done) => {
    resolve = done;
  });
  return { promise, resolve: () => resolve() };
}
