// Cytoscape.js sampler for the CSP / Trusted Types check (DEC-017, DEC-037, DEC-046 item 3). It is a
// test fixture, never part of the product build: e2e/cytoscape-check/build.ts builds it with the
// production Vite settings. selfcheck.ts draws a graph the way screen 6 will and reports PASS or
// FAIL in the page itself.
import './violations';

import { runSelfCheck } from './selfcheck';

void runSelfCheck();
