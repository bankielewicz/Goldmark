// The names Cytoscape.js (3.34.4) uses for its one stylesheet and its container class, and the rule
// that stylesheet holds. Kept apart, with no imports, so tests/cytoscape-stylesheet.test.mjs can load
// them and compare them with the installed bundle.
export const CYTOSCAPE_STYLESHEET_ID = '__________cytoscape_stylesheet';
export const CYTOSCAPE_CONTAINER_CLASS = '__________cytoscape_container';
export const CYTOSCAPE_CONTAINER_RULE = `.${CYTOSCAPE_CONTAINER_CLASS} { position: relative; }`;
