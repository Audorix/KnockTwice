import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { navigate, useLocation } from '../../app/router';
import type { OrganizationResponse, SupportConversationResponse, SupportStatus } from '../../shared/api/contracts';
import { FormAlert } from '../../shared/components/Field';
import { formatDateTime } from '../../shared/format';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { listOrganizations } from '../organizations/organizationsApi';
import { useRealtime } from '../realtime/RealtimeContext';
import * as api from './businessApi';

/** Organizations whose support inbox the user works: support agents and owners/administrators. */
export function supportOrganizations(organizations: OrganizationResponse[]): OrganizationResponse[] {
  return organizations.filter((o) => o.supportAgent || o.myPermissions.includes('organization.manage'));
}

/** The support inbox (Phase 11): website conversations with status, assignee, claim and close. */
export function SupportPage() {
  return (
    <RequireUser>
      <Support />
    </RequireUser>
  );
}

function Support() {
  const { t, i18n } = useTranslation();
  const { user } = useAuth();
  const { client } = useRealtime();
  const { query } = useLocation();
  const [organizations, setOrganizations] = useState<OrganizationResponse[] | null>(null);
  const [items, setItems] = useState<SupportConversationResponse[] | null>(null);
  const [status, setStatus] = useState<SupportStatus | 'all'>('open');
  const [mine, setMine] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const organizationId = query.get('org') ?? organizations?.[0]?.id ?? '';

  useEffect(() => {
    listOrganizations().then((all) => setOrganizations(supportOrganizations(all)), (err: unknown) => {
      setOrganizations([]);
      setError(err);
    });
  }, []);

  const reload = useCallback(() => {
    if (!organizationId) return;
    api.listSupportInbox(organizationId, status, mine).then(setItems, (err: unknown) => {
      setItems([]);
      setError(err);
    });
  }, [organizationId, status, mine]);

  useEffect(() => {
    setItems(null);
    reload();
    // Messages from visitors do not reach staff who are not in a conversation: refresh now and then.
    const timer = setInterval(reload, 30000);
    return () => clearInterval(timer);
  }, [reload]);

  useEffect(() => {
    if (!client) return;
    const off = [client.onConversationUpdated(() => reload()), client.onReconnected(() => reload())];
    return () => off.forEach((o) => o());
  }, [client, reload]);

  const act = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      await action();
      reload();
    } catch (err) {
      setError(err);
    }
  };

  return (
    <>
      <h1 className="cx-page-title">{t('support.title')}</h1>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {organizations?.length === 0 ? (
        <section className="cx-card">
          <p className="cx-muted mb-0">{t('support.noAccess')}</p>
        </section>
      ) : (
        <section className="cx-card" aria-labelledby="support-list-title">
          <div className="d-flex flex-wrap gap-3 align-items-end mb-3">
            {organizations && organizations.length > 1 && (
              <div>
                <label htmlFor="support-org" className="form-label">{t('channels.organization')}</label>
                <select id="support-org" className="form-select" value={organizationId}
                  onChange={(e) => navigate(`/support?org=${encodeURIComponent(e.target.value)}`, { replace: true })}>
                  {organizations.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
                </select>
              </div>
            )}
            <div>
              <label htmlFor="support-status" className="form-label">{t('support.status')}</label>
              <select id="support-status" className="form-select" value={status} onChange={(e) => setStatus(e.target.value as SupportStatus | 'all')}>
                <option value="open">{t('support.statuses.open')}</option>
                <option value="closed">{t('support.statuses.closed')}</option>
                <option value="all">{t('support.statuses.all')}</option>
              </select>
            </div>
            <div className="form-check mb-2">
              <input id="support-mine" className="form-check-input" type="checkbox" checked={mine} onChange={(e) => setMine(e.target.checked)} />
              <label className="form-check-label" htmlFor="support-mine">{t('support.mine')}</label>
            </div>
          </div>

          <h2 id="support-list-title" className="visually-hidden">{t('support.title')}</h2>
          {items === null ? (
            <p className="cx-muted">{t('auth.working')}</p>
          ) : items.length === 0 ? (
            <p className="cx-muted mb-0">{t('support.empty')}</p>
          ) : (
            <ul className="list-unstyled cx-session-list mb-0">
              {items.map((item) => {
                const mineItem = item.assignedTo?.id === user?.id;
                return (
                  <li key={item.conversationId} className="cx-session">
                    <span className="min-w-0">
                      <strong>{item.visitorName}</strong>{' '}
                      <span className={`badge ${item.status === 'open' ? 'text-bg-success' : 'text-bg-light border'}`}>{t(`support.statuses.${item.status}`)}</span>
                      {item.unreadCount > 0 && <span className="badge rounded-pill text-bg-primary ms-1">{item.unreadCount}</span>}
                      <span className="cx-meta d-block text-truncate">
                        {item.widgetName ?? t('conversations.types.website')} · {formatDateTime(item.lastActivityAtUtc, i18n.language)} ·{' '}
                        {item.assignedTo ? t('support.assignedTo', { name: item.assignedTo.displayName }) : t('support.unassigned')}
                      </span>
                    </span>
                    <span className="d-flex flex-wrap gap-2 flex-shrink-0">
                      {!mineItem && (
                        <button type="button" className="btn btn-sm btn-primary" onClick={() => void act(() => api.claimSupportConversation(item.conversationId))}
                          aria-label={t('support.claimNamed', { name: item.visitorName })}>
                          {t('support.claim')}
                        </button>
                      )}
                      {item.participating && (
                        <button type="button" className="btn btn-sm btn-outline-primary" onClick={() => navigate(`/conversations?id=${encodeURIComponent(item.conversationId)}`)}>
                          {t('channels.open')}
                        </button>
                      )}
                      <button type="button" className="btn btn-sm btn-outline-secondary"
                        onClick={() => void act(() => api.setSupportStatus(item.conversationId, item.status === 'open' ? 'closed' : 'open'))}>
                        {item.status === 'open' ? t('support.close') : t('support.reopen')}
                      </button>
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
        </section>
      )}
    </>
  );
}
