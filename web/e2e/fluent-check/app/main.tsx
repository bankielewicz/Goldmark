// Fluent UI v9 sampler for the CSP / Trusted Types check (DEC-017, DEC-037).
// It is a test fixture, never part of the product build: e2e/fluent-check/build.ts builds it
// with the production Vite settings. It covers representative ways Fluent and Griffel put
// styles on the page, and selfcheck.tsx reports PASS or FAIL in the page itself.
import './violations';

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RendererProvider } from '@fluentui/react-components';

import { createStyleRenderer } from '../../../src/styleNonce';
import { SamplerApp } from './sampler';

const container = document.getElementById('root');
if (!container) {
  throw new Error('Root element #root is missing');
}

createRoot(container).render(
  <StrictMode>
    <RendererProvider renderer={createStyleRenderer()} targetDocument={document}>
      <SamplerApp />
    </RendererProvider>
  </StrictMode>,
);
