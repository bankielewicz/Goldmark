// The graph's type icons (DEC-046 item 3): src/graph/icons holds each type icon as an SVG file per theme, because
// Cytoscape.js draws on a canvas and the served CSP takes only same-origin images. This fails when a file drifts
// from the installed @fluentui/react-icons or the theme (run `node scripts/graph-icons.mjs` to rewrite them), or
// when src/graph/typeIcons.ts and src/ui/inventory.tsx's icons disagree, so a node never shows another icon than
// the type's badge.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

import { iconColours, iconDrift } from '../scripts/graph-icons.mjs';
import { graphIconNames } from '../src/graph/typeIcons.ts';

describe('graph icons', () => {
  it('match the installed Fluent icons in the theme colour', () => {
    assert.deepEqual(iconDrift(), []);
  });

  it('use the same icon for every key as the type badges', () => {
    const inventory = readFileSync(fileURLToPath(import.meta.resolve('../src/ui/inventory.tsx')), 'utf8');
    const map = inventory.slice(inventory.indexOf('const icons: Record<string, ReactElement> = {'));
    const badges = Object.fromEntries(
      [...map.slice(0, map.indexOf('};')).matchAll(/^\s*'?([a-z-]+)'?: <([A-Za-z]+) \/>,$/gm)].map((m) => [m[1], m[2]]),
    );
    assert.ok(Object.keys(badges).length >= 20, 'inventory.tsx icons not found');
    assert.deepEqual(graphIconNames, badges);
  });

  it('are drawn in a foreground colour of each theme', () => {
    assert.match(iconColours.light, /^#[0-9a-f]{6}$/i);
    assert.match(iconColours.dark, /^#[0-9a-f]{6}$/i);
    assert.notEqual(iconColours.light, iconColours.dark);
  });
});
