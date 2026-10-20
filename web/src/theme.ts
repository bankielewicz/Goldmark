import { createDarkTheme, createLightTheme, type Theme } from '@fluentui/react-components';

import { goldmarkBrand } from './brand';

export const lightTheme: Theme = createLightTheme(goldmarkBrand);
export const darkTheme: Theme = createDarkTheme(goldmarkBrand);

export type ThemeChoice = 'light' | 'dark';

/** The viewer's preferences: kept in this browser only, never on the server. */
export interface Preferences {
  /** An explicit choice; null follows the system setting. */
  theme: ThemeChoice | null;
  compact: boolean;
}

const storageKey = 'goldmark:preferences';
export const darkQuery = '(prefers-color-scheme: dark)';

export function systemTheme(): ThemeChoice {
  try {
    return window.matchMedia(darkQuery).matches ? 'dark' : 'light';
  } catch {
    return 'light';
  }
}

export function loadPreferences(): Preferences {
  try {
    const stored = JSON.parse(window.localStorage.getItem(storageKey) ?? 'null') as Partial<Preferences> | null;
    return {
      theme: stored?.theme === 'dark' || stored?.theme === 'light' ? stored.theme : null,
      compact: stored?.compact === true,
    };
  } catch {
    return { theme: null, compact: false };
  }
}

export function savePreferences(preferences: Preferences): void {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(preferences));
  } catch {
    // Private windows and blocked storage: the preference lasts for this page only.
  }
}
