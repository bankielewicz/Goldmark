#!/usr/bin/env node
// The graph's type icons as files (DEC-046 item 3): Cytoscape.js draws nodes on its own canvas, so each type's
// icon is an image, and the served CSP's `img-src 'self'` takes only same-origin files (no data: URIs). This
// writes src/graph/icons/<key>-light.svg and -dark.svg for every key in src/graph/typeIcons.ts, from the installed
// @fluentui/react-icons (the same 20 px paths inventory.tsx draws) in each theme's colorNeutralForeground2, as
// Fluent's own theme functions compute it from the brand ramp. tests/graph-icons.test.mjs fails if a file drifts.
//
// Usage: node scripts/graph-icons.mjs           (run in web/ after a Fluent or brand change)
//        node scripts/graph-icons.mjs --check   (exit 1 if a file differs, is missing or is extra)

import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { goldmarkBrand } from '../src/brand.ts';
import { graphIconNames } from '../src/graph/typeIcons.ts';

const web = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const iconsDirectory = join(web, 'src', 'graph', 'icons');

const require = createRequire(import.meta.url);
const components = require.resolve('@fluentui/react-components');
const { createLightTheme, createDarkTheme } = createRequire(components)('@fluentui/react-theme');

/** The theme colour each set of icons is drawn in. */
export const iconColours = {
  light: createLightTheme(goldmarkBrand).colorNeutralForeground2,
  dark: createDarkTheme(goldmarkBrand).colorNeutralForeground2,
};

/** The installed @fluentui/react-icons package's folder. */
function iconsPackage() {
  let folder = dirname(require.resolve('@fluentui/react-icons'));
  while (!existsSync(join(folder, 'package.json')) || !folder.endsWith('react-icons')) {
    const parent = dirname(folder);
    if (parent === folder) throw new Error('@fluentui/react-icons is not installed');
    folder = parent;
  }
  return folder;
}

/** The path data of a Fluent icon component ("DesktopRegular"), read from the package's SVG atom. */
export function iconPaths(componentName) {
  const stem = componentName
    .replace(/Regular$/, '')
    .replace(/(?<=[a-z0-9])(?=[A-Z])/g, '-')
    .toLowerCase();
  const source = readFileSync(join(iconsPackage(), 'lib', 'atoms', 'svg', `${stem}.js`), 'utf8');
  const found = source.match(new RegExp(`createFluentIcon\\('${componentName}', "1em", \\[([^\\]]*)\\]`));
  if (!found) throw new Error(`${componentName} not found in @fluentui/react-icons (${stem}.js)`);
  return JSON.parse(`[${found[1]}]`);
}

/** Every icon file, by name, as this build should have it. */
export function expectedIcons() {
  const files = new Map();
  for (const [key, componentName] of Object.entries(graphIconNames)) {
    const paths = iconPaths(componentName);
    for (const [theme, colour] of Object.entries(iconColours)) {
      const body = paths.map((d) => `<path fill="${colour}" d="${d}"/>`).join('');
      files.set(
        `${key}-${theme}.svg`,
        `<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">${body}</svg>\n`,
      );
    }
  }
  return files;
}

/** The differences between the icon files on disk and the expected ones. */
export function iconDrift() {
  const expected = expectedIcons();
  const actual = existsSync(iconsDirectory) ? readdirSync(iconsDirectory).filter((f) => f.endsWith('.svg')) : [];
  const problems = [];
  for (const [name, content] of expected) {
    const path = join(iconsDirectory, name);
    if (!existsSync(path)) problems.push(`missing ${name}`);
    else if (readFileSync(path, 'utf8') !== content) problems.push(`differs ${name}`);
  }
  for (const name of actual) {
    if (!expected.has(name)) problems.push(`extra ${name}`);
  }
  return problems;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.includes('--check')) {
    const problems = iconDrift();
    for (const problem of problems) console.error(`graph-icons: ${problem}`);
    process.exit(problems.length > 0 ? 1 : 0);
  }
  mkdirSync(iconsDirectory, { recursive: true });
  const files = expectedIcons();
  for (const [name, content] of files) writeFileSync(join(iconsDirectory, name), content);
  console.log(`graph-icons: wrote ${files.size} files to src/graph/icons`);
}
