#!/usr/bin/env node
// Dependency policy check for the Goldmark SPA (PRD NFR-SEC-9, DEC-011).
//
// Runs with plain Node.js and no dependencies, so CI can run it BEFORE any
// package is installed. It fails when:
//   - a direct dependency is not in dependency-allowlist.json (or is in the
//     wrong section, or is not pinned to an exact registry version);
//   - a banned SSR/RSC package appears in package.json or pnpm-lock.yaml;
//   - a lockfile entry resolves from anywhere other than the registry;
//   - a pnpm/npm supply-chain setting is missing or weakened, or any setting
//     outside the reviewed allow-lists appears (e.g. a scoped registry);
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

/**
 * The only settings pnpm-workspace.yaml may contain. Anything else (registries,
 * overrides, policy exclusions, dangerouslyAllowAllBuilds, ...) can redirect or
 * weaken installs and must be reviewed by changing this list.
 */
const ALLOWED_PNPM_SETTINGS = new Set([...Object.keys(REQUIRED_PNPM_SETTINGS), 'allowBuilds', 'savePrefix']);

/**
 * The only keys .npmrc may contain, with their required values. Scoped
 * registries ("@scope:registry"), auth tokens, proxies, custom CAs or
 * strict-ssl overrides are rejected: they could point an allow-listed
 * name@version at another source.
 */
const REQUIRED_NPMRC = {
  registry: 'https://registry.npmjs.org/',
  'ignore-scripts': 'true',
};
const ALLOWED_NPMRC = new Map([...Object.entries(REQUIRED_NPMRC), ['engine-strict', 'true']]);

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

/*
 * Lockfile parsing. pnpm-lock.yaml is parsed structurally, but only as the YAML subset
 * pnpm 11 writes (block mappings, block sequences and a few flow forms), and every node
 * is then validated against the lockfile schema. Anything outside it fails the check:
 * unknown sections or keys, duplicate keys, double quotes or escapes, odd or jumping
 * indentation, anchors, aliases, tags, multi-document streams, URLs or protocols in
 * values. A spelling that YAML would treat as equivalent can therefore not hide a
 * package from this check, because it is rejected rather than read differently.
 */

const BARE_KEY = /^[A-Za-z0-9.][A-Za-z0-9._@/+()~-]*$/;
const QUOTED_KEY = /^'([^']+)'$/;
// Plain scalar: no YAML indicator first, and no ':', '#', quotes, brackets, braces, commas,
// anchors, aliases or tags anywhere (so no URLs or protocol specifiers either).
const BARE_SCALAR = /^[^\s\-?:,[\]{}#&*!|>'"%@`][^:#{}[\],&*!'"`]*$/;
const QUOTED_SCALAR = /^'([^':]*)'$/;
const CANONICAL_RESOLUTION = /^\{integrity: sha512-[A-Za-z0-9+/]+={0,2}\}$/;
const FLOW_MAP = /^\{[A-Za-z]+: (?:'[^':]*'|[^':#{}[\],]+)(?:, [A-Za-z]+: (?:'[^':]*'|[^':#{}[\],]+))*\}$/;
const FLOW_LIST = /^\[(?:'[^':]*'|[A-Za-z0-9_-]+)(?:, (?:'[^':]*'|[A-Za-z0-9_-]+))*\]$/;
const BOOLEAN = /^(true|false)$/;

