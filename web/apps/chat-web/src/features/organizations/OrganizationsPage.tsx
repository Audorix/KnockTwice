import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate } from '../../app/router';
import type { MyInvitationResponse, OrganizationResponse } from '../../shared/api/contracts';
import { Field, FormAlert } from '../../shared/components/Field';
import { errorMessage } from '../auth/errors';
import { RequireUser, useAction } from '../auth/RequireUser';
import * as api from './organizationsApi';

/** The caller's organizations, invitations addressed to them, and organization creation. */
export function OrganizationsPage() {
  return (
    <RequireUser>
      <Organizations />
    </RequireUser>
  );
}

function Organizations() {
  const { t } = useTranslation();
  const [organizations, setOrganizations] = useState<OrganizationResponse[] | null>(null);
  const [invitations, setInvitations] = useState<MyInvitationResponse[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [name, setName] = useState('');

  const reload = useCallback(() => {
    Promise.all([api.listOrganizations(), api.listMyInvitations()]).then(([o, i]) => {
      setOrganizations(o);
      setInvitations(i);
    }, setError);
  }, []);
  useEffect(reload, [reload]);
  const act = useAction(setError, reload);

  const create = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      const created = await api.createOrganization(name);
      navigate(`/organization?id=${encodeURIComponent(created.id)}`);
    } catch (err) {
      setError(err);
    }
  };

  return (
    <>
      <h1 className="cx-page-title">{t('organizations.title')}</h1>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      {invitations.length > 0 && (
        <section className="cx-card mb-3" aria-labelledby="invitations-title">
          <h2 id="invitations-title" className="cx-card-title">
            {t('organizations.invitations')}
          </h2>
          <ul className="list-unstyled mb-0 cx-session-list">
            {invitations.map((invitation) => (
              <li key={invitation.id} className="cx-session">
                <span>
                  <strong>{invitation.organizationName}</strong>
                  <span className="cx-meta d-block">
                    {t('organizations.invitedAs', { role: t(`organizations.roles.${invitation.role}`) })}
                    {invitation.invitedBy && ` · ${t('organizations.invitedBy', { name: invitation.invitedBy.displayName })}`}
                  </span>
                </span>
                <span className="d-flex gap-2">
                  <button type="button" className="btn btn-sm btn-primary" onClick={() => void act(() => api.acceptInvitation(invitation.id))}>
                    {t('organizations.join')}
                  </button>
                  <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.declineInvitation(invitation.id))}>
                    {t('contacts.decline')}
                  </button>
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="cx-grid">
        <section className="cx-card" aria-labelledby="mine-title">
          <h2 id="mine-title" className="cx-card-title">
            {t('organizations.mine')}
          </h2>
          {organizations === null ? (
            <p className="cx-muted mb-0">{t('auth.working')}</p>
          ) : organizations.length === 0 ? (
            <p className="cx-muted mb-0">{t('organizations.none')}</p>
          ) : (
            <ul className="list-unstyled mb-0 cx-session-list">
              {organizations.map((organization) => (
                <li key={organization.id} className="cx-session">
                  <Link to={`/organization?id=${encodeURIComponent(organization.id)}`}>{organization.name}</Link>
                  <span className="badge text-bg-light border">{t(`organizations.roles.${organization.myRole}`)}</span>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="cx-card" aria-labelledby="create-title">
          <h2 id="create-title" className="cx-card-title">
            {t('organizations.create')}
          </h2>
          <form onSubmit={create} noValidate>
            <Field label={t('organizations.name')} maxLength={100} value={name} onChange={(e) => setName(e.target.value)} hint={t('organizations.createHint')} />
            <button type="submit" className="btn btn-primary" disabled={!name.trim()}>
              {t('organizations.createSubmit')}
            </button>
          </form>
        </section>
      </div>
    </>
  );
}
