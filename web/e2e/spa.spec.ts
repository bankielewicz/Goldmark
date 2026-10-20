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

  // Signed out, the app shows sign-in (slice 3): Fluent UI rendered under the served CSP.
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
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
  // Signed out: sign-in first, then back to the page asked for.
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  expect(new URL(page.url()).searchParams.get('next')).toBe('/ci/00000000-0000-7000-8000-000000000000');
  expect(await page.evaluate(() => window.__cspViolations)).toEqual([]);
});

test('every shell response carries a fresh style nonce, hidden from the DOM', async ({ page }) => {
  await recordViolations(page);
  const nonces: string[] = [];
  for (let i = 0; i < 2; i++) {
    const response = i === 0 ? await page.goto('/') : await page.reload();
    expect(response?.headers()['cache-control']).toBe('no-store');
    const nonce = (response?.headers()['content-security-policy'] ?? '').match(
      /style-src 'self' 'nonce-([A-Za-z0-9_-]{22,})';/,
    )?.[1];
    expect(nonce).toBeTruthy();
    const meta = await page.evaluate(() => {
      const element = document.querySelector<HTMLMetaElement>('meta[property="csp-nonce"]');
      return { nonce: element?.nonce, attribute: element?.getAttribute('nonce') };
    });
    expect(meta).toEqual({ nonce, attribute: '' });
    nonces.push(nonce ?? '');
  }
  expect(nonces[0]).not.toBe(nonces[1]);

  // Without the nonce, injected styles and style attributes stay blocked.
  const blocked = await page.evaluate(() => {
    const style = document.createElement('style');
    style.textContent = 'h1 { color: rgb(255, 0, 0); }';
    document.head.append(style);
    const div = document.createElement('div');
    div.setAttribute('style', 'color: rgb(255, 0, 0)');
    return { sheet: style.sheet === null, attribute: div.style.color === '' };
  });
  expect(blocked).toEqual({ sheet: true, attribute: true });
});
