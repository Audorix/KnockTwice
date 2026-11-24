import { useTranslation } from 'react-i18next';
import { NavIcon } from './NavIcon';

/** Page numbers with the first, last and neighbours of the current page; gaps shown as "…". */
export function Pagination({ page, pages, onPage }: { page: number; pages: number; onPage: (page: number) => void }) {
  const { t } = useTranslation();
  const numbers = [...new Set([1, page - 1, page, page + 1, pages])].filter((n) => n >= 1 && n <= pages).sort((a, b) => a - b);
  return (
    <nav className="cx-pagination" aria-label={t('contacts.pages')}>
      <button type="button" className="cx-page-button" disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label={t('contacts.previousPage')}>
        <NavIcon name="chevron-left" size={16} />
      </button>
      {numbers.map((n, i) => (
        <span key={n} className="d-contents">
          {i > 0 && n - numbers[i - 1]! > 1 && <span className="cx-page-gap" aria-hidden="true">…</span>}
          <button type="button" className={`cx-page-button${n === page ? ' is-current' : ''}`} aria-current={n === page ? 'page' : undefined}
            aria-label={t('contacts.page', { page: n })} onClick={() => onPage(n)}>
            {n}
          </button>
        </span>
      ))}
      <button type="button" className="cx-page-button" disabled={page >= pages} onClick={() => onPage(page + 1)} aria-label={t('contacts.nextPage')}>
        <NavIcon name="chevron-right" size={16} />
      </button>
    </nav>
  );
}
