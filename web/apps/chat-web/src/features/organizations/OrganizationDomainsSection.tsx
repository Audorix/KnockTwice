import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { FormAlert } from '../../shared/components/Field';
import { errorMessage } from '../auth/errors';
import * as api from './organizationsApi';

export function OrganizationDomainsSection({ organizationId }: { organizationId: string }) {
  const { t } = useTranslation();
  const [domains, setDomains] = useState<api.OrganizationDomain[]>([]);
  const [enrollmentEnabled, setEnrollmentEnabled] = useState(false);
  const [value, setValue] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const reload = () => Promise.all([api.listOrganizationDomains(organizationId), api.getAutomaticEnrollment(organizationId)])
    .then(([items, enabled]) => { setDomains(items); setEnrollmentEnabled(enabled); }).catch(setError);
  useEffect(() => { void reload(); }, [organizationId]);

  const add = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setError(null);
    try { const created = await api.addOrganizationDomain(organizationId, value); setDomains((items) => [...items, created]); setValue(''); }
    catch (e) { setError(e); }
    finally { setBusy(false); }
  };
  const verify = async (id: string) => {
    setBusy(true); setError(null);
    try { const verified = await api.verifyOrganizationDomain(organizationId, id); setDomains((items) => items.map((item) => item.id === id ? verified : item)); }
    catch (e) { setError(e); }
    finally { setBusy(false); }
  };
  const remove = async (id: string) => {
    setBusy(true); setError(null);
    try { await api.removeOrganizationDomain(organizationId, id); setDomains((items) => items.filter((item) => item.id !== id)); }
    catch (e) { setError(e); }
    finally { setBusy(false); }
  };
  const toggleEnrollment = async (enabled: boolean) => {
    setBusy(true); setError(null);
    try { setEnrollmentEnabled(await api.setAutomaticEnrollment(organizationId, enabled)); }
    catch (e) { setError(e); }
    finally { setBusy(false); }
  };

  return <section className="cx-card cx-org-domains" aria-labelledby="org-domains-title">
    <h2 id="org-domains-title" className="cx-card-title">{t('orgDomains.title')}</h2>
    <p className="cx-muted">{t('orgDomains.hint')}</p>
    {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
    <form className="cx-inline-form" onSubmit={(event) => void add(event)}>
      <label className="form-label" htmlFor="org-domain">{t('orgDomains.domainLabel')}</label>
      <div className="d-flex gap-2">
        <input id="org-domain" className="form-control" value={value} onChange={(event) => setValue(event.target.value)} placeholder="example.com" required maxLength={253} />
        <button type="submit" className="btn btn-primary" disabled={busy}>{t('orgDomains.add')}</button>
      </div>
    </form>
    {domains.length === 0 ? <p className="cx-muted mt-3 mb-0">{t('orgDomains.empty')}</p> : <ul className="list-unstyled mt-3 mb-0">
      {domains.map((domain) => <li key={domain.id} className="cx-org-domain-row">
        <div className="min-w-0 flex-grow-1">
          <div className="d-flex align-items-center gap-2"><strong>{domain.domain}</strong><span className={`badge ${domain.verified ? 'text-bg-success' : 'text-bg-secondary'}`}>{t(domain.verified ? 'orgDomains.verified' : 'orgDomains.pending')}</span></div>
          {!domain.verified && <div className="small mt-2"><div>{t('orgDomains.addTxtRecord')}</div><code>{domain.txtRecordName}</code><br /><code>{domain.txtRecordValue}</code></div>}
        </div>
        <div className="d-flex gap-2 flex-shrink-0">
          {!domain.verified && <button type="button" className="btn btn-outline-primary btn-sm" disabled={busy} onClick={() => void verify(domain.id)}>{t('orgDomains.verify')}</button>}
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy} onClick={() => void remove(domain.id)}>{t('orgDomains.remove')}</button>
        </div>
      </li>)}
    </ul>}
    <div className="form-check form-switch mt-4">
      <input id="org-domain-auto-enrollment" className="form-check-input" type="checkbox" role="switch"
        checked={enrollmentEnabled} disabled={busy || !domains.some((domain) => domain.verified)}
        onChange={(event) => void toggleEnrollment(event.target.checked)} />
      <label className="form-check-label" htmlFor="org-domain-auto-enrollment">{t('orgDomains.enrollmentLabel')}</label>
      <div className="form-text">{t('orgDomains.enrollmentHint')}</div>
    </div>
  </section>;
}
