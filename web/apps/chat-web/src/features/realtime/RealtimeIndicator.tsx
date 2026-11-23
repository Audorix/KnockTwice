import { useTranslation } from 'react-i18next';
import { useRealtime } from './RealtimeContext';

/** Small live-connection badge for the top navigation; announced politely when it changes. */
export function RealtimeIndicator() {
  const { t } = useTranslation();
  const { status } = useRealtime();
  const tone = status === 'connected' ? 'ok' : status === 'disconnected' ? 'down' : 'warn';
  return (
    <span className={`cx-live cx-live-${tone}`} role="status" aria-live="polite" title={t(`realtime.status.${status}Hint`)}>
      <span className={`cx-dot cx-dot-${tone}`} aria-hidden="true" />
      {t(`realtime.status.${status}`)}
    </span>
  );
}
