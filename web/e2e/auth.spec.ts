import { expect, test, type Page } from '@playwright/test';

import { admin, expectAccessible, recordViolations, signIn, toast, totp, useScheme } from './session';

// Slices 3, 4, 4a, 5 and 5b in a real browser against the Compose stack (tests/smoke/compose-smoke.sh
// completes first-run setup through the API first and passes the administrator's credentials):
// sign-in with a TOTP code, the app shell, Users & roles, the two-factor page, the audit log, a
// password reset end to end, the CI search, detail and panel (slice 5) and Types & attributes (slice
// 5b), each checked with axe in both themes (NFR-A11Y-3) and with no CSP or Trusted Types violation
// (DEC-037).

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

test.describe('slice 5 screens', () => {
  test.use({ reducedMotion: 'reduce' });
  test.describe.configure({ timeout: 120_000 });
  test.skip(!admin.userName || !admin.password || !admin.totpKey, 'needs GOLDMARK_E2E_ADMIN_* from compose-smoke.sh');

  test('an administrator searches, creates, edits, retires and restores a CI, and an unknown ID is a friendly page', async ({
    page,
  }) => {
    await recordViolations(page);
    await signIn(page);
    const name = `e2e-team-${Date.now()}`;

    // Inventory > Search, from the nav; the top-bar search isn't shown on the page that has its own.
    await page.getByRole('link', { name: 'Search' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'Search' })).toBeVisible();
    await expect(page.getByRole('searchbox', { name: 'Search CIs' })).toHaveCount(0);
    await expectAccessible(page, 'search');

    // New CI: a type, a name, Create; the CI's page opens with its Toast.
    await page.getByRole('button', { name: 'New CI' }).first().click();
    const panel = page.getByRole('dialog', { name: 'New CI' });
    await expect(panel).toBeVisible();
    await expectAccessible(page, 'New CI panel');
    await panel.getByRole('combobox', { name: /^Type/ }).click();
    await page.getByRole('option', { name: 'Team' }).click();
    await panel.getByRole('textbox', { name: /^Name/ }).fill(name);
    await panel.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
    await expect(toast(page, `${name} created`)).toBeVisible();
    await expect(page).toHaveURL(/\/ci\/[0-9a-f-]{36}$/);
    const ciUrl = page.url();
    await expectAccessible(page, 'CI detail');
    await useScheme(page, 'dark');
    await expectAccessible(page, 'CI detail (dark)');

    // Edit: rename it.
    await page.getByRole('button', { name: 'Edit' }).click();
    const edit = page.getByRole('dialog', { name: `Edit ${name}` });
    await expect(edit).toBeVisible();
    await expectAccessible(page, 'Edit CI panel (dark)');
    await edit.getByRole('textbox', { name: /^Name/ }).fill(`${name}-renamed`);
    await edit.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('heading', { level: 1, name: `${name}-renamed` })).toBeVisible();
    await expect(toast(page, 'Changes saved')).toBeVisible();

    // Retire (confirmed), then Restore.
    await page.getByRole('button', { name: 'Retire…' }).click();
    const retire = page.getByRole('alertdialog', { name: `Retire ${name}-renamed?` });
    await expect(retire).toBeVisible();
    await expectAccessible(page, 'Retire dialog (dark)');
    await retire.getByRole('button', { name: 'Retire', exact: true }).click();
    await expect(page.getByText(/^Retired on \d{4}-\d{2}-\d{2} by /)).toBeVisible();
    await expectAccessible(page, 'retired CI (dark)');
    await page.getByRole('button', { name: 'Restore' }).first().click();
    await expect(toast(page, `${name}-renamed restored`)).toBeVisible();
    await expect(page.getByText(/^Retired on/)).toHaveCount(0);

    // Search by part of its name, in another case; the list is a deep link.
    await useScheme(page, 'light');
    await page.goto(`/search?q=${encodeURIComponent(name.toUpperCase())}`);
    const link = page.getByRole('link', { name: `${name}-renamed` });
    await expect(link).toBeVisible();
    await expect(page.getByText(/^1 CI match/)).toBeVisible();
    await expectAccessible(page, 'search results');
    await link.click();
    await expect(page).toHaveURL(ciUrl);

    // An unknown ID, and something that isn't one, get the friendly page with a search box.
    await page.goto('/ci/0192f3a4-7c1e-7d40-9a51-2b7e0c9d1f99');
    await expect(page.getByRole('heading', { level: 1, name: 'We can’t find that CI' })).toBeVisible();
    await expect(page.getByText(/There’s no CI with the ID 0192f3a4-7c1e-7d40-9a51-2b7e0c9d1f99/)).toBeVisible();
    await expectAccessible(page, 'CI not found');
    await page.goto('/ci/not-an-id');
    await expect(page.getByText(/That isn’t a CI ID/)).toBeVisible();

    // The top-bar search opens Search with the text.
    await page.getByRole('searchbox', { name: 'Search CIs', exact: true }).fill(name);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(new RegExp(`/search\\?q=${name}$`));
    await expect(page.getByRole('link', { name: `${name}-renamed` })).toBeVisible();
  });

  test('a number keeps every digit when typed, shown and saved again after another field changes', async ({ page }) => {
    await signIn(page);
    const stamp = Date.now();
    const attribute = `capacity${stamp}`;
    const label = `Capacity ${stamp}`;
    // The page's own session calls the API (an attribute's screen is slice 5b's); a GET reads the exact text.
    const call = (method: string, path: string, body: unknown = null) =>
      page.evaluate(
        async ({ method, path, body }) => {
          const token = ((await (await fetch('/api/v1/auth/antiforgery')).json()) as { token: string }).token;
          const response = await fetch(path, {
            method,
            headers: { 'Content-Type': 'application/json', 'X-Goldmark-Antiforgery': token },
            ...(body === null ? {} : { body: JSON.stringify(body) }),
          });
          return { status: response.status, json: (await response.json()) as Record<string, unknown> };
        },
        { method, path, body },
      );
    const stored = async (id: string) =>
      ((await call('GET', `/api/v1/cis/${id}`)).json.attributes as Record<string, unknown>)[attribute];

    expect(
      (await call('POST', '/api/v1/types/StorageArray/attributes', { name: attribute, label, dataType: 'number' }))
        .status,
    ).toBe(201);

    // Typed in the form: 2^53 + 1, which a JavaScript number would round to ...992.
    await page.goto('/search');
    await page.getByRole('button', { name: 'New CI' }).first().click();
    const panel = page.getByRole('dialog', { name: 'New CI' });
    await panel.getByRole('combobox', { name: /^Type/ }).click();
    await page.getByRole('option', { name: 'StorageArray' }).click();
    await panel.getByRole('textbox', { name: /^Name/ }).fill(`e2e-array-${stamp}`);
    await panel.getByRole('textbox', { name: label }).fill('9007199254740993');
    await panel.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('heading', { level: 1, name: `e2e-array-${stamp}` })).toBeVisible();
    await expect(page.getByText('9007199254740993', { exact: true })).toBeVisible();
    const typedId = page.url().split('/').pop() ?? '';
    expect(await stored(typedId)).toBe('9007199254740993');

    // Written through the API, then shown, and kept when only the name is edited.
    for (const value of ['1125899906842624', '9007199254740993', '0.1', '0.1234567890123456789']) {
      const name = `e2e-array-${stamp}-${value}`;
      const created = await call('POST', '/api/v1/cis', {
        type: 'StorageArray',
        name,
        attributes: { [attribute]: value },
      });
      expect(created.status).toBe(201);
      await page.goto(`/ci/${String(created.json.id)}`);
      await expect(page.getByText(value, { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Edit' }).click();
      const edit = page.getByRole('dialog', { name: `Edit ${name}` });
      await expect(edit.getByRole('textbox', { name: label })).toHaveValue(value);
      await edit.getByRole('textbox', { name: /^Name/ }).fill(`${name}-renamed`);
      await edit.getByRole('button', { name: 'Save' }).click();
      await expect(page.getByRole('heading', { level: 1, name: `${name}-renamed` })).toBeVisible();
      await expect(page.getByText(value, { exact: true })).toBeVisible();
      expect(await stored(String(created.json.id))).toBe(value);
    }
  });
});

