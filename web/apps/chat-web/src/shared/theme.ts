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

/** Called once at start-up: follow the system until a user's choice is known. */
export function initTheme(): void {
  apply();
  media?.addEventListener('change', () => {
    if (preference === 'system') apply();
  });
}
