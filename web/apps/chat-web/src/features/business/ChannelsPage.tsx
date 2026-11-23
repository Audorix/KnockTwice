import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { navigate, useLocation } from '../../app/router';
import type { ChannelResponse, ChannelVisibility, MyGuestInvitationResponse, OrganizationResponse, TeamResponse, WorkspaceResponse } from '../../shared/api/contracts';
import { Field, FormAlert } from '../../shared/components/Field';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { listOrganizations, listWorkspaces } from '../organizations/organizationsApi';
import * as api from './businessApi';
import { ChannelMark } from './ChannelMark';

/**
 * Channels (Phase 11): pick an organization and workspace, browse its public channels and your
 * private ones, join, open, or create a channel. Guest invitations to channels appear on top.
 */
export function ChannelsPage() {
  return (
    <RequireUser>
      <Channels />
    </RequireUser>
  );
}

function Channels() {
  const { t } = useTranslation();
  const { query } = useLocation();
  const [organizations, setOrganizations] = useState<OrganizationResponse[] | null>(null);
  const [workspaces, setWorkspaces] = useState<WorkspaceResponse[]>([]);
  const [channels, setChannels] = useState<ChannelResponse[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [creating, setCreating] = useState(false);
  const organizationId = query.get('org') ?? organizations?.[0]?.id ?? '';
  const workspaceId = query.get('ws') ?? workspaces.find((w) => w.myRole)?.id ?? workspaces[0]?.id ?? '';

  useEffect(() => {
    listOrganizations().then(setOrganizations, (err: unknown) => {
      setOrganizations([]);
      setError(err);
    });
  }, []);

  useEffect(() => {
    setWorkspaces([]);
    if (organizationId) listWorkspaces(organizationId).then(setWorkspaces, setError);
  }, [organizationId]);

  const reload = useCallback(() => {
    if (!organizationId || !workspaceId) return;
    setChannels(null);
    api.listChannels(organizationId, workspaceId).then(setChannels, (err: unknown) => {
      setChannels([]);
      setError(err);
    });
  }, [organizationId, workspaceId]);
  useEffect(reload, [reload]);

  const select = (org: string, ws?: string) => navigate(`/channels?org=${encodeURIComponent(org)}${ws ? `&ws=${encodeURIComponent(ws)}` : ''}`, { replace: true });

  const join = async (channel: ChannelResponse) => {
    setError(null);
    try {
      await api.joinChannel(channel.id);
      navigate(`/conversations?id=${encodeURIComponent(channel.id)}`);
    } catch (err) {
      setError(err);
    }
  };

  return (
    <>
      <h1 className="cx-page-title">{t('channels.title')}</h1>
      <GuestInvitations />
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      {organizations?.length === 0 ? (
        <section className="cx-card">
          <p className="cx-muted mb-0">{t('channels.noOrganization')}</p>
        </section>
      ) : (
        <section className="cx-card" aria-labelledby="channels-list-title">
          <div className="d-flex flex-wrap gap-2 align-items-end mb-3">
            <div>
              <label htmlFor="channels-org" className="form-label">{t('channels.organization')}</label>
              <select id="channels-org" className="form-select" value={organizationId} onChange={(e) => select(e.target.value)}>
                {organizations?.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
              </select>
            </div>
            <div>
              <label htmlFor="channels-ws" className="form-label">{t('channels.workspace')}</label>
              <select id="channels-ws" className="form-select" value={workspaceId} onChange={(e) => select(organizationId, e.target.value)}>
                {workspaces.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
              </select>
            </div>
            <button type="button" className="btn btn-primary ms-auto" aria-expanded={creating} onClick={() => setCreating(!creating)} disabled={!workspaceId}>
              {t('channels.new')}
            </button>
          </div>

          {creating && workspaceId && (
            <CreateChannel organizationId={organizationId} workspaceId={workspaceId} onCancel={() => setCreating(false)}
              onCreated={(id) => navigate(`/conversations?id=${encodeURIComponent(id)}`)} />
          )}

          <h2 id="channels-list-title" className="cx-card-title">{t('channels.inWorkspace')}</h2>
          {channels === null ? (
            <p className="cx-muted">{t('auth.working')}</p>
          ) : channels.length === 0 ? (
            <p className="cx-muted mb-0">{t('channels.none')}</p>
          ) : (
            <ul className="list-unstyled cx-session-list mb-0">
              {channels.map((c) => (
                <li key={c.id} className="cx-session">
                  <span className="min-w-0">
                    <strong className="cx-channel-name">
                      <ChannelMark visibility={c.visibility} /> {c.name}
                    </strong>
                    <span className="cx-meta d-block text-truncate">
                      {t(`channels.visibility.${c.visibility}`)} · {t('channels.members', { count: c.memberCount })}
                      {c.description ? ` · ${c.description}` : ''}
                    </span>
                  </span>
                  <span className="d-flex gap-2 flex-shrink-0">
                    {c.joined ? (
                      <button type="button" className="btn btn-sm btn-primary" onClick={() => navigate(`/conversations?id=${encodeURIComponent(c.id)}`)}>
                        {t('channels.open')}
                      </button>
                    ) : (
                      <button type="button" className="btn btn-sm btn-outline-primary" onClick={() => void join(c)} aria-label={t('channels.joinNamed', { name: c.name })}>
                        {t('channels.join')}
                      </button>
                    )}
                    {c.canManage && (
                      <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => navigate(`/channel?id=${encodeURIComponent(c.id)}`)}
                        aria-label={t('channels.manageNamed', { name: c.name })}>
                        {t('channels.manage')}
                      </button>
                    )}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </>
  );
}

function CreateChannel({ organizationId, workspaceId, onCreated, onCancel }: { organizationId: string; workspaceId: string; onCreated: (id: string) => void; onCancel: () => void }) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [visibility, setVisibility] = useState<ChannelVisibility>('public');
  const [teams, setTeams] = useState<TeamResponse[]>([]);
  const [teamIds, setTeamIds] = useState<string[]>([]);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    api.listTeams(organizationId, workspaceId).then(setTeams, () => setTeams([]));
  }, [organizationId, workspaceId]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      const created = await api.createChannel(organizationId, workspaceId, { name: name.trim(), description, visibility, teamIds });
      onCreated(created.channel.id);
    } catch (err) {
      setError(err);
    }
  };

  return (
    <form className="cx-subpanel mb-3" onSubmit={(e) => void submit(e)} noValidate aria-label={t('channels.new')}>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <Field label={t('channels.name')} maxLength={80} value={name} onChange={(e) => setName(e.target.value)} />
      <Field label={t('channels.description')} maxLength={500} value={description} onChange={(e) => setDescription(e.target.value)} />
      <fieldset className="mb-3">
        <legend className="form-label fs-6">{t('channels.visibilityLabel')}</legend>
        {(['public', 'private'] as const).map((v) => (
          <div key={v} className="form-check">
            <input id={`visibility-${v}`} className="form-check-input" type="radio" name="visibility" checked={visibility === v} onChange={() => setVisibility(v)} />
            <label className="form-check-label" htmlFor={`visibility-${v}`}>
              {t(`channels.visibility.${v}`)} <span className="cx-meta">— {t(`channels.visibilityHint.${v}`)}</span>
            </label>
          </div>
        ))}
      </fieldset>
      {teams.length > 0 && (
        <fieldset className="mb-3">
          <legend className="form-label fs-6">{t('channels.addTeams')}</legend>
          {teams.map((team) => (
            <div key={team.id} className="form-check">
              <input id={`team-${team.id}`} className="form-check-input" type="checkbox" checked={teamIds.includes(team.id)}
                onChange={(e) => setTeamIds(e.target.checked ? [...teamIds, team.id] : teamIds.filter((x) => x !== team.id))} />
              <label className="form-check-label" htmlFor={`team-${team.id}`}>{team.name}</label>
            </div>
          ))}
        </fieldset>
      )}
      <div className="d-flex gap-2">
        <button type="submit" className="btn btn-primary" disabled={!name.trim()}>{t('channels.create')}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>{t('conversations.cancel')}</button>
      </div>
    </form>
  );
}

/** Invitations to join a channel as a guest (addressed to the signed-in user's email). */
function GuestInvitations() {
  const { t } = useTranslation();
  const [items, setItems] = useState<MyGuestInvitationResponse[]>([]);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    api.listMyGuestInvitations().then(setItems, () => setItems([]));
  }, []);
  if (items.length === 0) return null;

  const accept = async (id: string) => {
    try {
      const conversationId = await api.acceptGuestInvitation(id);
      navigate(`/conversations?id=${encodeURIComponent(conversationId)}`);
    } catch (err) {
      setError(err);
    }
  };
  const decline = async (id: string) => {
    try {
      await api.declineGuestInvitation(id);
      setItems((current) => current.filter((i) => i.id !== id));
    } catch (err) {
      setError(err);
    }
  };

  return (
    <section className="cx-card mb-3" aria-labelledby="guest-invitations-title">
      <h2 id="guest-invitations-title" className="cx-card-title">{t('channels.guestInvitations')}</h2>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <ul className="list-unstyled cx-session-list mb-0">
        {items.map((i) => (
          <li key={i.id} className="cx-session">
            <span>{t('channels.guestInvitation', { channel: i.channelName, organization: i.organizationName, name: i.invitedBy?.displayName ?? t('conversations.someone') })}</span>
            <span className="d-flex gap-2">
              <button type="button" className="btn btn-sm btn-primary" onClick={() => void accept(i.id)}>{t('channels.acceptGuest')}</button>
              <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void decline(i.id)}>{t('channels.declineGuest')}</button>
            </span>
          </li>
        ))}
      </ul>
      <p className="cx-meta mt-2 mb-0">{t('channels.guestHint')}</p>
    </section>
  );
}
