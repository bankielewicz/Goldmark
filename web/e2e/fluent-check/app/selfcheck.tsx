// In-page qualification of Fluent UI under the served CSP (DEC-037). It runs on load in any
// browser and shows PASS or FAIL with the details, so the same page qualifies Chrome (through
// Playwright) and Edge and Firefox (opened by hand against a real server). It covers what
// depends on the CSP: the nonce, styles actually applied (a blocked stylesheet fails silently),
// keyframes, fonts, theme changes, portals, right-to-left rules, and positive controls proving
// that the policy is enforced. Keyboard behaviour is checked by e2e/fluent.spec.ts.
import { useEffect, useRef, useState } from 'react';

import { CONTROL_DIRECTIVES } from './controls';
import { violations } from './violations';

export interface CheckResult {
  name: string;
  pass: boolean;
  detail: string;
}

export interface SelfCheckReport {
  pass: boolean;
  userAgent: string;
  brands: string;
  checks: CheckResult[];
  violations: string[];
}

declare global {
  interface Window {
    __fluentCheckReport?: SelfCheckReport;
    // Not in TypeScript's DOM library yet.
    trustedTypes?: { createPolicy(name: string, rules: object): unknown };
  }
  interface Navigator {
    userAgentData?: { brands: { brand: string; version: string }[] };
  }
}

const byTestId = (id: string): HTMLElement => {
  const element = document.querySelector<HTMLElement>(`[data-testid="${id}"]`);
  if (!element) throw new Error(`no element [data-testid="${id}"]`);
  return element;
};

const css = (element: Element, property: string) => getComputedStyle(element).getPropertyValue(property).trim();

const button = (text: string): HTMLButtonElement => {
  const found = [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === text);
  if (!found) throw new Error(`no button "${text}"`);
  return found;
};

const nextFrame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

