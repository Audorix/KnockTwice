import { useTranslation } from 'react-i18next';
import { apiUrl } from '../../shared/api/httpClient';
import { useAuth } from '../auth/AuthContext';
import * as api from '../auth/authApi';
import { ImageCropDialog } from '../media/ImageCropDialog';

/**
 * Change profile photo (redesign 2026-11, owner mockup): a circle, checked at 120, 40 and 24 px,
 * uploaded as a 512 px square.
 */
export function ProfilePhotoDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const { user, setUser } = useAuth();
  const photo = user?.avatar.photoUrl ?? null;
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
        placeholder: user?.avatar.initials ?? '',
        upload: async (blob, name) => setUser(await api.uploadPhoto(blob, name.replace('image.', 'profile.'))),
        remove: photo ? async () => setUser(await api.removePhoto()) : undefined,
        removeLabel: t('profile.photo.remove'),
        removeHint: t('profile.photo.removeHint'),
      }}
    />
  );
}
