import { useEffect, useState, type ReactNode } from 'react';
import { getSetupStatus } from './setupApi';
import { SetupWizard } from './SetupWizard';

/**
 * Shows the setup wizard while the API runs in setup mode, the application otherwise. If the
 * status cannot be read (API down), the application renders and reports the outage itself.
 */
export function SetupGate({ children }: { children: ReactNode }) {
  const [state, setState] = useState<'checking' | 'setup' | 'app'>('checking');

  useEffect(() => {
    const controller = new AbortController();
    getSetupStatus(controller.signal).then(
      (status) => setState(status.required ? 'setup' : 'app'),
      () => {
        if (!controller.signal.aborted) setState('app');
      },
    );
    return () => controller.abort();
  }, []);

  if (state === 'checking') return null;
  return state === 'setup' ? <SetupWizard /> : <>{children}</>;
}
