import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getJson, sendJson } from '../../shared/api/httpClient';
import { useConfirm } from '../../shared/components/ConfirmDialog';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon, type NavIconName } from '../../shared/components/NavIcon';
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
  /** Support conversations from demo customers (also counted in conversations). */
  support?: number;
  /** Demo people with a profile photo from the demo avatars folder. */
  photos?: number;
}

interface DemoDataResponse {
  created: DemoCounts;
  total: DemoCounts;
  userEmails: string[];
  password: string;
}

const sum = (c: DemoCounts) => c.users + c.organizations + c.contacts + c.conversations + c.messages + (c.activity ?? 0) + (c.photos ?? 0);

/**
 * Demo data for installation administrators where it is enabled (Development and development-mode
 * installations): load it (safe to repeat; only missing data is added) or clear it (only demo data;
 * what real people created stays).
 */
export function DemoDataCard() {
  const { t, i18n } = useTranslation();
  const { user, config } = useAuth();
  const [busy, setBusy] = useState(false);
  const [existing, setExisting] = useState<DemoCounts | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [accounts, setAccounts] = useState<DemoDataResponse | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [confirm, confirmDialog] = useConfirm();
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
        users: created.users, organizations: created.organizations, contacts: created.contacts, conversations: created.conversations, messages: created.messages, activity: created.activity, support: created.support ?? 0, photos: created.photos ?? 0,
      }));
    setAccounts(result);
    setExisting(result.total);
  });

  const clear = async () => {
    const ok = await confirm({
      title: t('confirm.clearDemo.title'),
      text: t('confirm.clearDemo.text'),
      confirmLabel: t('confirm.clearDemo.action'),
    });
    if (!ok) return;
    void run(async () => {
      const { removed } = (await sendJson<{ removed: DemoCounts }>('DELETE', '/api/v1/admin/demo-data', undefined, { timeoutMs: 300_000 }))!;
      setNotice(t('demo.cleared', { users: removed.users, organizations: removed.organizations, conversations: removed.conversations, messages: removed.messages }));
      setAccounts(null);
      setExisting({ users: 0, organizations: 0, contacts: 0, conversations: 0, messages: 0, activity: 0, support: 0 });
    });
  };

  const tiles: { key: string; icon: NavIconName; value: number }[] = existing
    ? [
        { key: 'people', icon: 'people', value: existing.users },
        { key: 'companies', icon: 'building', value: existing.organizations },
        { key: 'conversations', icon: 'chats', value: existing.conversations },
        { key: 'messages', icon: 'mail', value: existing.messages },
        { key: 'activity', icon: 'at', value: existing.activity ?? 0 },
        { key: 'support', icon: 'headset', value: existing.support ?? 0 },
      ]
    : [];
  return (
    <section className="cx-card cx-demo-card" aria-labelledby="demo-title">
      {confirmDialog}
      <h2 id="demo-title" className="cx-card-title">
        {t('demo.title')}
      </h2>
      <p className="mb-1">{t('demo.lead')}</p>
      <p className="cx-muted small">{t('demo.intro')}</p>
      {existing && (
        <>
          <h3 className="cx-demo-loaded">{t('demo.loaded')}</h3>
          <ul className="list-unstyled cx-demo-tiles">
            {tiles.map((tile) => (
              <li key={tile.key} className="cx-demo-tile">
                <NavIcon name={tile.icon} size={22} />
                <span>
                  <strong>{tile.value.toLocaleString(i18n.language)}</strong>
                  <span className="cx-muted small d-block">{t(`demo.tiles.${tile.key}`)}</span>
                </span>
              </li>
            ))}
          </ul>
        </>
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
      <p className="cx-muted small mt-2 mb-0">{t('demo.clearNote')}</p>
    </section>
  );
}
