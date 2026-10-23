#!/usr/bin/env node
// Post-install, pre-build dependency check (NFR-SEC-9, DEC-011).
//
// The pre-install check (check-dependency-policy.mjs) reads the lockfile as
// text, which can never be a perfect YAML parser. This check closes that gap by
// asking pnpm itself what it installed: every package in pnpm's resolved tree
// must come from the npm registry under its own name, and none may be banned.
// Install scripts are disabled (ignoreScripts), so nothing that was fetched has
// run yet when this check executes; it must pass before any build or test step.
//
// Usage: node scripts/check-installed-tree.mjs   (run in web/ after pnpm install)

import { execFileSync, execSync } from 'node:child_process';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { FORBIDDEN } from './check-dependency-policy.mjs';

const REGISTRY = 'https://registry.npmjs.org/';

/** The tarball URL the npm registry serves for name@version. */
export function expectedTarball(name, version) {
  const base = name.startsWith('@') ? name.split('/')[1] : name;
  return `${REGISTRY}${name}/-/${base}-${version}.tgz`;
}

/** Returns policy violations for the output of `pnpm list --json --depth Infinity`. */
export function checkInstalledTree(listOutput) {
  const errors = [];
  const roots = Array.isArray(listOutput) ? listOutput : [listOutput];
  // pnpm omits the children of some repeated occurrences, so every occurrence is walked;
  // each distinct package is checked and reported once.
  const checked = new Set();
  const packages = new Set();
  const walk = (node, path) => {
    for (const section of ['dependencies', 'devDependencies', 'optionalDependencies']) {
      for (const [alias, dep] of Object.entries(node?.[section] ?? {})) {
        const name = dep.from ?? alias;
        const where = [...path, `${name}@${dep.version}`].join(' > ');
        packages.add(`${name}@${dep.version}`);
        const key = `${alias}|${name}@${dep.version}|${dep.resolved}`;
        if (checked.has(key)) {
          walk(dep, [...path, `${name}@${dep.version}`]);
          continue;
        }
        checked.add(key);
        for (const rule of FORBIDDEN) {
          if (rule.test(name) || rule.test(alias))
            errors.push(`installed banned package "${name}" (${where}) — ${rule.reason}`);
        }
        if (typeof dep.resolved !== 'string') {
          errors.push(`"${where}" has no registry resolution (pnpm reports none)`);
        } else if (dep.resolved !== expectedTarball(name, dep.version)) {
          errors.push(
            `"${where}" resolves to ${dep.resolved}, not the registry tarball ${expectedTarball(name, dep.version)}`,
          );
        }
        walk(dep, [...path, `${name}@${dep.version}`]);
      }
    }
  };
  for (const root of roots) walk(root, []);
  if (packages.size === 0) errors.push('pnpm reported no installed packages; run pnpm install --frozen-lockfile first');
  return { errors, count: packages.size };
}

const isMain = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isMain) {
  const options = { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 };
  // On Windows pnpm is a .cmd shim, which Node starts only through the shell; the command is a constant.
  const output =
    process.platform === 'win32'
      ? execSync('pnpm list --json --depth Infinity', options)
      : execFileSync('pnpm', ['list', '--json', '--depth', 'Infinity'], options);
  const { errors, count } = checkInstalledTree(JSON.parse(output));
  if (errors.length > 0) {
    console.error(`Installed-tree check FAILED (${errors.length} problem(s)):`);
    for (const e of errors) console.error(`  - ${e}`);
    process.exit(1);
  }
  console.log(`Installed-tree check passed (${count} packages, all from ${REGISTRY}).`);
}
