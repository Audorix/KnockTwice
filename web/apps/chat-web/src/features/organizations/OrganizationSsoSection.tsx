import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { FormAlert } from '../../shared/components/Field';
import { errorMessage } from '../auth/errors';
import * as api from './organizationsApi';

export function OrganizationSsoSection({ organizationId }: { organizationId: string }) {
  const { t } = useTranslation();
  const [settings, setSettings] = useState<api.OrganizationSso | null>(null);
  const [issuer, setIssuer] = useState('');
  const [clientId, setClientId] = useState('');
  const [clientSecret, setClientSecret] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const reload = () => api.getOrganizationSso(organizationId).then((value) => {
    setSettings(value);
    setIssuer(value.issuer ?? '');
    setClientId(value.clientId ?? '');
    setClientSecret('');
    setLoaded(true);
  }).catch((reason) => { setError(reason); setLoaded(true); });
  useEffect(() => { void reload(); }, [organizationId]);

  const save = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setError(null);
    try {
      const value = await api.setOrganizationSso(organizationId, { issuer: issuer.trim(), clientId: clientId.trim(), ...(clientSecret ? { clientSecret } : {}) });
      setSettings(value); setClientSecret('');
    } catch (reason) { setError(reason); }
    finally { setBusy(false); }
  };
  const remove = async () => {
    setBusy(true); setError(null);
    try { await api.removeOrganizationSso(organizationId); setSettings({ configured: false, issuer: null, clientId: null, clientSecretConfigured: false }); setIssuer(''); setClientId(''); setClientSecret(''); }
    catch (reason) { setError(reason); }
    finally { setBusy(false); }
  };

  const signInUrl = `${window.location.origin}/api/v1/auth/organization-sso/${encodeURIComponent(organizationId)}`;
  const redirectUri = `${window.location.origin}/api/v1/auth/organization-sso/callback/${encodeURIComponent(organizationId)}`;
  return <section className="cx-card" aria-labelledby="org-sso-title">
    <h2 id="org-sso-title" className="cx-card-title">{t('orgSso.title')}</h2>
    <p className="cx-muted">{t('orgSso.hint')}</p>
    {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
    {settings?.configured && <div className="alert alert-success py-2" role="status">{t('orgSso.enabled')}</div>}
    {loaded && <form onSubmit={(event) => void save(event)}>
      <div className="mb-3">
        <label className="form-label" htmlFor="org-sso-issuer">{t('orgSso.issuer')}</label>
        <input id="org-sso-issuer" className="form-control" type="url" autoComplete="url" value={issuer} onChange={(event) => setIssuer(event.target.value)} placeholder="https://identity.example.com" required maxLength={2048} />
      </div>
      <div className="mb-3">
        <label className="form-label" htmlFor="org-sso-client-id">{t('orgSso.clientId')}</label>
        <input id="org-sso-client-id" className="form-control" value={clientId} onChange={(event) => setClientId(event.target.value)} required maxLength={512} />
      </div>
      <div className="mb-3">
        <label className="form-label" htmlFor="org-sso-client-secret">{t(settings?.clientSecretConfigured ? 'orgSso.replaceSecret' : 'orgSso.clientSecret')}</label>
        <input id="org-sso-client-secret" className="form-control" type="password" autoComplete="new-password" value={clientSecret} onChange={(event) => setClientSecret(event.target.value)} required={!settings?.clientSecretConfigured} maxLength={2048} />
        {settings?.clientSecretConfigured && <div className="form-text">{t('orgSso.secretHint')}</div>}
      </div>
      <div className="d-flex flex-wrap gap-2 align-items-center">
        <button type="submit" className="btn btn-primary" disabled={busy || !issuer.trim() || !clientId.trim() || (!settings?.clientSecretConfigured && !clientSecret)}>{busy ? t('auth.working') : t('orgSso.save')}</button>
        {settings?.configured && <button type="button" className="btn btn-outline-danger" disabled={busy} onClick={() => void remove()}>{t('orgSso.remove')}</button>}
      </div>
    </form>}
    {settings?.configured && <div className="mt-3">
      <label className="form-label" htmlFor="org-sso-redirect-uri">{t('orgSso.redirectUri')}</label>
      <input id="org-sso-redirect-uri" className="form-control mb-3" readOnly value={redirectUri} onFocus={(event) => event.currentTarget.select()} />
      <label className="form-label" htmlFor="org-sso-sign-in-url">{t('orgSso.signInUrl')}</label>
      <div className="input-group">
        <input id="org-sso-sign-in-url" className="form-control" readOnly value={signInUrl} onFocus={(event) => event.currentTarget.select()} />
        <a className="btn btn-outline-secondary" href={signInUrl}>{t('orgSso.openSignIn')}</a>
      </div>
    </div>}
  </section>;
}
