import { useEffect, useMemo, useRef, useState, type DragEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../shared/api/httpClient';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon, type NavIconName } from '../../shared/components/NavIcon';
import { useAuth } from '../auth/AuthContext';
import * as api from '../auth/authApi';
import { errorMessage } from '../auth/errors';

export const platforms: api.LinkPlatform[] = ['github', 'linkedin', 'youtube', 'instagram', 'x', 'website'];
const MAX_LINKS = 10;

export const platformIcon: Record<api.LinkPlatform, NavIconName> = {
  github: 'github', linkedin: 'linkedin', youtube: 'youtube', instagram: 'instagram', x: 'x-logo', website: 'globe',
};

const placeholders: Record<api.LinkPlatform, string> = {
  github: 'https://github.com/yourname',
  linkedin: 'https://linkedin.com/in/yourname',
  youtube: 'https://youtube.com/@yourchannel',
  instagram: 'https://instagram.com/yourname',
  x: 'https://x.com/yourname',
  website: 'https://example.com',
};

interface Row extends api.ProfileLink {
  key: string;
}

let counter = 0;
const keyed = (link: api.ProfileLink): Row => ({ ...link, key: `l${++counter}` });
const plain = (rows: Row[]) => rows.map(({ platform, url, visibility }) => ({ platform, url: url.trim(), visibility }));

/**
 * "Media & links" (redesign 2026-11, owner mockup): profile and channel addresses for your
 * contacts, each with its own visibility, in an order you choose. Nothing is connected to the
 * accounts; the server checks that each address belongs to its platform.
 */
export function ProfileLinksTab() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const [saved, setSaved] = useState<api.ProfileLink[] | null>(null);
  const [rows, setRows] = useState<Row[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState(false);
  const dragged = useRef<number | null>(null);

  useEffect(() => {
    api.getMyLinks().then((links) => {
      setSaved(links);
      setRows(links.map(keyed));
    }, setError);
  }, []);

  const dirty = saved !== null && JSON.stringify(plain(rows)) !== JSON.stringify(saved);
  const fieldErrors = error instanceof ApiError ? error.fieldErrors ?? {} : {};
  const rowError = (index: number) => {
    const codes = fieldErrors[`links[${index}].url`] ?? fieldErrors[`links[${index}].platform`] ?? fieldErrors[`links[${index}].visibility`];
    return codes?.[0] ? t(`profile.links.errors.${codes[0]}`, { defaultValue: t('profile.links.errors.link_url_invalid') }) : null;
  };

  const update = (index: number, change: Partial<api.ProfileLink>) => {
    setNotice(false);
    setRows((current) => current.map((r, i) => (i === index ? { ...r, ...change } : r)));
  };

  const move = (from: number, to: number) => {
    if (to < 0 || to >= rows.length || from === to) return;
    setNotice(false);
    setRows((current) => {
      const next = [...current];
      const [row] = next.splice(from, 1);
      next.splice(to, 0, row!);
      return next;
    });
  };

  const add = () => {
    const used = new Set(rows.map((r) => r.platform));
    const platform = platforms.find((p) => !used.has(p) && p !== 'website') ?? 'website';
    setNotice(false);
    setRows((current) => [...current, keyed({ platform, url: '', visibility: 'contacts' })]);
  };

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await api.saveMyLinks(plain(rows));
      setSaved(result);
      setRows(result.map(keyed));
      setNotice(true);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const onGripKey = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return;
    event.preventDefault();
    const to = index + (event.key === 'ArrowUp' ? -1 : 1);
    move(index, to);
    requestAnimationFrame(() => document.getElementById(`link-grip-${to}`)?.focus());
  };

  const preview = useMemo(() => rows.filter((r) => r.visibility === 'contacts' && /^https?:\/\/\S+\.\S+/i.test(r.url.trim())), [rows]);
  if (!user) return null;

  return (
    <div className="cx-links-layout">
      <section className="cx-card cx-links-card" aria-labelledby="links-title">
        <div className="cx-card-header">
          <div>
            <h2 id="links-title" className="cx-card-title mb-1">{t('profile.links.title')}</h2>
            <p className="cx-muted mb-0">{t('profile.links.subtitle')}</p>
          </div>
          <button type="button" className="btn btn-outline-primary cx-icon-button" onClick={add} disabled={rows.length >= MAX_LINKS || saved === null}>
            <NavIcon name="plus" size={18} />
            {t('profile.links.add')}
          </button>
        </div>

        {error !== null && Object.keys(fieldErrors).length === 0 && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        {notice && !dirty && <FormAlert tone="success">{t('profile.links.saved')}</FormAlert>}

        {saved === null ? (
          <p className="cx-muted">{t('auth.working')}</p>
        ) : rows.length === 0 ? (
          <div className="cx-links-empty">
            <NavIcon name="link" size={28} />
            <p className="mb-0">{t('profile.links.empty')}</p>
          </div>
        ) : (
          <div className="cx-links-table" role="table" aria-label={t('profile.links.title')}>
            <div className="cx-links-head" role="row">
              <span role="columnheader"><span className="visually-hidden">{t('profile.links.order')}</span></span>
              <span role="columnheader">{t('profile.links.platform')}</span>
              <span role="columnheader">{t('profile.links.url')}</span>
              <span role="columnheader">{t('profile.links.visibleTo')}</span>
              <span role="columnheader"><span className="visually-hidden">{t('profile.links.remove')}</span></span>
            </div>
            {rows.map((row, index) => {
              const problem = rowError(index);
              return (
                <div
                  key={row.key}
                  className="cx-links-row"
                  role="row"
                  onDragOver={(e: DragEvent) => e.preventDefault()}
                  onDrop={(e: DragEvent) => {
                    e.preventDefault();
                    if (dragged.current !== null) move(dragged.current, index);
                    dragged.current = null;
                  }}
                >
                  <span role="cell">
                    <button
                      id={`link-grip-${index}`}
                      type="button"
                      className="cx-links-grip"
                      draggable
                      onDragStart={() => { dragged.current = index; }}
                      onKeyDown={(e) => onGripKey(e, index)}
                      aria-label={t('profile.links.moveLabel', { position: index + 1, count: rows.length })}
                      title={t('profile.links.moveHint')}
                    >
                      <NavIcon name="grip" size={18} />
                    </button>
                  </span>
                  <span role="cell" className="cx-links-platform">
                    <NavIcon name={platformIcon[row.platform]} size={18} />
                    <label className="visually-hidden" htmlFor={`link-platform-${index}`}>{t('profile.links.platform')}</label>
                    <select id={`link-platform-${index}`} className="form-select" value={row.platform}
                      onChange={(e) => update(index, { platform: e.target.value as api.LinkPlatform })}>
                      {platforms.map((p) => <option key={p} value={p}>{t(`profile.links.platforms.${p}`)}</option>)}
                    </select>
                  </span>
                  <span role="cell">
                    <label className="visually-hidden" htmlFor={`link-url-${index}`}>{t('profile.links.urlFor', { platform: t(`profile.links.platforms.${row.platform}`) })}</label>
                    <input id={`link-url-${index}`} type="url" inputMode="url" className={`form-control${problem ? ' is-invalid' : ''}`}
                      placeholder={placeholders[row.platform]} value={row.url} maxLength={300}
                      aria-invalid={problem ? true : undefined} aria-describedby={problem ? `link-error-${index}` : undefined}
                      onChange={(e) => update(index, { url: e.target.value })} />
                    {problem && <span id={`link-error-${index}`} className="invalid-feedback d-block">{problem}</span>}
                  </span>
                  <span role="cell">
                    <label className="visually-hidden" htmlFor={`link-visibility-${index}`}>{t('profile.links.visibleTo')}</label>
                    <select id={`link-visibility-${index}`} className="form-select" value={row.visibility}
                      onChange={(e) => update(index, { visibility: e.target.value as api.LinkVisibility })}>
                      <option value="contacts">{t('profile.links.visibility.contacts')}</option>
                      <option value="only_me">{t('profile.links.visibility.only_me')}</option>
                    </select>
                  </span>
                  <span role="cell">
                    <button type="button" className="cx-tool" onClick={() => { setNotice(false); setRows((current) => current.filter((_, i) => i !== index)); }}
                      aria-label={t('profile.links.removeNamed', { platform: t(`profile.links.platforms.${row.platform}`) })}>
                      <NavIcon name="trash" size={18} />
                    </button>
                  </span>
                </div>
              );
            })}
          </div>
        )}

        <div className="cx-links-note">
          <NavIcon name="link" size={18} />
          <div>
            <div>{t('profile.links.note')}</div>
            <div className="cx-muted small">{t('profile.links.platformsNote')}</div>
          </div>
        </div>

        <div className="cx-links-actions">
          {dirty && <span className="cx-links-pending"><span aria-hidden="true" />{t('profile.links.pending')}</span>}
          <button type="button" className="btn btn-outline-secondary" disabled={!dirty || busy}
            onClick={() => { setRows((saved ?? []).map(keyed)); setError(null); }}>
            {t('profile.links.discard')}
          </button>
          <button type="button" className="btn btn-primary" disabled={!dirty || busy} onClick={() => void save()}>
            {t('profile.links.save')}
          </button>
        </div>
      </section>

      <aside className="cx-card cx-links-preview" aria-labelledby="links-preview-title">
        <h2 id="links-preview-title" className="cx-card-title mb-1">{t('profile.links.previewTitle')}</h2>
        <p className="cx-muted">{t('profile.links.previewSubtitle')}</p>
        <div className="cx-links-preview-person">
          <Avatar user={user} />
          <strong>{user.displayName}</strong>
        </div>
        {preview.length === 0 ? (
          <p className="cx-muted small">{t('profile.links.previewEmpty')}</p>
        ) : (
          <ul className="list-unstyled cx-link-list">
            {preview.map((link) => <LinkItem key={link.key} link={link} />)}
          </ul>
        )}
        <p className="cx-links-preview-hint">
          <NavIcon name="eye" size={16} />
          {t('profile.links.previewHint')}
        </p>
        <div className="cx-links-control">
          <NavIcon name="shield" size={26} />
          <div>
            <strong>{t('profile.links.controlTitle')}</strong>
            <div className="cx-muted small">{t('profile.links.controlText')}</div>
          </div>
        </div>
      </aside>
    </div>
  );
}

/** One link as others see it: the platform's mark and name, opening in a new tab. */
export function LinkItem({ link }: { link: api.ProfileLink }) {
  const { t } = useTranslation();
  const name = t(`profile.links.platforms.${link.platform}`);
  return (
    <li>
      <a className="cx-link-item" href={link.url.trim()} target="_blank" rel="noopener noreferrer nofollow" title={link.url}>
        <NavIcon name={platformIcon[link.platform]} size={20} />
        <span className="cx-link-item-name">{name}</span>
        <NavIcon name="external" size={16} />
        <span className="visually-hidden">{t('profile.links.opensNew')}</span>
      </a>
    </li>
  );
}
