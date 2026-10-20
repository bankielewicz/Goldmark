import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

import { darkQuery, loadPreferences, savePreferences, systemTheme, type Preferences, type ThemeChoice } from './theme';

interface PreferencesValue {
  /** The theme in effect: the viewer's choice, or the system's. */
  theme: ThemeChoice;
  compact: boolean;
  setTheme: (theme: ThemeChoice) => void;
  setCompact: (compact: boolean) => void;
}

const PreferencesContext = createContext<PreferencesValue | null>(null);

export function usePreferences(): PreferencesValue {
  const value = useContext(PreferencesContext);
  if (!value) {
    throw new Error('usePreferences outside PreferencesProvider');
  }
  return value;
}

/**
 * Theme (the system's until the viewer chooses one, then theirs) and Compact density (Fluent size
 * small). Stored only once the viewer changes one of them.
 */
export function PreferencesProvider({ children }: { children: ReactNode }) {
  const [preferences, setPreferences] = useState<Preferences>(loadPreferences);
  const [system, setSystem] = useState<ThemeChoice>(systemTheme);
  const theme = preferences.theme ?? system;

  useEffect(() => {
    const query = window.matchMedia(darkQuery);
    const listener = () => setSystem(query.matches ? 'dark' : 'light');
    query.addEventListener('change', listener);
    return () => query.removeEventListener('change', listener);
  }, []);

  useEffect(() => {
    document.documentElement.style.colorScheme = theme;
  }, [theme]);

  const value = useMemo<PreferencesValue>(() => {
    const update = (next: Preferences) => {
      savePreferences(next);
      setPreferences(next);
    };
    return {
      theme,
      compact: preferences.compact,
      setTheme: (choice) => update({ ...preferences, theme: choice }),
      setCompact: (compact) => update({ ...preferences, compact }),
    };
  }, [theme, preferences]);

  return <PreferencesContext.Provider value={value}>{children}</PreferencesContext.Provider>;
}