test.describe('slice 5b screen', () => {
  test.use({ reducedMotion: 'reduce' });
  test.describe.configure({ timeout: 180_000 });
  test.skip(!admin.userName || !admin.password || !admin.totpKey, 'needs GOLDMARK_E2E_ADMIN_* from compose-smoke.sh');

  test('an administrator manages a custom type, its attribute and a relationship type on Types & attributes', async ({
    page,
  }) => {
    await recordViolations(page);
    await signIn(page);
    const stamp = Date.now();
    const typeName = `Printer${stamp}`;
    const relationshipName = `prints_for_${stamp}`;
    const call = (method: string, path: string, body: unknown = null) =>
      page.evaluate(
        async ({ method, path, body }) => {
          const token = ((await (await fetch('/api/v1/auth/antiforgery')).json()) as { token: string }).token;
          const response = await fetch(path, {
            method,
            headers: { 'Content-Type': 'application/json', 'X-Goldmark-Antiforgery': token },
            ...(body === null ? {} : { body: JSON.stringify(body) }),
          });
          return { status: response.status };
        },
        { method, path, body },
      );

    // Administration > Types & attributes, from the nav: the first type is selected.
    await page.getByRole('link', { name: 'Types & attributes' }).first().click();
    await expect(page.getByRole('heading', { level: 1, name: 'Types & attributes' })).toBeVisible();
    await expect(page.getByRole('listbox', { name: 'CI types' })).toBeVisible();
    await expectAccessible(page, 'types');

    // New CI type: a name, a group, Create; the new type is selected.
    await page.getByRole('button', { name: 'New CI type' }).click();
    const newType = page.getByRole('dialog', { name: 'New CI type' });
    await expect(newType).toBeVisible();
    await expectAccessible(page, 'New CI type panel');
    await newType.getByRole('textbox', { name: /^Name/ }).fill(typeName);
    await newType.getByRole('combobox', { name: /^Group/ }).click();
    // Exact: the type list behind the panel has options too (NetworkDevice).
    await page.getByRole('option', { name: 'Network', exact: true }).click();
    await newType.getByRole('radio', { name: 'Network device' }).click();
    await newType.getByRole('button', { name: 'Create' }).click();
    await expect(toast(page, `${typeName} added`)).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: typeName })).toBeVisible();
    await expect(page.getByText('This type has no attributes yet.')).toBeVisible();

    // Add attribute: a list of choices, its name suggested from the label.
    await page.getByRole('button', { name: 'Add attribute' }).first().click();
    const add = page.getByRole('dialog', { name: `Add attribute to ${typeName}` });
    await add.getByRole('textbox', { name: /^Label/ }).fill('Backup tier');
    await expect(add.getByRole('textbox', { name: /^Name/ })).toHaveValue('backupTier');
    await add.getByRole('radio', { name: 'List of choices' }).click();
    for (const choice of ['Gold', 'Silver']) {
      await add.getByRole('textbox', { name: 'New choice' }).fill(choice);
      await add.getByRole('button', { name: 'Add choice' }).click();
    }
    await expectAccessible(page, 'Add attribute panel');
    await add.getByRole('button', { name: 'Add', exact: true }).click();
    await expect(toast(page, 'Backup tier added')).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Gold, Silver' })).toBeVisible();

    // A CI holds Gold: on Edit, Gold can't be removed; the label changes.
    expect(
      (await call('POST', '/api/v1/cis', { type: typeName, name: `prn-${stamp}`, attributes: { backupTier: 'Gold' } }))
        .status,
    ).toBe(201);
    await page.reload();
    await page.getByRole('button', { name: 'Edit Backup tier' }).click();
    const edit = page.getByRole('dialog', { name: 'Edit Backup tier' });
    await expect(edit.getByRole('button', { name: 'Remove Gold' })).toBeDisabled();
    await expect(edit.getByText('1 CI uses Gold. Change them first.')).toBeVisible();
    await useScheme(page, 'dark');
    await expectAccessible(page, 'Edit attribute panel (dark)');
    await edit.getByRole('textbox', { name: /^Label/ }).fill('Backup level');
    await edit.getByRole('button', { name: 'Save' }).click();
    await expect(toast(page, 'Changes saved')).toBeVisible();

    // Delete it: a CI has a value, so its name must be typed; the value leaves the CI.
    await page.getByRole('button', { name: 'Delete Backup level' }).click();
    const typed = page.getByRole('alertdialog', { name: 'Delete Backup level?' });
    await expect(typed.getByText(/Each of those CIs will show as changed by you\./)).toBeVisible();
    await expect(typed.getByRole('button', { name: 'Delete attribute' })).toBeDisabled();
    await expectAccessible(page, 'typed confirmation (dark)');
    await typed.getByRole('textbox').fill('backupTier');
    await typed.getByRole('button', { name: 'Delete attribute' }).click();
    await expect(toast(page, 'Backup level deleted (1 value removed)')).toBeVisible();

    // A type with a CI can't be deleted; it can be hidden, and shown again.
    await useScheme(page, 'light');
    await page.getByRole('button', { name: `More actions for ${typeName}` }).click();
    await expect(page.getByRole('menuitem', { name: 'Delete type…' })).toBeDisabled();
    await page.getByRole('menuitem', { name: 'Hide type…' }).click();
    await page
      .getByRole('alertdialog', { name: `Hide ${typeName}?` })
      .getByRole('button', { name: 'Hide type' })
      .click();
    await expect(toast(page, `${typeName} hidden`)).toBeVisible();
    await expect(page.getByText(`${typeName} isn’t offered for new CIs or in Search’s type filter.`)).toBeVisible();
    await expectAccessible(page, 'hidden type');
    await page.getByRole('button', { name: 'Show again' }).click();
    await expect(toast(page, `${typeName} shown again`)).toBeVisible();

    // An unused custom type is deleted after a plain confirmation.
    expect((await call('POST', '/api/v1/types', { name: `Fax${stamp}`, category: 'Network' })).status).toBe(201);
    await page.goto(`/types?type=Fax${stamp}`);
    await page.getByRole('button', { name: `More actions for Fax${stamp}` }).click();
    await page.getByRole('menuitem', { name: 'Delete type…' }).click();
    await page
      .getByRole('alertdialog', { name: `Delete Fax${stamp}?` })
      .getByRole('button', { name: 'Delete' })
      .click();
    await expect(toast(page, `Fax${stamp} deleted`)).toBeVisible();

    // Relationship types: built-in ones are locked; a custom one is added, changed and deleted.
    await page.getByRole('tab', { name: 'Relationship types' }).click();
    await expect(page).toHaveURL(/\/types\?tab=relationships$/);
    await expect(page.getByRole('cell', { name: 'A VM runs on a hypervisor host.' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Edit runs_on' })).toBeDisabled();
    await expectAccessible(page, 'relationship types');
    await page.getByRole('button', { name: 'New relationship type' }).click();
    const rel = page.getByRole('dialog', { name: 'New relationship type' });
    await rel.getByRole('textbox', { name: /^Name/ }).fill(relationshipName);
    await rel.getByRole('textbox', { name: /^Description/ }).fill('Prints for a team.');
    await expectAccessible(page, 'New relationship type panel');
    await rel.getByRole('button', { name: 'Create' }).click();
    await expect(toast(page, `${relationshipName} added`)).toBeVisible();
    await page.getByRole('button', { name: `Edit ${relationshipName}` }).click();
    const relEdit = page.getByRole('dialog', { name: `Edit ${relationshipName}` });
    await relEdit.getByRole('radio', { name: /^None/ }).click();
    await relEdit.getByRole('button', { name: 'Save' }).click();
    await expect(toast(page, 'Changes saved')).toBeVisible();
    await page.getByRole('button', { name: `Delete ${relationshipName}` }).click();
    await page
      .getByRole('alertdialog', { name: `Delete ${relationshipName}?` })
      .getByRole('button', { name: 'Delete' })
      .click();
    await expect(toast(page, `${relationshipName} deleted`)).toBeVisible();

    // At 1024 px the type list is a Combobox above the details.
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto(`/types?type=${typeName}`);
    await expect(page.getByRole('combobox', { name: 'CI type' })).toHaveValue(typeName);
    await expectAccessible(page, 'types at 1024');

    // The audit log names the type and offers its other entries.
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/audit?action=type.attribute.deleted');
    await page
      .getByRole('button', { name: /^Show details for entry \d+: Custom attribute deleted/ })
      .first()
      .click();
    await page.getByRole('button', { name: `Show all entries about ${typeName}` }).click();
    await expect(page).toHaveURL(/target=type%3A\d+/);
    await expect(page.getByRole('cell', { name: 'Custom attribute deleted' }).first()).toBeVisible();
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
