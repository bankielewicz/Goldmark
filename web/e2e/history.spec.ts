import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Locator, type Page } from '@playwright/test';

import { admin, expectAccessible, INLINE_NAV, recordViolations, signIn, useScheme } from './session';

// Slice 7 in a real browser against the Compose stack (DEC-047; the boards `docs/design/screens/slice-7/`): a CI's
// History tab, "View this CI as of this time", the CI page, its Relationships tab, Search and the Graph explorer as of
// that moment under the amber banner, read-only, the top bar's As-of control and its dialog, the moment kept while on
// another screen, and the pages for a moment before anything existed; axe in both themes and no CSP or Trusted Types
// violation. With GOLDMARK_E2E_SCREENSHOTS set to a folder, each screen is saved there for a look against the boards.

const shots = process.env.GOLDMARK_E2E_SCREENSHOTS;

async function shot(page: Page, name: string) {
  if (!shots) return;
  mkdirSync(shots, { recursive: true });
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'));
  await page.screenshot({ path: join(shots, `history-${name}.png`), fullPage: true });
}

/** An API call from the signed-in page itself (its cookies, an anti-forgery token, the browser's pinned certificate). */
async function call(
  page: Page,
  method: 'GET' | 'POST' | 'PUT' | 'DELETE',
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

/**
 * What makes the page wider than the window: nothing when it isn't; else the innermost elements past the window's
 * right edge that no scroll box of their own holds back, so a failure names them.
 */
async function stickingOut(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const width = document.documentElement.clientWidth;
    const over = Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) - width;
    if (over <= 0) return [];
    const clipped = (e: Element): boolean => {
      for (let p = e.parentElement; p && p !== document.body; p = p.parentElement) {
        if (getComputedStyle(p).overflowX !== 'visible' && p.getBoundingClientRect().right <= width + 1) return true;
      }
      return false;
    };
    const out = [...document.querySelectorAll<HTMLElement>('body *')].filter((e) => {
      const box = e.getBoundingClientRect();
      return box.width > 0 && box.right > width + 1 && !clipped(e);
    });
    const named = out
      .filter((e) => !out.some((other) => other !== e && e.contains(other)))
      .slice(0, 6)
      .map(
        (e) =>
          `<${e.tagName.toLowerCase()} class="${String(e.className).split(' ').slice(0, 2).join(' ')}"> "${(e.textContent ?? '').trim().slice(0, 30)}" ends at ${Math.round(e.getBoundingClientRect().right)} of ${width}`,
      );
    // Wider, whatever the cause: never an empty answer for a page that scrolls sideways.
    return [`${over} px wider than the ${width} px window`, ...named];
  });
}

/** "2026-10-06 14:00 UTC", as the banner and the top bar write a moment. */
function shown(at: string): string {
  const text = new Date(at).toISOString();
  return `${text.slice(0, 10)} ${text.slice(11, 16)} UTC`;
}