async function waitFor<T>(what: string, probe: () => T | null | undefined | false, timeoutMs = 4000): Promise<T> {
  const deadline = performance.now() + timeoutMs;
  for (;;) {
    const value = probe();
    if (value) return value;
    if (performance.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
}

const isShown = (element: Element | null) =>
  element !== null && element.getClientRects().length > 0 && css(element, 'visibility') !== 'hidden';

const directive = (violation: string) => violation.split(' ', 1)[0] ?? '';

async function run(): Promise<SelfCheckReport> {
  const checks: CheckResult[] = [];
  const check = async (name: string, body: () => Promise<string> | string) => {
    try {
      checks.push({ name, pass: true, detail: await body() });
    } catch (e) {
      checks.push({ name, pass: false, detail: e instanceof Error ? e.message : String(e) });
    }
  };
  const expect = (condition: boolean, message: string) => {
    if (!condition) throw new Error(message);
  };

  const meta = document.querySelector<HTMLMetaElement>('meta[property="csp-nonce"]');
  const nonce = meta?.nonce ?? '';

  await check('nonce on the page and on every style element', () => {
    expect(nonce.length >= 22, `page nonce has ${nonce.length} characters`);
    const styles = [...document.querySelectorAll('style')];
    expect(styles.length > 0, 'no style elements');
    const wrong = styles.filter((s) => s.nonce !== nonce || s.sheet === null);
    expect(wrong.length === 0, `${wrong.length} of ${styles.length} style elements lack the nonce or a sheet`);
    return `${styles.length} style elements carry the page nonce`;
  });

  await check('nonce hidden from the DOM', () => {
    const exposed = [meta, ...document.querySelectorAll('style')].filter((e) => e && e.getAttribute('nonce') !== '');
    expect(exposed.length === 0, `${exposed.length} elements still expose the nonce attribute value`);
    return 'attribute values read as empty';
  });

  await check('theme and component styles applied', () => {
    const brand = css(byTestId('brand-reference'), 'background-color');
    expect(css(byTestId('primary-button'), 'background-color') === brand, 'primary button is not the brand colour');
    expect(css(byTestId('provider'), '--colorBrandBackground') !== '', 'theme variables missing');
    const probe = byTestId('make-styles-probe');
    expect(css(probe, 'border-top-width') === '3px', 'makeStyles rule missing');
    expect(css(probe, 'padding-left') === '7px', 'media-query rule missing');
    expect(css(byTestId('reset-probe'), 'outline-style') === 'dotted', 'makeResetStyles rule missing');
    expect(css(byTestId('static-probe'), 'letter-spacing') === '2px', 'makeStaticStyles rule missing');
    expect(
      css(byTestId('dark-provider'), 'background-color') === css(byTestId('dark-reference'), 'background-color'),
      'nested dark theme not applied',
    );
    return 'theme, makeStyles, media query, makeResetStyles, makeStaticStyles, nested theme';
  });

  await check('keyframes inserted and running', () => {
    const probe = byTestId('make-styles-probe');
    const name = css(probe, 'animation-name');
    const running = probe.getAnimations().filter((a) => a instanceof CSSAnimation && a.animationName === name);
    expect(running.length === 1 && running[0]?.playState === 'running', `no running animation "${name}"`);
    const rules = [...document.styleSheets].flatMap((sheet) => [...sheet.cssRules]);
    expect(
      rules.some((r) => r instanceof CSSKeyframesRule && r.name === name),
      `no @keyframes ${name}`,
    );
    return `@keyframes ${name} running`;
  });

  await check('font icons load their font', async () => {
    const family = css(byTestId('font-icon').firstElementChild ?? byTestId('font-icon'), 'font-family');
    expect(family.includes('FluentSystemIcons'), `font icon uses "${family}"`);
    const faces = await document.fonts.load('20px FluentSystemIconsFilled');
    expect(faces.length > 0 && faces.every((f) => f.status === 'loaded'), 'FluentSystemIconsFilled did not load');
    return `${faces.length} font face loaded (${family})`;
  });

  await check('theme switch after mount', async () => {
    const provider = byTestId('provider');
    const light = css(byTestId('light-reference'), 'background-color');
    const dark = css(byTestId('dark-reference'), 'background-color');
    expect(css(provider, 'background-color') === light, 'provider does not start light');
    button('Switch theme').click();
    await waitFor('dark theme', () => css(provider, 'background-color') === dark);
    button('Switch theme').click();
    await waitFor('light theme', () => css(provider, 'background-color') === light);
    return 'light → dark → light';
  });

  const portal = async (trigger: string, testId: string, close: () => void, background?: string) => {
    button(trigger).click();
    const surface = await waitFor(trigger, () => {
      const s = document.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
      return isShown(s) && s;
    });
    expect(/absolute|fixed/.test(css(surface, 'position')), `${testId} is not positioned`);
    const actual = css(surface, 'background-color');
    expect(actual !== 'rgba(0, 0, 0, 0)', `${testId} has no background`);
    if (background) expect(actual === background, `${testId} background ${actual}, expected ${background}`);
    close();
    await waitFor(`${testId} to close`, () => !isShown(document.querySelector(`[data-testid="${testId}"]`)));
  };

  await check('portals open styled and positioned', async () => {
    const light = css(byTestId('light-reference'), 'background-color');
    await portal('Open menu', 'menu-popover', () => button('Open menu').click(), light);
    await portal('Open popover', 'popover-surface', () => button('Open popover').click());
    await portal('Open dialog', 'dialog-surface', () => button('Cancel').click());
    await portal('Open drawer', 'drawer', () => button('Close drawer').click());
    button('Show toast').click();
    const toast = await waitFor('toast', () => {
      const t = document.querySelector('[data-testid="toast"]');
      return isShown(t) && t;
    });
    expect(css(toast, 'background-color') !== 'rgba(0, 0, 0, 0)', 'toast has no background');
    return 'menu, popover, dialog, drawer, toast';
  });

  await check('portal inside a nested dark theme', async () => {
    const dark = css(byTestId('dark-reference'), 'background-color');
    await portal('Open dark menu', 'dark-menu-popover', () => button('Open dark menu').click(), dark);
    return 'menu popover takes the dark theme';
  });

  await check('portal into a custom mount node', () => {
    const probe = byTestId('portal-probe');
    expect(byTestId('portal-mount').contains(probe), 'portal content is not in its mount node');
    expect(css(probe, 'border-top-width') === '3px', 'portal content is unstyled');
    return 'styled inside its mount node';
  });

  await check('right-to-left rules', async () => {
    const probe = byTestId('rtl-probe');
    expect(css(probe, 'direction') === 'rtl', 'not right-to-left');
    expect(css(probe, 'padding-right') === '11px' && css(probe, 'padding-left') === '0px', 'rule not mirrored');
    await portal('Open RTL popover', 'rtl-popover-surface', () => button('Open RTL popover').click());
    return 'mirrored rule and popover';
  });

  await check('no violations during normal use', () => {
    expect(violations.length === 0, violations.join('; '));
    return '0 violations';
  });

  await check('positive controls: the policy is enforced', async () => {
    const before = violations.length;
    const target = byTestId('nonce-control');
    const head = document.head;

    const injected = document.createElement('style');
    injected.textContent = '[data-testid="nonce-control"] { color: rgb(255, 0, 0); }';
    const empty = document.createElement('style');
    const wrongNonce = document.createElement('style');
    wrongNonce.setAttribute('nonce', 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA');
    wrongNonce.textContent = '[data-testid="nonce-control"] { color: rgb(255, 0, 0); }';
    head.append(injected, empty, wrongNonce);

    const attribute = document.createElement('div');
    attribute.setAttribute('style', 'color: rgb(255, 0, 0)');
    document.body.append(attribute);

    let policyRefused = false;
    try {
      window.trustedTypes?.createPolicy('fluent-check-probe', {});
    } catch (e) {
      policyRefused = e instanceof TypeError;
    }

    // The authorised path: an element carrying the page nonce may add rules.
    const authorised = document.createElement('style');
    authorised.setAttribute('nonce', nonce);
    authorised.textContent = '[data-testid="nonce-control"] { letter-spacing: 3px; }';
    head.append(authorised);

    await waitFor('control violations', () => violations.length - before >= CONTROL_DIRECTIVES.length, 3000).catch(
      () => undefined,
    );
    await nextFrame();
    const caused = violations.slice(before).map(directive).sort();
    const results = {
      injectedBlocked: injected.sheet === null,
      emptyUnnoncedBlocked: empty.sheet === null,
      wrongNonceBlocked: wrongNonce.sheet === null,
      attributeBlocked: attribute.style.color === '',
      policyRefused,
      authorisedApplied: authorised.sheet !== null && css(target, 'letter-spacing') === '3px',
      targetNotRed: css(target, 'color') !== 'rgb(255, 0, 0)',
    };
    for (const element of [injected, empty, wrongNonce, authorised, attribute]) element.remove();

    const failed = Object.entries(results).filter(([, ok]) => !ok);
    expect(failed.length === 0, `failed: ${failed.map(([name]) => name).join(', ')}`);
    expect(
      JSON.stringify(caused) === JSON.stringify([...CONTROL_DIRECTIVES]),
      `violations ${JSON.stringify(caused)}, expected ${JSON.stringify(CONTROL_DIRECTIVES)}`,
    );
    return 'injected, empty and wrong-nonce styles, style attribute and policy creation blocked; nonce path works';
  });

  return {
    pass: checks.every((c) => c.pass),
    userAgent: navigator.userAgent,
    brands: navigator.userAgentData?.brands.map((b) => `${b.brand} ${b.version}`).join(', ') ?? '',
    checks,
    violations: [...violations],
  };
}

export function SelfCheck() {
  const [report, setReport] = useState<SelfCheckReport | undefined>();
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    // Let the first render's styles, fonts and effects settle first.
    const timer = setTimeout(() => {
      void run().then((result) => {
        window.__fluentCheckReport = result;
        setReport(result);
      });
    }, 100);
    return () => clearTimeout(timer);
  }, []);

  const status = report === undefined ? 'running' : report.pass ? 'pass' : 'fail';
  return (
    <section data-testid="self-check" data-status={status}>
      <h2>Self-check: {status.toUpperCase()}</h2>
      {report && <pre>{JSON.stringify(report, null, 2)}</pre>}
    </section>
  );
}
