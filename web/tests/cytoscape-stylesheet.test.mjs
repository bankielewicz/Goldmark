// The web app adds Cytoscape.js's stylesheet itself, with the page's style nonce, because Cytoscape's
// own has none and the served CSP refuses it (DEC-046 item 3, option (i)). That works only while the
// installed Cytoscape still uses the same id, class and rule, still skips its own element when ours
// exists, and creates no other <style> element. This test fails when an update changes any of that.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';

import {
  CYTOSCAPE_CONTAINER_CLASS,
  CYTOSCAPE_CONTAINER_RULE,
  CYTOSCAPE_STYLESHEET_ID,
} from '../src/graph/cytoscapeIds.ts';

const require = createRequire(import.meta.url);
// The ES module build is the one Vite bundles (package.json "exports" → "import").
const bundle = readFileSync(require.resolve('cytoscape').replace(/cytoscape\.cjs\.js$/, 'cytoscape.esm.mjs'), 'utf8');
const occurrences = (text) => bundle.split(text).length - 1;

describe('Cytoscape.js stylesheet (DEC-046 item 3)', () => {
  it('uses the id and container class the app pre-creates', () => {
    assert.equal(occurrences(`var stylesheetId = '${CYTOSCAPE_STYLESHEET_ID}';`), 1);
    assert.equal(occurrences(`var className = '${CYTOSCAPE_CONTAINER_CLASS}';`), 1);
  });

  it('skips its own element when one with that id exists', () => {
    assert.equal(occurrences('var stylesheetAlreadyExists = document.getElementById(stylesheetId) != null;'), 1);
    assert.equal(occurrences('if (!stylesheetAlreadyExists) {'), 1);
  });

  it('holds the same rule as the app', () => {
    assert.equal(occurrences("stylesheet.textContent = '.' + className + ' { position: relative; }';"), 1);
    assert.equal(CYTOSCAPE_CONTAINER_RULE, `.${CYTOSCAPE_CONTAINER_CLASS} { position: relative; }`);
  });

  it('creates no other <style> element', () => {
    assert.equal(occurrences("createElement('style')"), 1);
    assert.equal(occurrences('createElement("style")'), 0);
  });
});
