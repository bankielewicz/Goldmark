// Tests for the dependency policy check. Run: node --test scripts/
import assert from 'node:assert/strict';
import { cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, beforeEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  checkPolicy,
  lockfileStructureErrors,
  nameFromLockKey,
  parseWorkspaceYaml,
  readLockfile,
} from './check-dependency-policy.mjs';

const webDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const scratch = [];

/** Copies the real policy files into a temp dir so each test can mutate them. */
function fixture() {
  const dir = mkdtempSync(join(tmpdir(), 'goldmark-policy-'));
  scratch.push(dir);
  for (const file of ['package.json', 'dependency-allowlist.json', 'pnpm-workspace.yaml', '.npmrc', 'pnpm-lock.yaml']) {
    try {
      cpSync(join(webDir, file), join(dir, file));
    } catch {
      // pnpm-lock.yaml may not exist before the first install.
    }
  }
  return dir;
}

function editJson(dir, file, mutate) {
  const path = join(dir, file);
  const data = JSON.parse(readFileSync(path, 'utf8'));
  mutate(data);
  writeFileSync(path, JSON.stringify(data, null, 2));
}

function editText(dir, file, mutate) {
  const path = join(dir, file);
  writeFileSync(path, mutate(readFileSync(path, 'utf8')));
}

after(() => {
  for (const dir of scratch) rmSync(dir, { recursive: true, force: true });
});

describe('repository policy files', () => {
  it('pass the policy check as committed', () => {
    assert.deepEqual(checkPolicy(webDir), []);
  });
});

describe('direct dependencies', () => {
  let dir;
  beforeEach(() => {
    dir = fixture();
  });

  it('fails when a runtime dependency is not allow-listed', () => {
    editJson(dir, 'package.json', (p) => {
      p.dependencies = { ...p.dependencies, 'left-pad': '1.3.0' };
    });
    const errors = checkPolicy(dir);
    assert.ok(
      errors.some((e) => e.includes('"left-pad" is not in dependency-allowlist.json')),
      errors.join('\n'),
    );
  });

  it('fails when a dev dependency is not allow-listed', () => {
    editJson(dir, 'package.json', (p) => {
      p.devDependencies = { ...p.devDependencies, 'is-odd': '3.0.1' };
    });
    assert.ok(checkPolicy(dir).some((e) => e.includes('"is-odd" is not in dependency-allowlist.json')));
  });

  it('fails when a dev-only tool is added as a runtime dependency', () => {
    editJson(dir, 'package.json', (p) => {
      p.dependencies = { ...p.dependencies, vite: '1.0.0' };
    });
    assert.ok(checkPolicy(dir).some((e) => e.includes('"vite" is allow-listed as dev, not runtime')));
  });

  it('fails on version ranges and non-registry specs', () => {
    editJson(dir, 'package.json', (p) => {
      p.dependencies = { ...p.dependencies, react: '^19.0.0', 'react-dom': 'github:facebook/react' };
    });
    const errors = checkPolicy(dir);
    assert.ok(errors.some((e) => e.includes('"react" must be pinned')));
    assert.ok(errors.some((e) => e.includes('"react-dom" must be pinned')));
  });

  it('fails on banned SSR/RSC packages even if someone allow-lists them', () => {
    editJson(dir, 'dependency-allowlist.json', (a) => {
      a.runtime.next = { justification: 'trying to sneak SSR in' };
    });
    editJson(dir, 'package.json', (p) => {
      p.dependencies = { ...p.dependencies, next: '15.0.0' };
    });
    assert.ok(checkPolicy(dir).some((e) => e.includes('banned package "next"')));
  });

  it('fails on install lifecycle scripts', () => {
    editJson(dir, 'package.json', (p) => {
      p.scripts = { ...p.scripts, postinstall: 'node evil.js' };
    });
    assert.ok(checkPolicy(dir).some((e) => e.includes('lifecycle script "postinstall"')));
  });

  it('fails when the pnpm version is not pinned with a hash', () => {
    editJson(dir, 'package.json', (p) => {
      p.packageManager = 'pnpm@11.28.5';
    });
    assert.ok(checkPolicy(dir).some((e) => e.includes('"packageManager" must pin pnpm')));
  });
});

