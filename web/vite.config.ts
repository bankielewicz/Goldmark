import { defineConfig } from 'vite';

// Browser-only SPA build (D1). No React plugin: Vite's built-in transformer
// handles TSX with the automatic JSX runtime, which keeps the dev dependency
// tree small (DEC-012). Output is static files only; ASP.NET Core serves them.
export default defineConfig({
  oxc: {
    jsx: { runtime: 'automatic' },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    // Never inline assets as data: URIs; the CSP only allows 'self' (NFR-SEC-2).
    assetsInlineLimit: 0,
    // Current Edge/Chrome/Firefox support modulepreload natively (NFR-REL-4).
    modulePreload: { polyfill: false },
    sourcemap: false,
  },
});
