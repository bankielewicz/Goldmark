import { readStyleNonce } from '../styleNonce';
import { CYTOSCAPE_CONTAINER_RULE, CYTOSCAPE_STYLESHEET_ID } from './cytoscapeIds';

/**
 * Cytoscape.js gives its container `position: relative` through one `<style>` element it puts first
 * in `<head>`, without a nonce, so the served CSP refuses it. It skips that element when one with
 * its id already exists, so the app adds it first, carrying the page's nonce (DEC-046 item 3,
 * option (i)). Call this before the first `cytoscape()`. tests/cytoscape-stylesheet.test.mjs fails
 * if a Cytoscape update changes the id, the class, the rule or the check.
 */
export function ensureCytoscapeStylesheet(doc: Document = document): HTMLStyleElement {
  const existing = doc.getElementById(CYTOSCAPE_STYLESHEET_ID);
  if (existing instanceof HTMLStyleElement) return existing;
  const style = doc.createElement('style');
  style.id = CYTOSCAPE_STYLESHEET_ID;
  style.setAttribute('nonce', readStyleNonce(doc));
  style.textContent = CYTOSCAPE_CONTAINER_RULE;
  doc.head.insertBefore(style, doc.head.firstChild);
  return style;
}
