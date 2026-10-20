// Checks the production build for anything the CSP (NFR-SEC-2) would block or
// that would need 'unsafe-inline': inline scripts, inline styles, event-handler
// attributes and data: URLs. Run after `pnpm build`: node --test tests/
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const dist = join(dirname(fileURLToPath(import.meta.url)), '..', 'dist');
const indexPath = join(dist, 'index.html');

describe('production build', { skip: !existsSync(indexPath) && 'dist/ not built; run pnpm build first' }, () => {
  const html = existsSync(indexPath) ? readFileSync(indexPath, 'utf8') : '';

  it('has no inline scripts: every <script> has a same-origin src', () => {
    // End tags may carry whitespace or (ignored) attributes, e.g. "</script >".
    const scripts = [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script\b[^>]*>/gi)];
    assert.equal(scripts.length, (html.match(/<script\b/gi) ?? []).length, 'unterminated or malformed script tag');
    assert.ok(scripts.length > 0, 'expected at least one script tag');
    for (const [, attrs, body] of scripts) {
      assert.match(attrs, /\bsrc="\/assets\/[^"]+\.js"/, `script without same-origin src: ${attrs}`);
      assert.equal(body.trim(), '', 'script tag has inline content');
    }
  });

  it('has no inline styles, event handlers or data: URLs', () => {
    assert.doesNotMatch(html, /<style\b/i, 'inline <style> element');
    assert.doesNotMatch(html, /\sstyle=/i, 'inline style attribute');
    assert.doesNotMatch(html, /\son[a-z]+=/i, 'inline event handler attribute');
    assert.doesNotMatch(html, /data:/i, 'data: URL');
  });

  it('leaves exactly one style-nonce placeholder for the server (DEC-037)', () => {
    const metas = [...html.matchAll(/<meta property="csp-nonce" nonce="__GOLDMARK_STYLE_NONCE__"\s*\/?>/g)];
    assert.equal(metas.length, 1, 'expected one <meta property="csp-nonce" nonce="__GOLDMARK_STYLE_NONCE__">');
    assert.equal(html.split('__GOLDMARK_STYLE_NONCE__').length, 2, 'the placeholder appears more than once');
  });

  it('emits hashed assets only under /assets', () => {
    const assets = readdirSync(join(dist, 'assets'));
    assert.ok(
      assets.some((f) => /^index-[\w-]+\.js$/.test(f)),
      `no hashed JS entry in ${assets.join(', ')}`,
    );
    for (const file of assets) {
      assert.match(file, /-[\w-]{8,}\.(js|css)$/, `unexpected unhashed asset ${file}`);
    }
  });

  it('does not ship source maps', () => {
    assert.ok(!readdirSync(join(dist, 'assets')).some((f) => f.endsWith('.map')));
  });
});
