import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { apiUrl } from '../../shared/api/httpClient';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { trapTab } from '../../shared/components/ConfirmDialog';
import { NavIcon } from '../../shared/components/NavIcon';
import { useAuth } from '../auth/AuthContext';
import * as api from '../auth/authApi';
import { errorMessage } from '../auth/errors';
import { ImageCropDialog } from '../media/ImageCropDialog';

const choices = ['identicon', 'lorelei', 'open-peeps', 'pixel-art', 'shapes', 'thumbs'] as const;
type StyleChoice = (typeof choices)[number];

/** Photo and locally generated avatar choices share one profile dialog. */
export function ProfilePhotoDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const { user, setUser } = useAuth();
  const [mode, setMode] = useState<'photo' | 'generated'>('photo');
  if (!user) return null;
  const tabs = (
    <div className="cx-avatar-mode-tabs" role="tablist" aria-label={t('profile.photo.modeLabel')}>
      <button type="button" role="tab" aria-selected={mode === 'photo'} className={mode === 'photo' ? 'active' : ''} onClick={() => setMode('photo')}>
        {t('profile.photo.photoTab')}
      </button>
      <button type="button" role="tab" aria-selected={mode === 'generated'} className={mode === 'generated' ? 'active' : ''} onClick={() => setMode('generated')}>
        {t('profile.photo.illustratedTab')}
      </button>
    </div>
  );
  if (mode === 'generated') return <GeneratedAvatarDialog onClose={onClose} tabs={tabs} />;

  const photo = user.avatar.photoUrl ?? null;
  return (
    <ImageCropDialog
      onClose={onClose}
      options={{
        title: t('profile.photo.dialogTitle'),
        subtitle: t('profile.photo.dialogSubtitle'),
        shape: 'circle',
        output: { width: 512, height: 512 },
        previews: [120, 40, 24],
        currentUrl: photo ? apiUrl(photo) : null,
        placeholder: user.avatar.initials,
        upload: async (blob, name) => setUser(await api.uploadPhoto(blob, name.replace('image.', 'profile.'))),
        remove: photo || user.avatar.generatedStyle ? async () => setUser(await api.saveGeneratedAvatar(null, null)) : undefined,
        removeLabel: t('profile.photo.useInitials'),
        removeHint: t('profile.photo.removeHint'),
        modeSwitcher: tabs,
      }}
    />
  );
}

function GeneratedAvatarDialog({ onClose, tabs }: { onClose: () => void; tabs: ReactNode }) {
  const { t } = useTranslation();
  const { user, setUser } = useAuth();
  const [style, setStyle] = useState<StyleChoice>((choices.includes(user?.avatar.generatedStyle as StyleChoice) ? user?.avatar.generatedStyle : 'identicon') as StyleChoice);
  const [seed, setSeed] = useState(user?.avatar.generatedSeed ?? user?.id ?? 'knocktwice');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  if (!user) return null;

  const save = async (selectedStyle: string | null, selectedSeed: string | null) => {
    setBusy(true);
    setError(null);
    try {
      setUser(await api.saveGeneratedAvatar(selectedStyle, selectedSeed));
      onClose();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="cx-dialog-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget && !busy) onClose(); }}>
      <div className="cx-dialog cx-avatar-dialog" role="dialog" aria-modal="true" aria-labelledby="avatar-dialog-title" onKeyDown={(e) => { if (e.key === 'Escape') { e.stopPropagation(); if (!busy) onClose(); } else trapTab(e); }}>
        <header className="cx-dialog-head">
          <span className="cx-dialog-icon" aria-hidden="true"><NavIcon name="user" size={26} /></span>
          <div className="flex-grow-1">
            <h2 id="avatar-dialog-title" className="cx-dialog-title">{t('profile.photo.dialogTitle')}</h2>
            <p className="cx-muted mb-0">{t('profile.photo.illustratedSubtitle')}</p>
          </div>
          <button type="button" className="cx-tool" onClick={onClose} aria-label={t('profile.photo.close')}><NavIcon name="close" size={20} /></button>
        </header>
        {tabs}
        <div className="cx-avatar-picker">
          <div className="cx-avatar-picker-grid" role="listbox" aria-label={t('profile.photo.styleLabel')}>
            {choices.map((choice) => (
              <button key={choice} type="button" role="option" aria-selected={style === choice} className={`cx-avatar-choice${style === choice ? ' selected' : ''}`} onClick={() => setStyle(choice)}>
                <Avatar user={{ id: user.id, avatar: { initials: user.avatar.initials, generatedStyle: choice, generatedSeed: seed } }} size="xl" />
                <span>{t(`profile.photo.styles.${choice}`)}</span>
              </button>
            ))}
          </div>
          <button type="button" className="btn btn-outline-secondary btn-sm cx-avatar-reroll" onClick={() => setSeed(randomSeed())}>
            <NavIcon name="refresh" size={17} /> {t('profile.photo.anotherSuggestion')}
          </button>
          <p className="cx-muted small mb-0">{t('profile.photo.localGeneration')}</p>
          <p className="cx-muted small mb-0">{t('profile.photo.license')} <a href="https://creativecommons.org/publicdomain/zero/1.0/" target="_blank" rel="noreferrer">CC0 1.0</a></p>
        </div>
        {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        <footer className="cx-dialog-foot">
          <button type="button" className="btn btn-link" disabled={busy || (!user.avatar.photoUrl && !user.avatar.generatedStyle)} onClick={() => void save(null, null)}>{t('profile.photo.useInitials')}</button>
          <div className="d-flex gap-2">
            <button type="button" className="btn btn-outline-secondary" onClick={onClose} disabled={busy}>{t('profile.photo.cancel')}</button>
            <button type="button" className="btn btn-primary" onClick={() => void save(style, seed)} disabled={busy}>{t('profile.photo.useIllustration')}</button>
          </div>
        </footer>
      </div>
    </div>
  );
}

function randomSeed(): string {
  return typeof globalThis.crypto?.randomUUID === 'function' ? globalThis.crypto.randomUUID() : Math.random().toString(36).slice(2, 14);
}