test.describe('slice 7 screens', () => {
  test.use({ reducedMotion: 'reduce' });
  test.describe.configure({ timeout: 240_000 });
  test.skip(!admin.userName || !admin.password || !admin.totpKey, 'needs GOLDMARK_E2E_ADMIN_* from compose-smoke.sh');

  test('a CI’s history, and Goldmark as of a moment from it: read-only, under the banner, on every screen with a past', async ({
    page,
  }) => {
    await recordViolations(page);
    const problems: string[] = [];
    page.on('pageerror', (error) => problems.push(`page error: ${error.message}`));
    await signIn(page);

    // Before the moment: a custom attribute, a VM with a value for it, two hosts, and the VM running on the first.
    const attribute = await call(page, 'POST', '/api/v1/types/VirtualMachine/attributes', {
      name: 'histCostCentre',
      label: 'Cost centre',
      dataType: 'string',
    });
    expect(attribute.status).toBe(201);
    const vm = await post(page, '/api/v1/cis', {
      type: 'VirtualMachine',
      name: 'hist-vm-01',
      attributes: { histCostCentre: 'CC-4100' },
    });
    const oldHost = await post(page, '/api/v1/cis', { type: 'HypervisorHost', name: 'hist-esx-011' });
    const newHost = await post(page, '/api/v1/cis', { type: 'HypervisorHost', name: 'hist-esx-014' });
    const runsOn = await call(page, 'POST', '/api/v1/relationships', {
      type: 'runs_on',
      from: vm,
      to: oldHost,
      redundancyGroup: '',
    });
    expect(runsOn.status).toBe(201);
    const history = await call(page, 'GET', `/api/v1/cis/${vm}/history`);
    const moment = String((history.json?.items as { at: string }[])[0]?.at);

    // After it: a rename and a new value, the move to the other host, the attribute deleted, and a second VM.
    const read = await call(page, 'GET', `/api/v1/cis/${vm}`);
    expect(
      (
        await call(
          page,
          'PUT',
          `/api/v1/cis/${vm}`,
          { name: 'hist-vm-01b', attributes: { histCostCentre: 'CC-4200' } },
          read.etag ?? '',
        )
      ).status,
    ).toBe(200);
    expect(
      (await call(page, 'DELETE', `/api/v1/relationships/${String(runsOn.json?.id)}`, undefined, runsOn.etag ?? ''))
        .status,
    ).toBe(204);
    await post(page, '/api/v1/relationships', { type: 'runs_on', from: vm, to: newHost, redundancyGroup: '' });
    const types = await call(page, 'GET', '/api/v1/types');
    const definition = (types.json?.types as { name: string; attributes: { name: string; eTag: string }[] }[])
      .find((t) => t.name === 'VirtualMachine')
      ?.attributes.find((a) => a.name === 'histCostCentre');
    expect(
      (
        await call(
          page,
          'DELETE',
          '/api/v1/types/VirtualMachine/attributes/histCostCentre',
          undefined,
          `"${definition?.eTag ?? ''}"`,
        )
      ).status,
    ).toBe(200);
    const later = await post(page, '/api/v1/cis', { type: 'VirtualMachine', name: 'hist-vm-02' });

    // Screen 5's History tab: newest first, the newest selected, each change with who made it.
    await page.goto(`/ci/${vm}?tab=history`);
    await expect(page.getByRole('heading', { level: 1, name: 'hist-vm-01b' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'History' })).toHaveAttribute('aria-selected', 'true');
    // The inline navigation is there at this width, under the classes expectAccessible measures on every screen.
    await expect(page.locator(INLINE_NAV)).toHaveCount(1);
    const timeline = page.getByRole('group', { name: 'Timeline' });
    await expect(timeline.getByRole('button', { name: /Removed Cost centre \(attribute deleted\)/ })).toHaveAttribute(
      'aria-current',
      'true',
    );
    await expect(page.getByText('6 changes')).toBeVisible();
    await expect(timeline.getByRole('button', { name: /Added relationship: runs_on hist-esx-014/ })).toBeVisible();
    await expect(timeline.getByRole('button', { name: /Ended relationship: runs_on hist-esx-011/ })).toBeVisible();
    await expect(timeline.getByRole('button', { name: /Changed Name and Cost centre/ })).toBeVisible();
    await expect(page.getByRole('region', { name: /^Change on / }).getByText('Attribute deleted')).toBeVisible();
    await expectAccessible(page, 'history tab');
    await shot(page, 'tab');

    // On a phone: one column, the selected entry open in place, and its diff table scrolling in its own box, so
    // the page is no wider than the screen.
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(page.getByRole('button', { name: 'Open navigation' })).toBeVisible();
    await expect(timeline.getByRole('button', { name: /Removed Cost centre \(attribute deleted\)/ })).toHaveAttribute(
      'aria-expanded',
      'true',
    );
    const diffBox = page.getByRole('group', { name: 'What changed, as a table' });
    await expect(diffBox).toBeVisible();
    // The box scrolls, from the keyboard too: nothing of the table is cut off out of reach.
    await diffBox.focus();
    await page.keyboard.press('ArrowRight');
    await expect.poll(() => diffBox.evaluate((box) => box.scrollLeft)).toBeGreaterThan(0);
    expect(await stickingOut(page), 'wider than a phone').toEqual([]);
    await expectAccessible(page, 'history tab on a phone');
    await shot(page, 'tab-phone');
    await page.setViewportSize({ width: 1280, height: 720 });

    // The rename's diff: Before struck through, After plain, the change in words.
    await timeline.getByRole('button', { name: /Changed Name and Cost centre/ }).click();
    const detail = page.getByRole('region', { name: /^Change on / });
    await expect(detail.getByRole('table')).toBeVisible();
    await expect(detail.getByRole('row', { name: /Name Changed hist-vm-01 hist-vm-01b/ })).toBeVisible();
    await expect(detail.getByRole('row', { name: /Cost centre Changed CC-4100 CC-4200/ })).toBeVisible();
    await useScheme(page, 'dark');
    await expectAccessible(page, 'history tab, a change (dark)');
    await useScheme(page, 'light');

    // Who ended the relationship, kept with its history (DEC-047 item 9).
    await timeline.getByRole('button', { name: /Ended relationship: runs_on hist-esx-011/ }).click();
    await expect(detail.getByText('This CI depends on hist-esx-011')).toBeVisible();
    await expect(detail.getByText('The audit entry that recorded who ended it is no longer kept.')).toHaveCount(0);

    // "View this CI as of this time" from the entry of the moment: the CI as it was, read-only, under the banner.
    await timeline.getByRole('button', { name: /Added relationship: runs_on hist-esx-011/ }).click();
    await detail.getByRole('link', { name: 'View this CI as of this time' }).click();
    await expect(page).toHaveURL(/asOf=/);
    const banner = page.getByText(`Viewing as of ${shown(moment)} (read-only)`);
    await expect(banner).toBeVisible();
    await expect(page.getByRole('heading', { level: 1, name: 'hist-vm-01' })).toBeVisible();
    await expect(page.getByText('CC-4100')).toBeVisible();
    await expect(page.getByText('Deleted since')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Edit' })).toHaveAttribute('aria-disabled', 'true');
    await expect(page.getByRole('button', { name: `As of: ${shown(moment)}. Choose a point in time` })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await expectAccessible(page, 'CI as of a moment');
    await useScheme(page, 'dark');
    await expectAccessible(page, 'CI as of a moment (dark)');
    await useScheme(page, 'light');
    await shot(page, 'ci-as-of');
    // On a phone the top bar holds the control with its short label, and the banner wraps: nothing sticks out.
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(page.getByRole('button', { name: 'Open navigation' })).toBeVisible();
    // Its short label is in its name, before the whole moment (Label in Name).
    await expect(
      page.getByRole('button', { name: new RegExp(`^As of: .+ \\(${shown(moment)}\\)\\. Choose a point in time$`) }),
    ).toBeVisible();
    expect(await stickingOut(page), 'a CI as of a moment is wider than a phone').toEqual([]);
    await expectAccessible(page, 'CI as of a moment on a phone');
    await shot(page, 'ci-as-of-phone');
    await page.setViewportSize({ width: 1280, height: 720 });

    // Its Relationships tab then: the host it ran on, ended since; Add relationship disabled.
    await page.getByRole('tab', { name: /Relationships/ }).click();
    await expect(page.getByRole('row', { name: /hist-esx-011.*Ended since/ })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add relationship' })).toHaveAttribute('aria-disabled', 'true');
    await expectAccessible(page, 'relationships as of a moment');

    // Its History tab marks the entry shown above.
    await page.getByRole('tab', { name: 'History' }).click();
    await expect(page.getByRole('group', { name: 'Timeline' }).getByText('Shown above')).toHaveCount(1);

    // Search keeps the moment: the CIs of then, under the names of then; New CI disabled.
    await page.getByRole('link', { name: 'Search', exact: true }).first().click();
    await expect(page).toHaveURL(/\/search\?.*asOf=/);
    await page.getByRole('searchbox', { name: 'Search by name' }).fill('hist-');
    await expect(page.getByText('3 CIs matched ‘hist-’ at that time')).toBeVisible();
    await expect(page.getByRole('link', { name: 'hist-vm-01', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'hist-vm-02' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'New CI' })).toHaveAttribute('aria-disabled', 'true');
    await expectAccessible(page, 'search as of a moment');
    await shot(page, 'search-as-of');

    // The Graph explorer then: the relationship ended since is still walked.
    await page.goto(`/graph?ci=${vm}&asOf=${encodeURIComponent(moment)}`);
    await expect(page.getByText(`Graph as of ${shown(moment)} (read-only)`)).toBeVisible();
    await expect(page.getByText(/2 CIs · 1 relationship · up to 3 hops from hist-vm-01, at that time/)).toBeVisible();
    await expectAccessible(page, 'graph as of a moment');
    await useScheme(page, 'dark');
    await expectAccessible(page, 'graph as of a moment (dark)');
    await useScheme(page, 'light');
    await shot(page, 'graph-as-of');

    // A CI created after the moment: the page says when.
    await page.goto(`/ci/${later}?asOf=${encodeURIComponent(moment)}`);
    await expect(page.getByRole('heading', { level: 1, name: 'hist-vm-02 didn’t exist at that time.' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Breadcrumb' })).toContainText('hist-vm-02');
    await expect(page.getByText(/It was created on .* UTC, after the time you’re viewing\./)).toBeVisible();
    await expectAccessible(page, 'a CI that didn’t exist yet');
    await shot(page, 'ci-not-yet');

    // A screen with no past: the control is disabled there, and the moment is kept for the way back.
    await page.goto('/audit');
    await expect(page.getByRole('button', { name: 'As of: Now. Choose a point in time' })).toHaveAttribute(
      'aria-disabled',
      'true',
    );
    await expectAccessible(page, 'the audit log with a moment kept');
    await shot(page, 'audit-kept');
    // From a page with a table, where the navigation once shrank out of reach.
    await page.getByRole('link', { name: 'Search', exact: true }).first().click();
    await expect(page).toHaveURL(/\/search\?asOf=/);
    await expect(page.getByText(`Viewing as of ${shown(moment)} (read-only)`)).toBeVisible();

    // The dialog: reopened with the moment, Return to now.
    await page.getByRole('button', { name: `As of: ${shown(moment)}. Choose a point in time` }).click();
    const dialog = page.getByRole('dialog', { name: 'Choose a point in time' });
    await expect(dialog.getByLabel('Date')).toHaveValue(new Date(moment).toISOString().slice(0, 10));
    await expectAccessible(page, 'the As-of dialog');
    await shot(page, 'dialog');
    await dialog.getByRole('button', { name: 'Return to now' }).click();
    await expect(page).not.toHaveURL(/asOf=/);
    await expect(page.getByText(/Viewing as of/)).toHaveCount(0);

    // Its errors, and a quick choice that fills the fields.
    await page.getByRole('button', { name: 'As of: Now. Choose a point in time' }).click();
    await dialog.getByRole('button', { name: 'Apply' }).click();
    await expect(dialog.getByRole('alert')).toHaveText('Enter a date and a time.');
    await dialog.getByLabel('Date').fill('2099-01-01');
    await dialog.getByLabel('Time').fill('10:00');
    await dialog.getByRole('button', { name: 'Apply' }).click();
    await expect(dialog.getByRole('alert')).toHaveText('That time is in the future.');
    await dialog.getByRole('button', { name: '1 hour ago' }).click();
    await expect(dialog.getByLabel('Time')).toHaveValue(/^\d{2}:\d{2}$/);
    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toHaveCount(0);

    // Before anything was entered.
    await page.goto('/search?asOf=2000-01-01T00:00Z');
    await expect(page.getByRole('heading', { name: 'No CIs existed at that time.' })).toBeVisible();
    await expect(page.getByText('Nothing had been entered in Goldmark yet.')).toBeVisible();
    await expectAccessible(page, 'search before anything existed');

    // At 1100 px the navigation is inline and the pages have the least room beside it. None is wider than the
    // window, the navigation keeps its width (expectAccessible), and what doesn't fit scrolls in its own box.
    await page.setViewportSize({ width: 1100, height: 800 });
    // Each page with what it must show before it is measured, so an error screen can't pass for it.
    const pages: [name: string, path: string, shows: () => Locator][] = [
      ['search', '/search', () => page.getByRole('table', { name: 'CIs' }).getByRole('link', { name: 'hist-vm-01b' })],
      ['history', `/ci/${vm}?tab=history`, () => page.getByRole('region', { name: /^Change on / }).getByRole('table')],
      ['relationships', `/ci/${vm}?tab=relationships`, () => page.getByRole('row', { name: /runs_on.*hist-esx-014/ })],
      ['graph', `/graph?ci=${vm}`, () => page.getByText(/2 CIs · 1 relationship · up to 3 hops from hist-vm-01b/)],
      ['audit', '/audit', () => page.getByRole('row', { name: /Relationship added/ }).first()],
      [
        'users',
        '/users',
        () => page.getByRole('grid').getByRole('row', { name: new RegExp(admin.userName.replace('.', '\\.')) }),
      ],
      ['types', '/types', () => page.getByRole('table', { name: /^Attributes of / })],
    ];
    for (const [name, path, shows] of pages) {
      await page.goto(path);
      await expect(shows(), `${name} at 1100 px shows its content`).toBeVisible();
      await expect(page.locator(INLINE_NAV)).toHaveCount(1);
      expect(await stickingOut(page), `${name} at 1100 px is wider than the window`).toEqual([]);
      await expectAccessible(page, `${name} at 1100 px`);
      await shot(page, `1100-${name}`);
    }

    expect(problems).toEqual([]);
  });
});
