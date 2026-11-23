import { useEffect, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { navigate, useLocation } from '../../app/router';
import { useAuth } from './AuthContext';

/** Renders children only for signed-in users; otherwise sends the visitor to sign-in and back. */
export function RequireUser({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const { user, status } = useAuth();
  const { pathname, query } = useLocation();

  useEffect(() => {
    if (status === 'ready' && !user) {
      const current = pathname + (query.size ? `?${query.toString()}` : '');
      navigate(`/sign-in?returnUrl=${encodeURIComponent(current)}`, { replace: true });
    }
  }, [status, user, pathname, query]);

  if (!user) {
    return (
      <p className="cx-status-line" role="status">
        <span className="spinner-border spinner-border-sm text-primary" aria-hidden="true" /> {t('auth.working')}
      </p>
    );
  }

  return <>{children}</>;
}

/** Runs an async action, reporting failures and refreshing data afterwards. */
export function useAction(onError: (error: unknown) => void, reload: () => void) {
  return async (action: () => Promise<unknown>) => {
    onError(null);
    try {
      await action();
    } catch (error) {
      onError(error);
    }
    reload();
  };
}
