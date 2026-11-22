import { useCallback, useEffect, useRef, useState } from 'react';
import { currentLanguage } from '../../i18n/i18n';
import { checkReadiness, type ReadinessResult } from './readinessApi';

export type ReadinessState = { state: 'loading'; previous?: ReadinessResult } | ReadinessResult;

const refreshIntervalMs = 30_000;

/**
 * Polls API readiness while mounted. Each check cancels the previous one, and unmounting
 * aborts any request in flight.
 */
export function useReadiness(): { readiness: ReadinessState; refresh: () => void } {
  const [readiness, setReadiness] = useState<ReadinessState>({ state: 'loading' });
  const controller = useRef<AbortController | null>(null);

  const refresh = useCallback(() => {
    controller.current?.abort();
    const current = new AbortController();
    controller.current = current;

    setReadiness((prev) => (prev.state === 'loading' ? prev : { state: 'loading', previous: prev }));
    checkReadiness(current.signal, currentLanguage())
      .then((result) => {
        if (!current.signal.aborted) setReadiness(result);
      })
      .catch(() => {
        // Only an aborted request rejects; a newer check or unmount owns the state.
      });
  }, []);

  useEffect(() => {
    refresh();
    const timer = window.setInterval(refresh, refreshIntervalMs);
    return () => {
      window.clearInterval(timer);
      controller.current?.abort();
    };
  }, [refresh]);

  return { readiness, refresh };
}
