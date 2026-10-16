#!/usr/bin/env node
// Dependency policy check for the Goldmark SPA (PRD NFR-SEC-9, DEC-011).
//
// Runs with plain Node.js and no dependencies, so CI can run it BEFORE any
// package is installed. It fails when:
//   - a direct dependency is not in dependency-allowlist.json (or is in the
//     wrong section, or is not pinned to an exact registry version);
//   - a banned SSR/RSC package appears in package.json or pnpm-lock.yaml;
//   - a lockfile entry resolves from anywhere other than the registry;
//   - a pnpm/npm supply-chain setting is missing or weakened;
//   - package.json declares install lifecycle scripts.
//
// Usage: node scripts/check-dependency-policy.mjs [webDir]

import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

/** Packages that must never appear anywhere in the dependency tree (D1). */
export const FORBIDDEN = [
  { test: (n) => n === 'next', reason: 'Next.js (SSR/RSC framework) is banned by D1 / NFR-SEC-9' },
  {
    test: (n) => n.startsWith('react-server-dom-'),
    reason: 'React Server Components runtimes are banned by D1 / NFR-SEC-9',
  },
  { test: (n) => n.startsWith('@remix-run/'), reason: 'Remix (SSR) packages are banned by D1 / NFR-SEC-9' },
  {
    test: (n) => n.startsWith('@react-router/'),
    reason: 'React Router framework/server packages are banned; use react-router in library mode (DEC-011)',
  },
  { test: (n) => n === '@vitejs/plugin-rsc', reason: 'The Vite RSC plugin is banned by D1 (DEC-011)' },
];

/** pnpm-workspace.yaml settings that must be present with a compliant value. */
const REQUIRED_PNPM_SETTINGS = {
  minimumReleaseAge: { ok: (v) => Number(v) >= 10080, expect: '>= 10080 (7 days, in minutes)' },
  minimumReleaseAgeStrict: { ok: (v) => v === 'true', expect: 'true' },
  trustPolicy: { ok: (v) => v === 'no-downgrade', expect: 'no-downgrade' },
  blockExoticSubdeps: { ok: (v) => v === 'true', expect: 'true' },
  ignoreScripts: { ok: (v) => v === 'true', expect: 'true' },
  strictDepBuilds: { ok: (v) => v === 'true', expect: 'true' },
  engineStrict: { ok: (v) => v === 'true', expect: 'true' },
};

/** Settings that weaken the policy and must not be used without changing this script. */
const FORBIDDEN_PNPM_SETTINGS = [
  'dangerouslyAllowAllBuilds',
  'minimumReleaseAgeExclude',
  'trustPolicyExclude',
  'trustPolicyIgnoreAfter',
  'neverBuiltDependencies',
  'onlyBuiltDependencies',
];

const REQUIRED_NPMRC = {
  registry: 'https://registry.npmjs.org/',
  'ignore-scripts': 'true',
};

const LIFECYCLE_SCRIPTS = [
  'preinstall',
  'install',
  'postinstall',
  'prepare',
  'preprepare',
  'postprepare',
  'prepublish',
  'prepack',
  'postpack',
];

const EXACT_VERSION = /^\d+\.\d+\.\d+$/;
const PACKAGE_MANAGER_PIN = /^pnpm@\d+\.\d+\.\d+\+sha512\.[0-9a-f]{128}$/;

function stripComment(line) {
  const hash = line.indexOf(' #');
  const cut = line.startsWith('#') ? 0 : hash;
  return (cut >= 0 ? line.slice(0, cut) : line).trimEnd();
}

function unquote(value) {
  const v = value.trim();
  if ((v.startsWith("'") && v.endsWith("'")) || (v.startsWith('"') && v.endsWith('"'))) {
    return v.slice(1, -1);
  }
  return v;
}

/**
 * Minimal reader for the flat pnpm-workspace.yaml we own: top-level scalars and
 * one level of block or empty-inline maps. Anything fancier is reported, so the
 * file stays simple enough for this check to reason about.
 */
