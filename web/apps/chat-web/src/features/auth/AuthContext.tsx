import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import type { AuthConfigurationResponse, CurrentUserResponse } from '../../shared/api/contracts';
import { isSupportedLanguage, selectLanguage } from '../../i18n/i18n';
import { restoreLocalTheme, setThemePreference } from '../../shared/theme';
import { getAuthConfiguration, getCurrentUser, logout } from './authApi';
import { disablePush } from '../notifications/pushApi';

export interface AuthState {
  /** null when the API could not be reached. */
  config: AuthConfigurationResponse | null;
  user: CurrentUserResponse | null;
  status: 'loading' | 'ready';
  setUser: (user: CurrentUserResponse | null) => void;
  refresh: () => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

/** Loads sign-in configuration and the current user; applies the user's saved UI language. */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [config, setConfig] = useState<AuthConfigurationResponse | null>(null);
  const [user, setUserState] = useState<CurrentUserResponse | null>(null);
  const [status, setStatus] = useState<'loading' | 'ready'>('loading');

  const setUser = useCallback((next: CurrentUserResponse | null) => {
    setUserState(next);
    const language = next?.preferences.uiLanguage;
    if (isSupportedLanguage(language)) void selectLanguage(language);
    if (next) setThemePreference(next.preferences.theme);
    else restoreLocalTheme();
  }, []);

  const refresh = useCallback(async () => {
    const [configResult, userResult] = await Promise.allSettled([getAuthConfiguration(), getCurrentUser()]);
    setConfig(configResult.status === 'fulfilled' ? configResult.value : null);
    setUser(userResult.status === 'fulfilled' ? userResult.value : null);
    setStatus('ready');
  }, [setUser]);

  const signOut = useCallback(async () => {
    try {
      // This browser stops receiving the person's push notifications (Phase 15).
      await disablePush();
      await logout();
    } finally {
      setUserState(null);
      restoreLocalTheme();
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const value = useMemo<AuthState>(
    () => ({ config, user, status, setUser, refresh, signOut }),
    [config, user, status, setUser, refresh, signOut],
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

/** The auth state, or null outside <AuthProvider> (the setup wizard runs without one). */
export function useOptionalAuth(): AuthState | null {
  return useContext(AuthContext);
}

export function useAuth(): AuthState {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used inside <AuthProvider>.');
  return value;
}