describe('pnpm and npm settings', () => {
  let dir;
  beforeEach(() => {
    dir = fixture();
  });

  for (const key of ['minimumReleaseAge', 'ignoreScripts', 'strictDepBuilds', 'trustPolicy', 'blockExoticSubdeps']) {
    it(`fails when ${key} is removed`, () => {
      editText(dir, 'pnpm-workspace.yaml', (t) =>
        t
          .split('\n')
          .filter((l) => !l.startsWith(`${key}:`))
          .join('\n'),
      );
      assert.ok(checkPolicy(dir).some((e) => e.includes(`"${key}" must be`)));
    });
  }

  it('fails when the release-age delay is shortened', () => {
    editText(dir, 'pnpm-workspace.yaml', (t) => t.replace('minimumReleaseAge: 10080', 'minimumReleaseAge: 60'));
    assert.ok(checkPolicy(dir).some((e) => e.includes('"minimumReleaseAge" must be >= 10080')));
  });

  it('fails when a policy exclusion is added', () => {
    editText(dir, 'pnpm-workspace.yaml', (t) => `${t}\nminimumReleaseAgeExclude:\n  react: 'true'\n`);
    assert.ok(
      checkPolicy(dir).some((e) => e.includes('"minimumReleaseAgeExclude" is not on the reviewed settings list')),
    );
  });

  it('fails when pnpm-workspace.yaml adds registries or overrides', () => {
    editText(
      dir,
      'pnpm-workspace.yaml',
      (t) => `${t}\nregistries:\n  '@playwright': https://attacker.example/\noverrides:\n  react: 19.0.0\n`,
    );
    const errors = checkPolicy(dir);
    assert.ok(errors.some((e) => e.includes('"registries" is not on the reviewed settings list')));
    assert.ok(errors.some((e) => e.includes('"overrides" is not on the reviewed settings list')));
  });

  it('fails on a scoped registry override in .npmrc', () => {
    editText(dir, '.npmrc', (t) => `${t}@playwright:registry=https://attacker.example/\n`);
    assert.ok(checkPolicy(dir).some((e) => e.includes('.npmrc: "@playwright:registry" is not allowed')));
  });

  it('fails on auth tokens, custom CAs or disabled TLS checks in .npmrc', () => {
    editText(dir, '.npmrc', (t) => `${t}//registry.npmjs.org/:_authToken=abc\nstrict-ssl=false\ncafile=/tmp/ca.pem\n`);
    const errors = checkPolicy(dir);
    for (const key of ['//registry.npmjs.org/:_authToken', 'strict-ssl', 'cafile']) {
      assert.ok(
        errors.some((e) => e.includes(`.npmrc: "${key}" is not allowed`)),
        key,
      );
    }
  });

  it('fails when engine-strict is turned off in .npmrc', () => {
    editText(dir, '.npmrc', (t) => t.replace('engine-strict=true', 'engine-strict=false'));
    assert.ok(checkPolicy(dir).some((e) => e.includes('.npmrc: "engine-strict" must be "true"')));
  });

  it('fails when a build script is approved without an allow-list entry', () => {
    editText(dir, 'pnpm-workspace.yaml', (t) => t.replace('allowBuilds: {}', 'allowBuilds:\n  esbuild: true'));
    assert.ok(checkPolicy(dir).some((e) => e.includes('build scripts for "esbuild" are allowed but not approved')));
  });

  it('accepts an explicitly denied build script', () => {
    editText(dir, 'pnpm-workspace.yaml', (t) => t.replace('allowBuilds: {}', 'allowBuilds:\n  esbuild: false'));
    assert.deepEqual(checkPolicy(dir), []);
  });

  it('fails when .npmrc stops ignoring scripts', () => {
    editText(dir, '.npmrc', (t) => t.replace('ignore-scripts=true', 'ignore-scripts=false'));
    assert.ok(checkPolicy(dir).some((e) => e.includes('.npmrc: "ignore-scripts"')));
  });
});

