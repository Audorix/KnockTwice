import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import { currentLanguage, isSupportedLanguage, selectLanguage, supportedLanguages } from '../../i18n/i18n';
import { useOptionalAuth } from '../auth/AuthContext';
import { updatePreferences } from '../auth/authApi';

/**
 * Native <select>: keyboard and screen-reader behaviour come from the platform.
 * Language names are shown in their own language so they are recognizable in either UI.
 * Signed-in users' choice is also saved to their account preferences. `compact` keeps the label
 * for assistive technology only (the signed-out top bar).
 */
export function LanguageSelector({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation();
  const auth = useOptionalAuth();
  const user = auth?.user ?? null;
  const setUser = auth?.setUser ?? (() => undefined);
  const id = useId();

  return (
    <div className={`cx-language${compact ? ' is-compact' : ''}`}>
      <label htmlFor={id} className={compact ? 'visually-hidden' : 'form-label cx-language-label'}>
        {t('language.label')}
      </label>
      <select
        id={id}
        className="form-select form-select-sm"
        value={currentLanguage()}
        onChange={(event) => {
          const value = event.target.value;
          if (!isSupportedLanguage(value)) return;
          void selectLanguage(value);
          if (user && user.preferences.uiLanguage !== value) {
            updatePreferences(value).then(setUser, () => {
              // The local choice still applies; the account preference is retried on the next change.
            });
          }
        }}
      >
        {supportedLanguages.map((language) => (
          <option key={language} value={language} lang={language}>
            {t(`language.${language}`)}
          </option>
        ))}
      </select>
    </div>
  );
}
