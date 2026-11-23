import { useTranslation } from 'react-i18next';
import type { UserSummary } from '../api/contracts';

const palette = 6;

/** Stable colour slot derived from the user id, so a user keeps the same avatar colour. */
function colourSlot(id: string): number {
  let hash = 0;
  for (const char of id) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return hash % palette;
}

/**
 * Generated initials avatar. Uploaded images arrive with the storage module (Phase 7).
 * With `presence`, a status dot is shown and announced to assistive technology.
 */
export function Avatar({
  user,
  size = 'md',
  presence,
}: {
  user: Pick<UserSummary, 'id' | 'avatar'>;
  size?: 'sm' | 'md';
  presence?: 'online' | 'offline' | undefined;
}) {
  const { t } = useTranslation();
  const avatar = (
    <span className={`cx-avatar cx-avatar-${size} cx-avatar-c${colourSlot(user.id)}`} aria-hidden="true">
      {user.avatar.initials || '?'}
    </span>
  );
  if (!presence) return avatar;
  return (
    <span className="cx-avatar-wrap">
      {avatar}
      <span className={`cx-presence cx-presence-${presence}`} aria-hidden="true" />
      <span className="visually-hidden">{t(`realtime.presence.${presence}`)}</span>
    </span>
  );
}