describe('lockfile', () => {
  const lock = [
    "lockfileVersion: '9.0'",
    '',
    'packages:',
    '',
    "  '@remix-run/node@2.0.0':",
    '    resolution: {integrity: sha512-abc}',
    '',
    '  react-server-dom-webpack@19.0.0:',
    '    resolution: {integrity: sha512-abc}',
    '',
    '  evil@1.0.0:',
    '    resolution: {tarball: https://example.invalid/evil.tgz}',
    '',
    'snapshots:',
    '',
    '  next@15.0.0(react@19.0.0):',
    '    dependencies:',
    '      react: 19.0.0',
    '',
  ].join('\n');

  it('extracts scoped and unscoped package names', () => {
    assert.equal(nameFromLockKey('@types/react@19.0.0'), '@types/react');
    assert.equal(nameFromLockKey('react-dom@19.0.0(react@19.0.0)'), 'react-dom');
    const { names } = readLockfile(lock);
    assert.ok(names.has('@remix-run/node'));
    assert.ok(names.has('react-server-dom-webpack'));
    assert.ok(names.has('next'));
  });

  it('accepts the committed lockfile as canonical', () => {
    assert.deepEqual(lockfileStructureErrors(readFileSync(join(webDir, 'pnpm-lock.yaml'), 'utf8')), []);
  });

  for (const [name, mutate, expected] of [
    [
      'quoted section keys',
      (t) => t.replace('\npackages:', "\n'packages':").replace('\nsnapshots:', "\n'snapshots':"),
      'unexpected top-level entry',
    ],
    [
      're-indented entries',
      (t) => t.replace("\n  '@cacheable/memory@2.2.0':", "\n   '@cacheable/memory@2.2.0':"),
      'odd indentation',
    ],
    ['an extra top-level key', (t) => `${t}\noverrides:\n  react: 19.0.0\n`, 'unexpected top-level entry'],
    [
      'anchors and aliases',
      (t) => t.replace('autoInstallPeers: true', 'autoInstallPeers: &x true'),
      'anchors, aliases',
    ],
    ['a second YAML document', (t) => `${t}\n---\npackages: {}\n`, 'multiple YAML documents'],
    ['tabs', (t) => t.replace('  autoInstallPeers', '\tautoInstallPeers'), 'tabs'],
    [
      'a flow-style packages section',
      (t) => t.replace('\npackages:\n', '\npackages: {next@15.0.0: {}}\n'),
      'unexpected value for "packages"',
    ],
    [
      'a quoted resolution key pointing at a tarball',
      (t) =>
        t.replace(
          /\n {4}resolution: \{integrity: (sha512-[^}]+)\}/,
          "\n    'resolution': {tarball: https://attacker.example/pkg.tgz, integrity: $1}",
        ),
      'quoted key "resolution" is not canonical',
    ],
    [
      'a double-quoted, escaped package key',
      (t) => t.replace("\n  '@cacheable/memory@2.2.0':", '\n  "\\x6eext@15.0.0":'),
      'double-quoted or escaped keys',
    ],
    [
      'a package without its canonical resolution',
      (t) =>
        t.replace(
          /\n {4}resolution: \{integrity: sha512-[^}]+\}/,
          '\n    resolution: {integrity: sha512-x, tarball: https://attacker.example/x.tgz}',
        ),
      'non-canonical resolution',
    ],
  ]) {
    it(`fails on ${name} in the lockfile`, () => {
      const dir = fixture();
      editText(dir, 'pnpm-lock.yaml', mutate);
      assert.ok(
        checkPolicy(dir).some((e) => e.includes(expected)),
        checkPolicy(dir).join('\n'),
      );
    });
  }

  it('fails on banned transitive packages and non-registry resolutions', () => {
    const dir = fixture();
    writeFileSync(join(dir, 'pnpm-lock.yaml'), lock);
    const errors = checkPolicy(dir);
    assert.ok(errors.some((e) => e.includes('pnpm-lock.yaml: banned package "next"')));
    assert.ok(errors.some((e) => e.includes('banned package "react-server-dom-webpack"')));
    assert.ok(errors.some((e) => e.includes('banned package "@remix-run/node"')));
    assert.ok(errors.some((e) => e.includes('non-registry resolution is not allowed: evil@1.0.0')));
  });
});

describe('workspace yaml reader', () => {
  it('reads scalars, block maps and empty maps', () => {
    const { settings, errors } = parseWorkspaceYaml(
      "# c\nminimumReleaseAge: 10080 # seven days\nallowBuilds:\n  esbuild: false\nsavePrefix: ''\nempty: {}\n",
    );
    assert.deepEqual(errors, []);
    assert.equal(settings.minimumReleaseAge, '10080');
    assert.deepEqual(settings.allowBuilds, { esbuild: 'false' });
    assert.equal(settings.savePrefix, '');
    assert.deepEqual(settings.empty, {});
  });
});
