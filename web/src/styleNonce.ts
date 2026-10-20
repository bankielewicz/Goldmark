import { createDOMRenderer, type GriffelRenderer } from '@fluentui/react-components';

/**
 * This page's style nonce (DEC-037). The server writes a fresh one into
 * `<meta property="csp-nonce" nonce="…">` and into `style-src` on every shell response. It is
 * read through the element's `nonce` property: once the page is parsed the browser hides the
 * attribute's value from the DOM, so CSS selectors cannot read it.
 */
export function readStyleNonce(doc: Document = document): string {
  const nonce = doc.querySelector<HTMLMetaElement>('meta[property="csp-nonce"]')?.nonce ?? '';
  if (nonce === '') {
    throw new Error('The page has no style nonce, so its styles would be blocked by the Content-Security-Policy.');
  }
  return nonce;
}

/**
 * The Griffel renderer for Fluent UI: every `<style>` element it creates, including
 * FluentProvider's theme element, carries the page's nonce. Pass it to `RendererProvider`.
 */
export function createStyleRenderer(doc: Document = document): GriffelRenderer {
  return createDOMRenderer(doc, { styleElementAttributes: { nonce: readStyleNonce(doc) } });
}
