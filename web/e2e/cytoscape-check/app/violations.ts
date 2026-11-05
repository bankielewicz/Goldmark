// Imported first by main.ts, so it is evaluated before Cytoscape.js and records every CSP /
// Trusted Types violation the page reports from the start.

export const violations: string[] = [];

declare global {
  interface Window {
    __cytoscapeCheckViolations?: string[];
  }
}

window.__cytoscapeCheckViolations = violations;
document.addEventListener('securitypolicyviolation', (e) => {
  violations.push(`${e.effectiveDirective} blocked ${e.blockedURI || '(inline)'}`);
});
