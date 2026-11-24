import { useTranslation } from 'react-i18next';
import { formatDuration, formatTime } from '../../shared/format';
import { useReadiness } from './useReadiness';
import type { ReadinessResult } from './readinessApi';

const presentation: Record<ReadinessResult['state'], { label: string; detail: string; tone: string }> = {
  available: { label: 'readiness.available', detail: 'readiness.availableDetail', tone: 'ok' },
  notReady: { label: 'readiness.notReady', detail: 'readiness.notReadyDetail', tone: 'warn' },
  unreachable: { label: 'readiness.unreachable', detail: 'readiness.unreachableDetail', tone: 'down' },
};

/** `compact`: one row (the signed-out landing page, redesign 2026-11). */
export function ReadinessIndicator({ compact = false }: { compact?: boolean }) {
  const { t, i18n } = useTranslation();
  const { readiness, refresh } = useReadiness();
  const language = i18n.resolvedLanguage ?? i18n.language;

  const isChecking = readiness.state === 'loading';
  // While re-checking, keep showing the last known result instead of flashing a spinner.
  const shown = readiness.state === 'loading' ? readiness.previous : readiness;
  const view = shown ? presentation[shown.state] : undefined;

  if (compact) {
    return (
      <section className="cx-card cx-status-bar" aria-labelledby="readiness-title">
        <h2 id="readiness-title" className="cx-status-bar-title">{t('readiness.title')}</h2>
        <span className="cx-status-bar-divider" aria-hidden="true" />
        <p id="readiness-status" className="cx-status-bar-state" role="status" aria-live="polite" aria-busy={isChecking}>
          {view && shown ? (
            <>
              <span className={`cx-dot cx-dot-${view.tone}`} aria-hidden="true" />
              <strong>{t(view.label)}</strong>
              <span className="cx-muted">
                {t('readiness.checkedAt', { time: formatTime(shown.checkedAt, language) })}
                {'durationMs' in shown && <> · {formatDuration(shown.durationMs, language)}</>}
              </span>
            </>
          ) : (
            <span className="cx-muted">{t('readiness.loading')}</span>
          )}
        </p>
        <button type="button" className="btn btn-sm btn-outline-primary" onClick={refresh} disabled={isChecking} aria-describedby="readiness-status">
          {isChecking ? t('readiness.checking') : t('readiness.retry')}
        </button>
      </section>
    );
  }

  return (
    <section className="cx-card cx-status" aria-labelledby="readiness-title">
      <div className="cx-card-header">
        <h2 id="readiness-title" className="cx-card-title">
          {t('readiness.title')}
        </h2>
        <button
          type="button"
          className="btn btn-sm btn-outline-primary"
          onClick={refresh}
          disabled={isChecking}
          aria-describedby="readiness-status"
        >
          {isChecking ? t('readiness.checking') : t('readiness.retry')}
        </button>
      </div>

      <div id="readiness-status" className="cx-status-body" role="status" aria-live="polite" aria-busy={isChecking}>
        {view && shown ? (
          <>
            <p className="cx-status-line">
              <span className={`cx-dot cx-dot-${view.tone}`} aria-hidden="true" />
              <strong>{t(view.label)}</strong>
            </p>
            <p className="cx-muted mb-2">{t(view.detail)}</p>
            <p className="cx-meta mb-0">
              {t('readiness.checkedAt', { time: formatTime(shown.checkedAt, language) })}
              {'durationMs' in shown && (
                <>
                  <span aria-hidden="true"> · </span>
                  {t('readiness.responseTime', { duration: formatDuration(shown.durationMs, language) })}
                </>
              )}
            </p>
          </>
        ) : (
          <p className="cx-status-line mb-0">
            <span className="spinner-border spinner-border-sm text-primary" aria-hidden="true" />
            <span>{t('readiness.loading')}</span>
          </p>
        )}
      </div>
    </section>
  );
}
