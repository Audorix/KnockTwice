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
      <h1 className="cx-page-title">{user ? t('home.welcomeBack', { name: user.displayName }) : t('home.title')}</h1>
      {!user && (
        <p className="cx-muted">
          {t('home.signedOut')} <Link to="/sign-in">{t('nav.signIn')}</Link> · <Link to="/register">{t('nav.register')}</Link>
        </p>
      )}

      <ReadinessIndicator />
      <DemoDataCard />

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
    </>
  );
}
