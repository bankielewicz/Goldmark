import { existsSync, readFileSync } from 'node:fs';
import { basename, extname, join } from 'node:path';
import type { Page } from '@playwright/test';

// The CSP checks (fluent.spec.ts, cytoscape.spec.ts) keep the real server's SPA shell (its HTML,
// its fresh style nonce and its headers) and swap only the shell's script for a sampler's build.

const CONTENT_TYPES: Record<string, string> = {
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
  '.woff': 'font/woff',
  '.ttf': 'font/ttf',
};

/**
 * Serves the sampler's assets from the build directory named by `distVariable`; the shell's entry
 * script is answered with the sampler's entry. Anything else goes to the real server.
 */
export async function swapInSampler(page: Page, distVariable: string, buildScript: string) {
  const dist = process.env[distVariable];
  if (!dist || !existsSync(join(dist, 'index.html'))) {
    throw new Error(`The sampler was not built (${buildScript} is a global setup).`);
  }
  const entry = readFileSync(join(dist, 'index.html'), 'utf8').match(
    /<script type="module" crossorigin src="\/assets\/([^"]+\.js)">/,
  )?.[1];
  if (!entry) throw new Error('No module entry in the sampler build.');
  const assets = join(dist, 'assets');

  await page.route('**/assets/*', async (route) => {
    const name = basename(new URL(route.request().url()).pathname);
    const own = existsSync(join(assets, name)) ? name : route.request().resourceType() === 'script' ? entry : null;
    const contentType = own ? CONTENT_TYPES[extname(own)] : undefined;
    if (!own || !contentType) {
      await route.continue();
      return;
    }
    await route.fulfill({
      status: 200,
      headers: { 'content-type': contentType, 'x-content-type-options': 'nosniff' },
      body: readFileSync(join(assets, own)),
    });
  });
}

/** The style nonce in a served Content-Security-Policy, if it has exactly the expected shape. */
export const headerNonce = (csp: string) => csp.match(/(?:^|; )style-src 'self' 'nonce-([A-Za-z0-9_-]{22,})';/)?.[1];
