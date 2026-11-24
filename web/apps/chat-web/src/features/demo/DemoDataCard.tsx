import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getJson, sendJson } from '../../shared/api/httpClient';
import { FormAlert } from '../../shared/components/Field';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../auth/errors';

interface DemoCounts {
  users: number;
  organizations: number;
  contacts: number;
  conversations: number;
  messages: number;
  /** Mentions, saved messages, reminders and scheduled messages. */
  activity: number;
}

interface DemoDataResponse {
  created: DemoCounts;
  total: DemoCounts;
  userEmails: string[];
  password: string;
}

const sum = (c: DemoCounts) => c.users + c.organizations + c.contacts + c.conversations + c.messages + (c.activity ?? 0);

/**
 * Demo data for installation administrators where it is enabled (Development and development-mode
 * installations): load it (safe to repeat; only missing data is added) or clear it (only demo data;
 * what real people created stays).
 */
export function DemoDataCard() {
  const { t } = useTranslation();
  const { user, config } = useAuth();
  const [busy, setBusy] = useState(false);
  const [existing, setExisting] = useState<DemoCounts | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [accounts, setAccounts] = useState<DemoDataResponse | null>(null);
  const [error, setError] = useState<unknown>(null);
  const allowed = !!user && !!config?.demoDataEnabled && user.permissions.includes('installation.demo_data');

  useEffect(() => {
    if (!allowed) return;
    getJson<DemoCounts>('/api/v1/admin/demo-data').then((r) => setExisting(r.body ?? null), () => undefined);
  }, [allowed]);

  if (!allowed) return null;

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const populate = () => run(async () => {
    // Loading thousands of records takes a while: wait up to five minutes.
    const result = (await sendJson<DemoDataResponse>('POST', '/api/v1/admin/demo-data', undefined, { timeoutMs: 300_000 }))!;
    const created = result.created;
    setNotice(sum(created) === 0
      ? t('demo.upToDate')
      : t('demo.created', {
        users: created.users, organizations: created.organizations, contacts: created.contacts, conversations: created.conversations, messages: created.messages, activity: created.activity,
      }));
    setAccounts(result);
    setExisting(result.total);
  });

  const clear = () => {
    if (!window.confirm(t('demo.clearConfirm'))) return;
    void run(async () => {
      const { removed } = (await sendJson<{ removed: DemoCounts }>('DELETE', '/api/v1/admin/demo-data', undefined, { timeoutMs: 300_000 }))!;
      setNotice(t('demo.cleared', { users: removed.users, organizations: removed.organizations, conversations: removed.conversations, messages: removed.messages }));
      setAccounts(null);
      setExisting({ users: 0, organizations: 0, contacts: 0, conversations: 0, messages: 0, activity: 0 });
    });
  };

  return (
    <section className="cx-card" aria-labelledby="demo-title">
      <h2 id="demo-title" className="cx-card-title">
        {t('demo.title')}
      </h2>
      <p className="cx-muted">{t('demo.intro')}</p>
      {existing && sum(existing) > 0 && (
        <p className="small cx-meta">
          {t('demo.existing', { users: existing.users, organizations: existing.organizations, conversations: existing.conversations, messages: existing.messages, activity: existing.activity })}
        </p>
      )}
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {notice && (
        <div role="status">
          <FormAlert tone="success">{notice}</FormAlert>
        </div>
      )}
      {accounts && (
        <>
          <p className="small mb-1">{t('demo.signInAs', { password: accounts.password })}</p>
          <p className="small cx-meta">{accounts.userEmails.join(', ')}</p>
        </>
      )}
      <div className="d-flex flex-wrap gap-2">
        <button type="button" className="btn btn-primary" onClick={() => void populate()} disabled={busy}>
          {busy ? t('auth.working') : t('demo.populate')}
        </button>
        <button type="button" className="btn btn-outline-danger" onClick={clear} disabled={busy || !existing || sum(existing) === 0}>
          {t('demo.clear')}
        </button>
      </div>
    </section>
  );
}
