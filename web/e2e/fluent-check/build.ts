import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build, mergeConfig } from 'vite';

import productionConfig from '../../vite.config';

/**
 * Playwright global setup: builds the Fluent UI sampler (e2e/fluent-check/app) with the
 * product's own Vite settings, in production mode, into a temporary directory. The
 * sampler never enters web/dist or the image. Returns the teardown that removes it.
 */
export default async function buildFluentCheck() {
  const outDir = mkdtempSync(join(tmpdir(), 'goldmark-fluent-check-'));
  await build(
    mergeConfig(productionConfig, {
      configFile: false,
      root: join(dirname(fileURLToPath(import.meta.url)), 'app'),
      mode: 'production',
      // Fluent's modules carry 'use client' directives, which a browser-only build ignores.
      logLevel: 'error',
      build: { outDir, emptyOutDir: true },
    }),
  );
  process.env.GOLDMARK_FLUENT_CHECK_DIST = outDir;
  return () => rmSync(outDir, { recursive: true, force: true });
}
