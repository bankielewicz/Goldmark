import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build, mergeConfig } from 'vite';

import productionConfig from '../../vite.config';

/**
 * Playwright global setup: builds the Cytoscape.js sampler (e2e/cytoscape-check/app) with the
 * product's own Vite settings (assets never inlined as data: URIs), in production mode, into a
 * temporary directory. The sampler never enters web/dist or the image. Returns the teardown that
 * removes it.
 */
export default async function buildCytoscapeCheck() {
  const outDir = mkdtempSync(join(tmpdir(), 'goldmark-cytoscape-check-'));
  await build(
    mergeConfig(productionConfig, {
      configFile: false,
      root: join(dirname(fileURLToPath(import.meta.url)), 'app'),
      mode: 'production',
      logLevel: 'error',
      build: { outDir, emptyOutDir: true },
    }),
  );
  process.env.GOLDMARK_CYTOSCAPE_CHECK_DIST = outDir;
  return () => rmSync(outDir, { recursive: true, force: true });
}
