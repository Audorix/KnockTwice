import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { StyleDefinition } from '@dicebear/core/lite';
import { apiUrl } from '../api/httpClient';
import type { UserSummary } from '../api/contracts';

const palette = 6;
const styleLoaders: Record<string, () => Promise<{ default: unknown }>> = {
  identicon: () => import('@dicebear/styles/identicon.json'),
  lorelei: () => import('@dicebear/styles/lorelei.json'),
  'open-peeps': () => import('@dicebear/styles/open-peeps.json'),
  'pixel-art': () => import('@dicebear/styles/pixel-art.json'),
  shapes: () => import('@dicebear/styles/shapes.json'),
  thumbs: () => import('@dicebear/styles/thumbs.json'),
};
const generatedCache = new Map<string, string>();

async function renderGeneratedAvatar(style: string, seed: string): Promise<string> {
  const key = `${style}:${seed}`;
  const cached = generatedCache.get(key);
  if (cached) return cached;
  const loader = styleLoaders[style];
  if (!loader) throw new Error('avatar_style_invalid');
  const [{ Avatar: DiceBearAvatar, Style }, definition] = await Promise.all([import('@dicebear/core/lite'), loader()]);
  const uri = new DiceBearAvatar(new Style<unknown>(definition.default as StyleDefinition), { seed, size: 128 }).toDataUri();
  if (generatedCache.size >= 500) generatedCache.delete(generatedCache.keys().next().value!);
  generatedCache.set(key, uri);
  return uri;
}

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
  const [generated, setGenerated] = useState<string | null>(null);
  const photo = user.avatar.photoUrl && failed !== user.avatar.photoUrl ? user.avatar.photoUrl : null;
  useEffect(() => {
    let active = true;
    setGenerated(null);
    const style = user.avatar.generatedStyle;
    const seed = user.avatar.generatedSeed;
    if (!photo && style && seed) {
      void renderGeneratedAvatar(style, seed).then(
        (uri) => { if (active) setGenerated(uri); },
        () => { if (active) setGenerated(null); },
      );
    }
    return () => { active = false; };
  }, [photo, user.avatar.generatedStyle, user.avatar.generatedSeed]);
  const image = photo ? apiUrl(photo) : generated;
  const avatar = (
    <span className={`cx-avatar cx-avatar-${size} cx-avatar-c${colourSlot(user.id)}${image ? ' has-photo' : ''}`} aria-hidden="true">
      {image ? (
        <img src={image} alt="" loading="lazy" decoding="async" onError={photo ? () => setFailed(photo) : undefined} />
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
