import { useTranslation } from 'react-i18next';
import { Link } from '../../app/router';
import { NavIcon } from '../../shared/components/NavIcon';

/** An address the app does not know: says so, instead of quietly showing the home page. */
export function NotFoundPage() {
  const { t } = useTranslation();
  return (
    <section className="cx-card cx-not-found" aria-labelledby="not-found-title">
      <span className="cx-not-found-icon" aria-hidden="true">
        <NavIcon name="search" size={34} />
      </span>
      <h1 id="not-found-title" className="cx-not-found-title">{t('notFound.title')}</h1>
      <p className="cx-muted mb-4">{t('notFound.text')}</p>
      <Link to="/" className="btn btn-primary">{t('notFound.home')}</Link>
    </section>
  );
}
