// WCAG 2.2 contrast of the theme pairs Goldmark's screens use (DEC-039; NFR-A11Y-1): recomputed from
// the app's brand ramp (src/brand.ts) and Fluent's own theme functions, so a change to the ramp or a
// Fluent upgrade that lowers a ratio fails here. Text needs 4.5:1, UI parts (strokes, focus, icons
// that carry meaning) 3:1. Runs with the repository's Node (type stripping loads the .ts module).
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';

import { goldmarkBrand } from '../src/brand.ts';

// @fluentui/react-components re-exports createLightTheme/createDarkTheme from @fluentui/react-theme;
// loading that package alone keeps this test fast.
const components = createRequire(import.meta.url).resolve('@fluentui/react-components');
const { createLightTheme, createDarkTheme } = createRequire(components)('@fluentui/react-theme');
const themes = { light: createLightTheme(goldmarkBrand), dark: createDarkTheme(goldmarkBrand) };

function luminance(hex) {
  const value = hex.replace('#', '');
  const [r, g, b] = [0, 2, 4]
    .map((i) => parseInt(value.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrast(a, b) {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
}

const text = [
  ['colorNeutralForeground1', 'colorNeutralBackground1'],
  ['colorNeutralForeground2', 'colorNeutralBackground1'],
  ['colorNeutralForeground3', 'colorNeutralBackground1'],
  ['colorNeutralForeground3', 'colorNeutralBackground2'],
  ['colorNeutralForeground3', 'colorNeutralBackground3'],
  ['colorNeutralForegroundOnBrand', 'colorBrandBackground'],
  ['colorNeutralForegroundOnBrand', 'colorBrandBackgroundHover'],
  ['colorNeutralForegroundOnBrand', 'colorBrandBackgroundPressed'],
  ['colorBrandForeground1', 'colorNeutralBackground1'],
  ['colorBrandForeground1', 'colorNeutralBackground2'],
  ['colorBrandForegroundLink', 'colorNeutralBackground1'],
  ['colorBrandForeground2', 'colorBrandBackground2'],
  ['colorNeutralForeground1', 'colorBrandBackground2'],
  ['colorPaletteRedForeground1', 'colorNeutralBackground1'],
  ['colorPaletteGreenForeground1', 'colorNeutralBackground1'],
  ['colorStatusDangerForeground1', 'colorStatusDangerBackground1'],
  ['colorStatusSuccessForeground1', 'colorStatusSuccessBackground1'],
  ['colorStatusWarningForeground1', 'colorStatusWarningBackground1'],
];

const ui = [
  ['colorNeutralStrokeAccessible', 'colorNeutralBackground1'],
  ['colorNeutralStrokeAccessible', 'colorNeutralBackground3'],
  ['colorCompoundBrandStroke', 'colorNeutralBackground1'],
  ['colorCompoundBrandBackground', 'colorNeutralBackground1'],
  ['colorStrokeFocus2', 'colorNeutralBackground1'],
  ['colorStatusDangerForeground1', 'colorNeutralBackground1'],
  ['colorStatusSuccessForeground1', 'colorNeutralBackground1'],
];

describe('theme contrast (WCAG 2.2 AA)', () => {
  it('uses the approved brand ramp (shade 80 is #8a5a00)', () => {
    assert.equal(goldmarkBrand[80], '#8a5a00');
    assert.equal(Object.keys(goldmarkBrand).length, 16);
  });

  for (const [name, theme] of Object.entries(themes)) {
    for (const [foreground, background] of text) {
      it(`${name}: ${foreground} on ${background} is at least 4.5:1 (text)`, () => {
        const ratio = contrast(theme[foreground], theme[background]);
        assert.ok(ratio >= 4.5, `${theme[foreground]} on ${theme[background]}: ${ratio.toFixed(2)}:1`);
      });
    }
    for (const [foreground, background] of ui) {
      it(`${name}: ${foreground} on ${background} is at least 3:1 (UI)`, () => {
        const ratio = contrast(theme[foreground], theme[background]);
        assert.ok(ratio >= 3, `${theme[foreground]} on ${theme[background]}: ${ratio.toFixed(2)}:1`);
      });
    }
  }
});
