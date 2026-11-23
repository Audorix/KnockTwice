import { useTranslation } from 'react-i18next';
import type { ChannelVisibility } from '../../shared/api/contracts';

/** The "#" (or private) sign in front of a channel name. Own module so the chat need not load the channels page. */
export function ChannelMark({ visibility }: { visibility: ChannelVisibility | null | undefined }) {
  const { t } = useTranslation();
  // Not the padlock: that sign is reserved for private chats that are never saved (Phase 7).
  return visibility === 'private' ? (
    <span className="cx-channel-mark" title={t('channels.visibility.private')} aria-hidden="true">
      <svg viewBox="0 0 24 24" width="14" height="14" focusable="false">
        <path d="M3 12s3.5-6 9-6 9 6 9 6-3.5 6-9 6-9-6-9-6z" fill="none" stroke="currentColor" strokeWidth="1.8" />
        <path d="M4 20 20 4" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
      </svg>
    </span>
  ) : (
    <span className="cx-channel-mark" aria-hidden="true">#</span>
  );
}
