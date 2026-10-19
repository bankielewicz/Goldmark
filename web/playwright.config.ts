import { defineConfig } from '@playwright/test';

// Browser smoke tests against a running Goldmark instance (normally the Compose
// stack started by tests/smoke/compose-smoke.sh). The instance uses its
// self-signed certificate, so the test pins that certificate's public key
// instead of disabling TLS checks: the page stays a secure context.
const baseURL = process.env.GOLDMARK_BASE_URL;
const spki = process.env.GOLDMARK_TLS_SPKI_SHA256;
if (!baseURL || !spki) {
  throw new Error('Set GOLDMARK_BASE_URL and GOLDMARK_TLS_SPKI_SHA256 (see tests/smoke/compose-smoke.sh).');
}

export default defineConfig({
  testDir: 'e2e',
  forbidOnly: true,
  retries: 0,
  workers: 1,
  timeout: 30_000,
  reporter: [['list']],
  use: {
    baseURL,
    browserName: 'chromium',
    // CI uses the runner's preinstalled Google Chrome; locally, point
    // GOLDMARK_E2E_CHROMIUM at any Chromium binary. Playwright never downloads one.
    ...(process.env.GOLDMARK_E2E_CHANNEL ? { channel: process.env.GOLDMARK_E2E_CHANNEL } : {}),
    launchOptions: {
      ...(process.env.GOLDMARK_E2E_CHROMIUM ? { executablePath: process.env.GOLDMARK_E2E_CHROMIUM } : {}),
      args: [`--ignore-certificate-errors-spki-list=${spki}`],
    },
  },
});
