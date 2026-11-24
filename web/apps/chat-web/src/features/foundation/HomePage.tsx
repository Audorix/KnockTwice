import { useTranslation } from 'react-i18next';
import { Link } from '../../app/router';
import { supportedLanguages } from '../../i18n/i18n';
import { useAuth } from '../auth/AuthContext';
import { ReadinessIndicator } from '../readiness/ReadinessIndicator';
import { DemoDataCard } from '../demo/DemoDataCard';

const includedItems = ['api', 'database', 'accounts', 'security', 'organizations', 'contacts', 'realtime', 'conversations', 'localization', 'docker'] as const;

/** Landing page: service status and what this build contains. No chat controls are implied. */
export function HomePage() {
  const { t } = useTranslation();
  const { user } = useAuth();

  return (
    <>
      {user ? (
        <h1 className="cx-page-title">{t('home.welcomeBack', { name: user.displayName })}</h1>
      ) : (
        // Redesign 2026-11: the product introduction moved here from the old side panel.
        <section className="cx-hero" aria-labelledby="home-title">
          <h1 id="home-title" className="cx-hero-title">{t('app.tagline')}</h1>
          <p className="cx-hero-intro">{t('app.intro')}</p>
          <p className="cx-hero-actions">
            <Link to="/register" className="btn btn-primary">{t('nav.register')}</Link>
            <Link to="/sign-in" className="btn btn-outline-primary">{t('nav.signIn')}</Link>
          </p>
        </section>
      )}

      <ReadinessIndicator />
      <DemoDataCard />

      <section className="cx-card" aria-labelledby="included-title">
        <h2 id="included-title" className="cx-card-title">
          {t('foundation.includedTitle')}
        </h2>
        <ul className="cx-checklist cx-checklist-columns">
          {includedItems.map((item) => (
            <li key={item}>{t(`foundation.included.${item}`)}</li>
          ))}
        </ul>
        <p className="cx-meta mb-0">{t('foundation.languageCount', { count: supportedLanguages.length })}</p>
      </section>
    </>
  );
}
