import { FluentProvider, RendererProvider } from '@fluentui/react-components';
import { StrictMode, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';

import { App } from './App';
import { PreferencesProvider, usePreferences } from './preferences';
import { SessionProvider } from './session';
import { createStyleRenderer } from './styleNonce';
import { darkTheme, lightTheme } from './theme';
import './app.css';

const container = document.getElementById('root');
if (!container) {
  throw new Error('Root element #root is missing from index.html');
}

/** Fluent's theme follows the viewer's choice; Compact density is Fluent's small size (DEC-039). */
function Themed({ children }: { children: ReactNode }) {
  const { theme } = usePreferences();
  return (
    <FluentProvider theme={theme === 'dark' ? darkTheme : lightTheme} applyStylesToPortals>
      {children}
    </FluentProvider>
  );
}

// Every <style> Fluent creates carries this page's nonce (DEC-037).
createRoot(container).render(
  <StrictMode>
    <RendererProvider renderer={createStyleRenderer()} targetDocument={document}>
      <PreferencesProvider>
        <Themed>
          <SessionProvider>
            <App />
          </SessionProvider>
        </Themed>
      </PreferencesProvider>
    </RendererProvider>
  </StrictMode>,
);