export function parseWorkspaceYaml(text) {
  const settings = {};
  const errors = [];
  let currentMap = null;
  for (const raw of text.split(/\r?\n/)) {
    const line = stripComment(raw);
    if (line.trim() === '') continue;
    const indented = /^\s/.test(line);
    if (indented) {
      const m = line.match(/^\s+(['"]?)([^'":]+)\1:\s*(.*)$/);
      if (!currentMap || !m) {
        errors.push(`pnpm-workspace.yaml: unsupported structure near "${raw.trim()}"`);
        continue;
      }
      settings[currentMap][m[2]] = unquote(m[3]);
      continue;
    }
    const m = line.match(/^([A-Za-z][A-Za-z0-9]*):\s*(.*)$/);
    if (!m) {
      errors.push(`pnpm-workspace.yaml: unsupported line "${raw.trim()}"`);
      continue;
    }
    const [, key, value] = m;
    if (value === '' || value === '{}') {
      settings[key] = {};
      currentMap = value === '' ? key : null;
    } else {
      settings[key] = unquote(value);
      currentMap = null;
    }
  }
  return { settings, errors };
}

export function parseNpmrc(text) {
  const out = {};
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (line === '' || line.startsWith('#') || line.startsWith(';')) continue;
    const eq = line.indexOf('=');
    if (eq > 0) out[line.slice(0, eq).trim()] = line.slice(eq + 1).trim();
  }
  return out;
}

/** Package name from a pnpm v9 lockfile key such as "@scope/name@1.2.3(peer@1.0.0)". */
export function nameFromLockKey(key) {
  const at = key.indexOf('@', key.startsWith('@') ? 1 : 0);
  return at > 0 ? key.slice(0, at) : key;
}

/** Package names and resolution lines from the packages/snapshots sections. */
export function readLockfile(text) {
  const names = new Set();
  const badResolutions = [];
  let section = null;
  let currentKey = null;
  for (const raw of text.split(/\r?\n/)) {
    if (/^\S/.test(raw)) {
      section = raw.replace(/:.*$/, '');
      currentKey = null;
      continue;
    }
    if (section !== 'packages' && section !== 'snapshots') continue;
    const keyMatch = raw.match(/^ {2}(['"]?)(.+?)\1:\s*(\{\})?\s*$/);
    if (keyMatch) {
      currentKey = keyMatch[2];
      names.add(nameFromLockKey(currentKey));
      continue;
    }
    const res = raw.match(/^ {4}resolution:\s*(.*)$/);
    if (res && section === 'packages') {
      const value = res[1];
      const registryOnly = /integrity:\s*sha512-/.test(value) && !/(tarball|repo|directory|type):/.test(value);
      if (!registryOnly) badResolutions.push(`${currentKey}: ${value}`);
    }
  }
  return { names, badResolutions };
}

function readJson(path, errors) {
  try {
    return JSON.parse(readFileSync(path, 'utf8'));
  } catch (err) {
    errors.push(`${path}: cannot read JSON (${err.message})`);
    return null;
  }
}

function checkForbidden(name, where, errors) {
  for (const rule of FORBIDDEN) {
    if (rule.test(name)) errors.push(`${where}: banned package "${name}" — ${rule.reason}`);
  }
}

/** Returns a list of policy violations for the web project in `webDir`. */
export function checkPolicy(webDir) {
  const errors = [];
  const pkgPath = join(webDir, 'package.json');
  const allowPath = join(webDir, 'dependency-allowlist.json');
  const pkg = readJson(pkgPath, errors);
  const allow = readJson(allowPath, errors);
  if (!pkg || !allow) return errors;

  // --- package.json -------------------------------------------------------
  if (!PACKAGE_MANAGER_PIN.test(pkg.packageManager ?? '')) {
    errors.push('package.json: "packageManager" must pin pnpm to an exact version with a sha512 hash');
  }
  for (const field of ['optionalDependencies', 'peerDependencies', 'bundleDependencies', 'bundledDependencies']) {
    if (pkg[field] && Object.keys(pkg[field]).length > 0) {
      errors.push(`package.json: "${field}" is not allowed (NFR-SEC-9); use dependencies or devDependencies`);
    }
  }
  for (const script of LIFECYCLE_SCRIPTS) {
    if (pkg.scripts?.[script] !== undefined) {
      errors.push(`package.json: lifecycle script "${script}" is not allowed`);
    }
  }
  const sections = [
    ['dependencies', 'runtime'],
    ['devDependencies', 'dev'],
  ];
  for (const [field, allowSection] of sections) {
    for (const [name, spec] of Object.entries(pkg[field] ?? {})) {
      checkForbidden(name, `package.json ${field}`, errors);
      const entry = allow[allowSection]?.[name];
      if (!entry) {
        const other = allowSection === 'runtime' ? 'dev' : 'runtime';
        errors.push(
          allow[other]?.[name]
            ? `package.json: "${name}" is allow-listed as ${other}, not ${allowSection}; move it or update the allow-list in a reviewed PR`
            : `package.json: "${name}" is not in dependency-allowlist.json (${allowSection}); adding a dependency requires a reviewed allow-list entry`,
        );
      } else if (typeof entry.justification !== 'string' || entry.justification.trim().length < 10) {
        errors.push(`dependency-allowlist.json: "${name}" needs a justification`);
      }
      if (!EXACT_VERSION.test(spec)) {
        errors.push(`package.json: "${name}" must be pinned to an exact registry version (found "${spec}")`);
      }
    }
  }
  for (const section of ['runtime', 'dev']) {
    for (const [name, entry] of Object.entries(allow[section] ?? {})) {
      checkForbidden(name, `dependency-allowlist.json ${section}`, errors);
      if (typeof entry?.justification !== 'string' || entry.justification.trim().length < 10) {
        errors.push(`dependency-allowlist.json: "${name}" needs a justification`);
      }
    }
  }

  // --- pnpm-workspace.yaml -------------------------------------------------
  const wsPath = join(webDir, 'pnpm-workspace.yaml');
  if (!existsSync(wsPath)) {
    errors.push('pnpm-workspace.yaml is missing (it carries the pnpm supply-chain settings)');
  } else {
    const { settings, errors: yamlErrors } = parseWorkspaceYaml(readFileSync(wsPath, 'utf8'));
    errors.push(...yamlErrors);
    for (const [key, rule] of Object.entries(REQUIRED_PNPM_SETTINGS)) {
      const value = settings[key];
      if (value === undefined || typeof value !== 'string' || !rule.ok(value)) {
        errors.push(`pnpm-workspace.yaml: "${key}" must be ${rule.expect} (found ${JSON.stringify(value)})`);
      }
    }
    for (const key of FORBIDDEN_PNPM_SETTINGS) {
      if (settings[key] !== undefined) {
        errors.push(`pnpm-workspace.yaml: "${key}" weakens the supply-chain policy and is not allowed`);
      }
    }
    const builds = settings.allowBuilds;
    if (typeof builds !== 'object' || builds === null) {
      errors.push('pnpm-workspace.yaml: "allowBuilds" must be present as a map (use {} when empty)');
    } else {
      for (const [name, value] of Object.entries(builds)) {
        if (value === 'true' && !allow.allowBuilds?.[name]) {
          errors.push(
            `pnpm-workspace.yaml: build scripts for "${name}" are allowed but not approved in dependency-allowlist.json "allowBuilds"`,
          );
        } else if (value !== 'true' && value !== 'false') {
          errors.push(`pnpm-workspace.yaml: allowBuilds "${name}" must be true or false (found "${value}")`);
        }
      }
    }
  }

  // --- .npmrc -------------------------------------------------------------
  const npmrcPath = join(webDir, '.npmrc');
  const npmrc = existsSync(npmrcPath) ? parseNpmrc(readFileSync(npmrcPath, 'utf8')) : {};
  for (const [key, expected] of Object.entries(REQUIRED_NPMRC)) {
    if (npmrc[key] !== expected) errors.push(`.npmrc: "${key}" must be "${expected}"`);
  }

  // --- pnpm-lock.yaml ------------------------------------------------------
  const lockPath = join(webDir, 'pnpm-lock.yaml');
  const hasDeps =
    Object.keys(pkg.dependencies ?? {}).length + Object.keys(pkg.devDependencies ?? {}).length > 0;
  if (!existsSync(lockPath)) {
    if (hasDeps) errors.push('pnpm-lock.yaml is missing; installs must use a committed lockfile');
  } else {
    const { names, badResolutions } = readLockfile(readFileSync(lockPath, 'utf8'));
    for (const name of names) checkForbidden(name, 'pnpm-lock.yaml', errors);
    for (const entry of badResolutions) {
      errors.push(`pnpm-lock.yaml: non-registry resolution is not allowed: ${entry}`);
    }
  }

  return errors;
}

const isMain = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isMain) {
  const webDir = resolve(process.argv[2] ?? join(dirname(fileURLToPath(import.meta.url)), '..'));
  const errors = checkPolicy(webDir);
  if (errors.length > 0) {
    console.error(`Dependency policy check FAILED (${errors.length} problem(s)):`);
    for (const e of errors) console.error(`  - ${e}`);
    process.exit(1);
  }
  console.log('Dependency policy check passed.');
}
