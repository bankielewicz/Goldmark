// Imported first by main.tsx, so it is evaluated before any Fluent UI module and records
// every CSP / Trusted Types violation the page reports from the start.

export const violations: string[] = [];

declare global {
  interface Window {
    __fluentCheckViolations?: string[];
  }
}

window.__fluentCheckViolations = violations;
document.addEventListener('securitypolicyviolation', (e) => {
  violations.push(`${e.effectiveDirective} blocked ${e.blockedURI || '(inline)'}`);
});
