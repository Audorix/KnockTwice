import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiUrl } from '../api/httpClient';
import type { UserSummary } from '../api/contracts';

const palette = 6;

/** Stable colour slot derived from the user id, so a user keeps the same avatar colour. */
function colourSlot(id: string): number {
  let hash = 0;
  for (const char of id) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return hash % palette;
}

/**
 * The person's profile photo when they have one (redesign 2026-11), otherwise generated initials.
 * A photo that fails to load falls back to the initials. With `presence`, a status dot is shown and
 * announced to assistive technology.
 */
export function Avatar({
  user,
  size = 'md',
  presence,
}: {
  user: Pick<UserSummary, 'id' | 'avatar'>;
  size?: 'sm' | 'md' | 'lg' | 'xl';
  presence?: 'online' | 'offline' | undefined;
}) {
  const { t } = useTranslation();
  const [failed, setFailed] = useState<string | null>(null);
  const photo = user.avatar.photoUrl && failed !== user.avatar.photoUrl ? user.avatar.photoUrl : null;
  const avatar = (
    <span className={`cx-avatar cx-avatar-${size} cx-avatar-c${colourSlot(user.id)}${photo ? ' has-photo' : ''}`} aria-hidden="true">
      {photo ? (
        <img src={apiUrl(photo)} alt="" loading="lazy" decoding="async" onError={() => setFailed(photo)} />
      ) : (
        user.avatar.initials || '?'
      )}
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
