// Tests for the post-install tree check. Run: node --test "scripts/*.test.mjs"
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { checkInstalledTree, expectedTarball } from './check-installed-tree.mjs';

const pkg = (name, version, extra = {}) => ({
  from: name,
  version,
  resolved: expectedTarball(name, version),
  ...extra,
});

describe('installed tree check', () => {
  it('derives registry tarball URLs for plain and scoped packages', () => {
    assert.equal(expectedTarball('react', '19.3.0'), 'https://registry.npmjs.org/react/-/react-19.3.0.tgz');
    assert.equal(
      expectedTarball('@types/react', '19.3.0'),
      'https://registry.npmjs.org/@types/react/-/react-19.3.0.tgz',
    );
  });

  it('accepts a tree resolved entirely from the registry', () => {
    const tree = [
      {
        dependencies: { react: pkg('react', '19.3.0') },
        devDependencies: { vite: pkg('vite', '8.3.4', { dependencies: { rolldown: pkg('rolldown', '1.2.12') } }) },
      },
    ];
    const { errors, count } = checkInstalledTree(tree);
    assert.deepEqual(errors, []);
    assert.equal(count, 3);
  });

  it('fails on a tarball from another host, even with a valid-looking integrity', () => {
    const tree = [
      { devDependencies: { vite: { ...pkg('vite', '8.3.4'), resolved: 'https://attacker.example/vite-8.3.4.tgz' } } },
    ];
    assert.ok(checkInstalledTree(tree).errors.some((e) => e.includes('attacker.example')));
  });

  it("fails on another package's tarball served under an allow-listed name", () => {
    const tree = [
      { dependencies: { react: { ...pkg('react', '19.3.0'), resolved: expectedTarball('evil', '1.0.0') } } },
    ];
    assert.ok(checkInstalledTree(tree).errors.some((e) => e.includes('not the registry tarball')));
  });

  it('fails on banned packages anywhere in the tree, including behind an alias', () => {
    const tree = [
      {
        devDependencies: {
          vite: pkg('vite', '8.3.4', { dependencies: { 'innocent-name': pkg('next', '15.0.0') } }),
        },
      },
    ];
    assert.ok(checkInstalledTree(tree).errors.some((e) => e.includes('banned package "next"')));
  });

  it('fails on packages without a registry resolution (git, file, link)', () => {
    const tree = [{ dependencies: { local: { from: 'local', version: 'file:../local' } } }];
    assert.ok(checkInstalledTree(tree).errors.some((e) => e.includes('no registry resolution')));
  });

  it('checks children that only appear under a later occurrence of a package', () => {
    const shallow = pkg('vite', '8.3.4');
    const deep = pkg('vite', '8.3.4', { dependencies: { 'innocent-name': pkg('next', '15.0.0') } });
    const tree = [
      { devDependencies: { vite: shallow, eslint: pkg('eslint', '10.12.0', { dependencies: { vite: deep } }) } },
    ];
    assert.ok(checkInstalledTree(tree).errors.some((e) => e.includes('banned package "next"')));
  });

  it('fails when nothing is installed', () => {
    assert.ok(checkInstalledTree([{}]).errors.some((e) => e.includes('no installed packages')));
  });
});
