import { useTranslation } from 'react-i18next';
import { supportedLanguages } from '../../i18n/i18n';
import { LanguageSelector } from '../language/LanguageSelector';
import { ReadinessIndicator } from '../readiness/ReadinessIndicator';
import { BrandMark } from '../../shared/components/BrandMark';

const includedItems = ['api', 'database', 'localization', 'docker'] as const;

/**
 * Foundation screen. Deliberately contains no chat, sign-in or navigation controls:
 * those features do not exist yet and must not be implied.
 */
export function FoundationPage() {
  const { t } = useTranslation();

  return (
    <div className="cx-shell">
      <a className="visually-hidden-focusable cx-skip" href="#main">
        {t('app.skipToContent')}
      </a>

      <header className="cx-identity">
        <div className="cx-brand">
          <BrandMark />
          <div>
            <p className="cx-product mb-0">{t('app.name')}</p>
            <span className="cx-badge">{t('app.stage')}</span>
          </div>
        </div>

        <div className="cx-identity-copy">
          <h1 className="cx-tagline">{t('app.tagline')}</h1>
          <p className="cx-intro">{t('app.intro')}</p>
        </div>

        <div className="cx-identity-footer">
          <LanguageSelector />
          <p className="cx-footnote mb-0">{t('app.footer')}</p>
        </div>
      </header>

      <main id="main" className="cx-content" tabIndex={-1}>
        <ReadinessIndicator />

        <div className="cx-grid">
          <section className="cx-card" aria-labelledby="included-title">
            <h2 id="included-title" className="cx-card-title">
              {t('foundation.includedTitle')}
            </h2>
            <ul className="cx-checklist">
              {includedItems.map((item) => (
                <li key={item}>{t(`foundation.included.${item}`)}</li>
              ))}
            </ul>
            <p className="cx-meta mb-0">{t('foundation.languageCount', { count: supportedLanguages.length })}</p>
          </section>

          <section className="cx-card" aria-labelledby="next-title">
            <h2 id="next-title" className="cx-card-title">
              {t('foundation.nextTitle')}
            </h2>
            <p className="cx-muted mb-0">{t('foundation.next')}</p>
          </section>
        </div>
      </main>
    </div>
  );
}
