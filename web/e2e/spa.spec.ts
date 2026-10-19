import { expect, test, type Page } from '@playwright/test';

declare global {
  interface Window {
    __cspViolations?: string[];
  }
}

/** Records every CSP / Trusted Types violation the page reports. */
async function recordViolations(page: Page) {
  await page.addInitScript(() => {
    window.__cspViolations = [];
    document.addEventListener('securitypolicyviolation', (e) => {
      window.__cspViolations?.push(`${e.effectiveDirective} blocked ${e.blockedURI || '(inline)'}`);
    });
  });
}

test('SPA renders under the production CSP with Trusted Types enforced', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') errors.push(m.text());
  });
  page.on('pageerror', (e) => errors.push(e.message));
  await recordViolations(page);

  const response = await page.goto('/');
  expect(response?.status()).toBe(200);
  const csp = response?.headers()['content-security-policy'] ?? '';
  expect(csp).toContain("default-src 'self'");
  expect(csp).toContain("require-trusted-types-for 'script'");
  expect(csp).not.toContain('unsafe-inline');
  expect(csp).not.toContain('unsafe-eval');

  await expect(page.getByRole('heading', { level: 1, name: 'Goldmark CMDB' })).toBeVisible();
  expect(await page.evaluate(() => window.isSecureContext)).toBe(true);
  expect(await page.evaluate(() => window.__cspViolations)).toEqual([]);
  expect(errors).toEqual([]);

  // Positive proof that enforcement is live: a raw HTML sink must throw.
  const sinkBlocked = await page.evaluate(() => {
    try {
      // eslint-disable-next-line no-restricted-syntax -- deliberately probing the Trusted Types sink
      document.createElement('div').innerHTML = '<b>probe</b>';
      return false;
    } catch (e) {
      return e instanceof TypeError;
    }
  });
  expect(sinkBlocked).toBe(true);
});

test('client-side routes fall back to the SPA shell', async ({ page }) => {
  await recordViolations(page);
  const response = await page.goto('/ci/00000000-0000-7000-8000-000000000000');
  expect(response?.status()).toBe(200);
  await expect(page.getByRole('heading', { level: 1, name: 'Goldmark CMDB' })).toBeVisible();
  expect(await page.evaluate(() => window.__cspViolations)).toEqual([]);
});
