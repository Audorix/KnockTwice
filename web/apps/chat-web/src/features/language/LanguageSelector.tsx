import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import { currentLanguage, isSupportedLanguage, selectLanguage, supportedLanguages } from '../../i18n/i18n';

/**
 * Native <select>: keyboard and screen-reader behaviour come from the platform.
 * Language names are shown in their own language so they are recognizable in either UI.
 */
export function LanguageSelector() {
  const { t } = useTranslation();
  const id = useId();

  return (
    <div className="cx-language">
      <label htmlFor={id} className="form-label cx-language-label">
        {t('language.label')}
      </label>
      <select
        id={id}
        className="form-select form-select-sm"
        value={currentLanguage()}
        onChange={(event) => {
          const value = event.target.value;
          if (isSupportedLanguage(value)) void selectLanguage(value);
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
