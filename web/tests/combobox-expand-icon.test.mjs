// Fluent UI 9.75 made a Combobox's expand arrow a keyboard stop of its own; Goldmark keeps it out of
// the Tab order (src/ui/combobox.ts says why). The arrow is a default of every Combobox, so a new one
// that forgets the property brings the stop back on its screen only. This test reads the source: every
// Combobox in src/ passes `expandIcon={comboboxExpandIcon}` as its first property, and the installed
// Fluent still lets that property decide the arrow's tabIndex. It can't see a later property that
// overrides the first; the browser tests check the rendered arrows (expectAccessible in
// e2e/session.ts, and the audit log's Tab from Actor to Action in e2e/auth.spec.ts).
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { comboboxExpandIcon } from '../src/ui/combobox.ts';

const src = join(dirname(fileURLToPath(import.meta.url)), '..', 'src');
const sources = readdirSync(src, { recursive: true })
  .filter((name) => name.endsWith('.tsx'))
  .map((name) => ({ name, lines: readFileSync(join(src, name), 'utf8').split('\n') }));

describe('Combobox expand arrow (DEC-040, Fluent UI 9.75)', () => {
  it('keeps the arrow out of the Tab order', () => {
    assert.deepEqual(comboboxExpandIcon, { tabIndex: -1 });
  });

  it('is the first property of every Combobox in src/', () => {
    const uses = [];
    for (const { name, lines } of sources) {
      lines.forEach((line, index) => {
        if (!/<Combobox\b/.test(line)) return;
        const tag = `${line.trim()} ${(lines[index + 1] ?? '').trim()}`;
        uses.push({ where: `${name}:${index + 1}`, ok: /^<Combobox\s+expandIcon=\{comboboxExpandIcon\}/.test(tag) });
      });
    }
    assert.ok(uses.length >= 8, `found only ${uses.length} Combobox elements: has the way they are written changed?`);
    assert.deepEqual(
      uses.filter((use) => !use.ok).map((use) => use.where),
      [],
      'these Combobox elements need expandIcon={comboboxExpandIcon} as their first property (src/ui/combobox.ts)',
    );
  });

  it('is still what the installed Fluent UI uses for the arrow’s tabIndex', () => {
    // The arrow's slot takes Fluent's values as defaults, so a property given by the app wins; Fluent
    // itself only ever sets -1 afterwards. If an update changes either, look at the arrow again.
    const require = createRequire(import.meta.url);
    const fluent = require.resolve('@fluentui/react-components');
    const entry = createRequire(fluent).resolve('@fluentui/react-combobox');
    const packageName = join('@fluentui', 'react-combobox');
    const root = entry.slice(0, entry.lastIndexOf(packageName) + packageName.length);
    // The ES module build is the one Vite bundles.
    const slot = readFileSync(join(root, 'lib', 'components', 'Combobox', 'useComboboxExpandIconSlot.js'), 'utf8');
    assert.match(
      slot,
      /slot\.optional\(expandIconFromProps, \{\s*renderByDefault: true,\s*defaultProps: \{[^}]*tabIndex: disabled \? -1 : 0\s*\}/,
    );
    assert.deepEqual(slot.match(/expandIcon\.tabIndex = [^;]+;/g), ['expandIcon.tabIndex = -1;']);
  });
});