/** Builds a tree of { key, value, children, items, line } nodes; returns structural errors. */
function buildTree(text) {
  const errors = [];
  const root = { key: '', value: null, children: new Map(), items: [], line: 0, depth: -1 };
  const stack = [root];
  text.split('\n').forEach((raw, index) => {
    const line = index + 1;
    const at = `pnpm-lock.yaml line ${line}`;
    if (raw.includes('\r') || raw.includes('\t'))
      return void errors.push(`${at}: tabs and carriage returns are not allowed`);
    if (raw.trim() === '') return;
    const indent = raw.length - raw.trimStart().length;
    if (indent % 2 !== 0) return void errors.push(`${at}: odd indentation`);
    const depth = indent / 2;
    while (stack.length > 1 && stack[stack.length - 1].depth >= depth) stack.pop();
    const parent = stack[stack.length - 1];
    if (depth !== parent.depth + 1) return void errors.push(`${at}: indentation jumps a level`);
    if (parent.value !== null) return void errors.push(`${at}: "${parent.key}" has both a value and children`);
    const body = raw.trim();
    if (body.startsWith('- ')) {
      if (parent.children.size > 0) return void errors.push(`${at}: mixed sequence and mapping under "${parent.key}"`);
      parent.items.push({ value: body.slice(2), line });
      return;
    }
    const match = body.match(/^('[^']*'|[^\s'"]+?):(?: (.*))?$/);
    if (!match) return void errors.push(`${at}: not a canonical "key: value" line`);
    const quoted = match[1].match(QUOTED_KEY);
    if (!quoted && !BARE_KEY.test(match[1])) return void errors.push(`${at}: key ${match[1]} is not canonical`);
    const key = quoted ? quoted[1] : match[1];
    if (quoted && !key.includes('@')) return void errors.push(`${at}: quoted key "${key}" is not canonical`);
    if (parent.items.length > 0) return void errors.push(`${at}: mixed sequence and mapping under "${parent.key}"`);
    if (parent.children.has(key)) return void errors.push(`${at}: duplicate key "${key}"`);
    const node = { key, value: match[2] ?? null, children: new Map(), items: [], line, depth };
    parent.children.set(key, node);
    stack.push(node);
  });
  return { root, errors };
}

/** Parses and validates pnpm-lock.yaml. Returns { errors, names } (all package names seen). */
export function parseLockfile(text) {
  const { root, errors } = buildTree(text);
  const names = new Set();
  const at = (node) => `pnpm-lock.yaml line ${node.line}`;
  const fail = (node, message) => errors.push(`${at(node)}: ${message}`);
  const isScalar = (v) => v !== null && (BARE_SCALAR.test(v) || QUOTED_SCALAR.test(v));
  const expectBlock = (node) => {
    if (node.value !== null && !(node.value === '{}' && node.children.size === 0))
      fail(node, `"${node.key}" must be a block`);
  };
  const onlyKeys = (node, allowed) => {
    for (const child of node.children.values()) {
      if (!allowed.includes(child.key)) fail(child, `unexpected key "${child.key}" under "${node.key}"`);
    }
    if (node.items.length > 0) fail(node, `"${node.key}" must not be a sequence`);
  };
  const addName = (name, node) => {
    if (!name || name.includes(':')) fail(node, `invalid package name "${name}"`);
    else names.add(name);
  };
  // name: version-or-range maps (dependencies, optionalDependencies, peerDependencies)
  const versionMap = (node) => {
    expectBlock(node);
    for (const dep of node.children.values()) {
      addName(dep.key, dep);
      if (!isScalar(dep.value) || dep.children.size > 0) fail(dep, `"${dep.key}" must map to a version or range`);
      const alias = dep.value?.match(/^(@?[^@\s]+)@\d/);
      if (alias) addName(alias[1], dep);
    }
  };

  onlyKeys(root, ['lockfileVersion', 'settings', 'importers', 'packages', 'snapshots']);
  const section = (key) => root.children.get(key);
  if (section('lockfileVersion')?.value !== "'9.0'") errors.push("pnpm-lock.yaml: lockfileVersion must be '9.0'");

  const settings = section('settings');
  if (settings) {
    expectBlock(settings);
    onlyKeys(settings, ['autoInstallPeers', 'excludeLinksFromLockfile']);
    for (const s of settings.children.values())
      if (!BOOLEAN.test(s.value ?? '')) fail(s, `"${s.key}" must be true or false`);
  }

  const importers = section('importers');
  if (importers) {
    expectBlock(importers);
    for (const importer of importers.children.values()) {
      if (importer.key !== '.') fail(importer, `unexpected importer "${importer.key}"`);
      expectBlock(importer);
      onlyKeys(importer, ['dependencies', 'devDependencies', 'optionalDependencies']);
      for (const group of importer.children.values()) {
        expectBlock(group);
        for (const dep of group.children.values()) {
          addName(dep.key, dep);
          expectBlock(dep);
          onlyKeys(dep, ['specifier', 'version']);
          for (const field of dep.children.values())
            if (!isScalar(field.value)) fail(field, `"${field.key}" must be a plain version`);
        }
      }
    }
  }

  const packages = section('packages');
  if (packages) {
    expectBlock(packages);
    for (const entry of packages.children.values()) {
      addName(nameFromLockKey(entry.key), entry);
      expectBlock(entry);
      onlyKeys(entry, [
        'resolution',
        'engines',
        'cpu',
        'os',
        'libc',
        'hasBin',
        'deprecated',
        'peerDependencies',
        'peerDependenciesMeta',
      ]);
      const resolution = entry.children.get('resolution');
      if (!resolution || !CANONICAL_RESOLUTION.test(resolution.value ?? '')) {
        fail(
          entry,
          `package ${entry.key} must have exactly one canonical registry resolution ({integrity: sha512-...})`,
        );
      }
      for (const field of entry.children.values()) {
        const ok = {
          resolution: () => true,
          engines: () => FLOW_MAP.test(field.value ?? ''),
          cpu: () => FLOW_LIST.test(field.value ?? ''),
          os: () => FLOW_LIST.test(field.value ?? ''),
          libc: () => FLOW_LIST.test(field.value ?? ''),
          hasBin: () => field.value === 'true',
          deprecated: () => field.value !== null && /^'[^']*'$|^[^\s'"&*!|>{[]/.test(field.value),
          peerDependencies: () => (versionMap(field), true),
          peerDependenciesMeta: () => {
            expectBlock(field);
            for (const meta of field.children.values()) {
              expectBlock(meta);
              onlyKeys(meta, ['optional']);
              for (const o of meta.children.values())
                if (!BOOLEAN.test(o.value ?? '')) fail(o, '"optional" must be true or false');
            }
            return true;
          },
        }[field.key]?.();
        if (ok === false) fail(field, `"${field.key}" is not in canonical form`);
      }
    }
  }

  const snapshots = section('snapshots');
  if (snapshots) {
    expectBlock(snapshots);
    for (const entry of snapshots.children.values()) {
      addName(nameFromLockKey(entry.key), entry);
      expectBlock(entry);
      onlyKeys(entry, ['dependencies', 'optionalDependencies', 'transitivePeerDependencies', 'optional']);
      for (const field of entry.children.values()) {
        if (field.key === 'dependencies' || field.key === 'optionalDependencies') versionMap(field);
        else if (field.key === 'optional' && field.value !== 'true') fail(field, '"optional" must be true');
        else if (field.key === 'transitivePeerDependencies') {
          if (field.value !== null || field.children.size > 0)
            fail(field, '"transitivePeerDependencies" must be a list');
          for (const item of field.items) {
            if (!BARE_KEY.test(item.value) && !QUOTED_KEY.test(item.value))
              fail(item, 'list item is not a package name');
            else addName(item.value.replace(/^'|'$/g, ''), item);
          }
        }
      }
    }
  }
  return { errors, names };
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
    for (const key of Object.keys(settings)) {
      if (!ALLOWED_PNPM_SETTINGS.has(key)) {
        errors.push(
          `pnpm-workspace.yaml: "${key}" is not on the reviewed settings list; settings such as registries, overrides or policy exclusions can redirect or weaken installs`,
        );
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
  for (const [key, value] of Object.entries(npmrc)) {
    if (!ALLOWED_NPMRC.has(key)) {
      errors.push(`.npmrc: "${key}" is not allowed; only ${[...ALLOWED_NPMRC.keys()].join(', ')} may be set`);
    } else if (!(key in REQUIRED_NPMRC) && ALLOWED_NPMRC.get(key) !== value) {
      errors.push(`.npmrc: "${key}" must be "${ALLOWED_NPMRC.get(key)}"`);
    }
  }

  // --- pnpm-lock.yaml ------------------------------------------------------
  const lockPath = join(webDir, 'pnpm-lock.yaml');
  const hasDeps = Object.keys(pkg.dependencies ?? {}).length + Object.keys(pkg.devDependencies ?? {}).length > 0;
  if (!existsSync(lockPath)) {
    if (hasDeps) errors.push('pnpm-lock.yaml is missing; installs must use a committed lockfile');
  } else {
    const { errors: lockErrors, names } = parseLockfile(readFileSync(lockPath, 'utf8'));
    errors.push(...lockErrors);
    for (const name of names) checkForbidden(name, 'pnpm-lock.yaml', errors);
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
