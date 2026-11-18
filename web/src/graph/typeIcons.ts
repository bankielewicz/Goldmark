/**
 * The Fluent icon each type icon key shows (src/ui/inventory.tsx draws them as React components). The graph draws
 * nodes on Cytoscape's own canvas, so it needs them as image files, and the served CSP has `img-src 'self'`, so they
 * ship in the build as SVG files (DEC-046 item 3): `scripts/graph-icons.mjs` writes `src/graph/icons/<key>-light.svg`
 * and `-dark.svg` from the installed @fluentui/react-icons in each theme's colorNeutralForeground2, and
 * tests/graph-icons.test.mjs fails if they, or this list, drift from the package, the theme or inventory.tsx.
 * No relative imports: Node's type stripping loads this file in that test.
 */
export const graphIconNames: Record<string, string> = {
  location: 'LocationRegular',
  team: 'PeopleTeamRegular',
  person: 'PersonRegular',
  'hypervisor-manager': 'OrganizationRegular',
  'hypervisor-cluster': 'ServerMultipleRegular',
  'hypervisor-host': 'CubeMultipleRegular',
  chassis: 'BoxMultipleRegular',
  'virtual-machine': 'DesktopRegular',
  server: 'ServerRegular',
  appliance: 'BoxRegular',
  'network-device': 'RouterRegular',
  datastore: 'HardDriveRegular',
  'storage-group': 'CubeRegular',
  'storage-array': 'StorageRegular',
  'windows-cluster': 'AppsListRegular',
  'cluster-network-name': 'GlobeRegular',
  'sql-instance': 'DatabaseRegular',
  'sql-availability-group': 'DatabaseMultipleRegular',
  'sql-database': 'TableRegular',
  'business-service': 'BriefcaseRegular',
  generic: 'CubeRegular',
};
