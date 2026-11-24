import type { ThemePreference } from './api/contracts';

/**
 * Light/dark theme (Phase 8): the signed-in user's saved choice, or the operating system's
 * colour scheme for "system" and for signed-out pages. Uses Bootstrap's data-bs-theme switch.
 */

const media = typeof window !== 'undefined' && window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
let preference: ThemePreference = 'system';

function resolve(): 'light' | 'dark' {
  if (preference !== 'system') return preference;
  return media?.matches ? 'dark' : 'light';
}

function apply(): void {
  document.documentElement.setAttribute('data-bs-theme', resolve());
}

export function isThemePreference(value: unknown): value is ThemePreference {
  return value === 'system' || value === 'light' || value === 'dark';
}

/** Applies a preference now and remembers it while the page is open. */
export function setThemePreference(value: ThemePreference | undefined | null): void {
  preference = isThemePreference(value) ? value : 'system';
  apply();
}

export function currentThemePreference(): ThemePreference {
  return preference;
}

const storageKey = 'chatx.theme';

/** The theme as it shows now (a "system" preference resolved). */
export function currentResolvedTheme(): 'light' | 'dark' {
  return resolve();
}

/**
 * Signed-out pages (redesign 2026-11): a light/dark choice kept in this browser only. Signed-in
 * users' account preference replaces it while they are signed in.
 */
export function chooseLocalTheme(value: 'light' | 'dark'): void {
  try {
    window.localStorage.setItem(storageKey, value);
  } catch {
    // Storage can be unavailable (private mode, blocked site data); the choice still applies now.
  }
  setThemePreference(value);
}

function storedTheme(): ThemePreference | null {
  try {
    const value = window.localStorage.getItem(storageKey);
    return isThemePreference(value) ? value : null;
  } catch {
    return null;
  }
}

/** Back to this browser's signed-out choice (or the system). */
export function restoreLocalTheme(): void {
  setThemePreference(storedTheme() ?? 'system');
}

/** Called once at start-up: this browser's choice, else the system, until a user's choice is known. */
export function initTheme(): void {
  preference = storedTheme() ?? 'system';
  apply();
  media?.addEventListener('change', () => {
    if (preference === 'system') apply();
  });
}
