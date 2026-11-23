import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type { ChannelDetailResponse, ChannelVisibility, OrganizationResponse, TeamResponse, WorkspaceMemberResponse } from '../../shared/api/contracts';
import { Field, FormAlert } from '../../shared/components/Field';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { getOrganization, listWorkspaceMembers } from '../organizations/organizationsApi';
import * as api from './businessApi';
import { ChannelMark } from './ChannelMark';

/** One channel: settings, members, teams and guest invitations (managers), or just who is in it. */
export function ChannelPage() {
  return (
    <RequireUser>
      <Channel />
    </RequireUser>
  );
}

function Channel() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const { query } = useLocation();
  const channelId = query.get('id') ?? '';
  const [detail, setDetail] = useState<ChannelDetailResponse | null>(null);
  const [organization, setOrganization] = useState<OrganizationResponse | null>(null);
  const [missing, setMissing] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const reload = useCallback(() => {
    api.getChannel(channelId).then(
      (d) => {
        setDetail(d);
        getOrganization(d.channel.organizationId).then(setOrganization, () => setOrganization(null));
      },
      () => setMissing(true),
    );
  }, [channelId]);
  useEffect(reload, [reload]);

  const act = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      const result = await action();
      if (result && typeof result === 'object' && 'channel' in result) setDetail(result as ChannelDetailResponse);
      else reload();
    } catch (err) {
      setError(err);
    }
  };

  if (missing) {
    return (
      <>
        <FormAlert tone="danger">{t('errors.channel_not_found')}</FormAlert>
        <Link to="/channels">{t('channels.back')}</Link>
      </>
    );
  }
  if (!detail || !user) return <p className="cx-muted">{t('auth.working')}</p>;

  const c = detail.channel;
  const canInviteGuests = !!organization?.myPermissions.includes('organization.members.manage') && organization.guestsEnabled !== false;

  return (
    <>
      <p className="mb-2">
        <Link to={`/channels?org=${encodeURIComponent(c.organizationId)}&ws=${encodeURIComponent(c.workspaceId)}`}>{t('channels.back')}</Link>
      </p>
      <div className="cx-account-header mb-3">
        <h1 className="cx-page-title mb-0">
          <ChannelMark visibility={c.visibility} /> {c.name}
        </h1>
        <span className="badge text-bg-light border">{t(`channels.visibility.${c.visibility}`)}</span>
        <span className="cx-meta">{c.workspaceName}</span>
        <span className="ms-auto d-flex gap-2">
          {c.joined ? (
            <>
              <button type="button" className="btn btn-sm btn-primary" onClick={() => navigate(`/conversations?id=${encodeURIComponent(c.id)}`)}>{t('channels.open')}</button>
              <button type="button" className="btn btn-sm btn-outline-danger"
                onClick={() => window.confirm(t('channels.leaveConfirm', { name: c.name })) && void act(async () => {
                  await api.leaveChannel(c.id);
                  navigate('/channels');
                })}>
                {t('channels.leave')}
              </button>
            </>
          ) : (
            <button type="button" className="btn btn-sm btn-primary" onClick={() => void act(() => api.joinChannel(c.id))}>{t('channels.join')}</button>
          )}
        </span>
      </div>
      {c.description && <p>{c.description}</p>}
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      {c.canManage && <ChannelSettings detail={detail} onSaved={setDetail} />}

      <section className="cx-card mt-3" aria-labelledby="channel-members-title">
        <h2 id="channel-members-title" className="cx-card-title">{t('channels.members', { count: detail.members.length })}</h2>
        <ul className="list-unstyled cx-session-list mb-0">
          {detail.members.map((m) => (
            <li key={m.user.id} className="cx-session">
              <span>
                {m.user.displayName} <span className="cx-meta">· {t(`channels.roles.${m.role}`)}</span>
              </span>
              {c.canManage && m.user.id !== user.id && (
                <span className="d-flex gap-2">
                  {m.role !== 'guest' && (
                    <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.setChannelManager(c.id, m.user.id, m.role !== 'manager'))}>
                      {m.role === 'manager' ? t('channels.makeMember') : t('channels.makeManager')}
                    </button>
                  )}
                  <button type="button" className="btn btn-sm btn-outline-secondary" aria-label={t('channels.removeNamed', { name: m.user.displayName })}
                    onClick={() => void act(() => api.removeChannelMember(c.id, m.user.id))}>
                    {t('contacts.remove')}
                  </button>
                </span>
              )}
            </li>
          ))}
        </ul>
        {c.canManage && <AddMembers detail={detail} onAdded={setDetail} />}
      </section>

      {c.canManage && canInviteGuests && <InviteGuest organizationId={c.organizationId} channelId={c.id} />}
    </>
  );
}

function ChannelSettings({ detail, onSaved }: { detail: ChannelDetailResponse; onSaved: (d: ChannelDetailResponse) => void }) {
  const { t } = useTranslation();
  const c = detail.channel;
  const [name, setName] = useState(c.name);
  const [description, setDescription] = useState(c.description ?? '');
  const [visibility, setVisibility] = useState<ChannelVisibility>(c.visibility);
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setSaved(false);
    try {
      onSaved(await api.updateChannel(c.id, { name: name.trim(), description, visibility }));
      setSaved(true);
    } catch (err) {
      setError(err);
    }
  };

  return (
    <section className="cx-card" aria-labelledby="channel-settings-title">
      <h2 id="channel-settings-title" className="cx-card-title">{t('channels.settings')}</h2>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {saved && <FormAlert tone="success">{t('widgets.saved')}</FormAlert>}
      <form onSubmit={(e) => void submit(e)} noValidate>
        <Field label={t('channels.name')} maxLength={80} value={name} onChange={(e) => setName(e.target.value)} />
        <Field label={t('channels.description')} maxLength={500} value={description} onChange={(e) => setDescription(e.target.value)} />
        <div className="mb-3">
          <label htmlFor="channel-visibility" className="form-label">{t('channels.visibilityLabel')}</label>
          <select id="channel-visibility" className="form-select w-auto" value={visibility} onChange={(e) => setVisibility(e.target.value as ChannelVisibility)}>
            <option value="public">{t('channels.visibility.public')}</option>
            <option value="private">{t('channels.visibility.private')}</option>
          </select>
        </div>
        <button type="submit" className="btn btn-primary" disabled={!name.trim()}>{t('account.save')}</button>
      </form>
    </section>
  );
}

function AddMembers({ detail, onAdded }: { detail: ChannelDetailResponse; onAdded: (d: ChannelDetailResponse) => void }) {
  const { t } = useTranslation();
  const c = detail.channel;
  const [people, setPeople] = useState<WorkspaceMemberResponse[]>([]);
  const [teams, setTeams] = useState<TeamResponse[]>([]);
  const [userId, setUserId] = useState('');
  const [teamId, setTeamId] = useState('');
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    listWorkspaceMembers(c.organizationId, c.workspaceId).then(setPeople, () => setPeople([]));
    api.listTeams(c.organizationId, c.workspaceId).then(setTeams, () => setTeams([]));
  }, [c.organizationId, c.workspaceId]);

  const candidates = people.filter((p) => !detail.members.some((m) => m.user.id === p.user.id));
  const unlinked = teams.filter((team) => !c.teams.some((l) => l.id === team.id));

  const add = async (body: { userIds?: string[]; teamIds?: string[] }) => {
    setError(null);
    try {
      onAdded(await api.addChannelMembers(c.id, body));
      setUserId('');
      setTeamId('');
    } catch (err) {
      setError(err);
    }
  };

  return (
    <div className="cx-subpanel mt-3">
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <div className="d-flex flex-wrap gap-3">
        <form className="cx-inline-form" onSubmit={(e) => { e.preventDefault(); void add({ userIds: [userId] }); }}>
          <div className="mb-3">
            <label htmlFor="channel-add-person" className="form-label">{t('channels.addPerson')}</label>
            <select id="channel-add-person" className="form-select" value={userId} onChange={(e) => setUserId(e.target.value)}>
              <option value="">{t('organizations.chooseMember')}</option>
              {candidates.map((p) => <option key={p.user.id} value={p.user.id}>{p.user.displayName}</option>)}
            </select>
          </div>
          <button type="submit" className="btn btn-outline-primary" disabled={!userId}>{t('contacts.add')}</button>
        </form>
        <form className="cx-inline-form" onSubmit={(e) => { e.preventDefault(); void add({ teamIds: [teamId] }); }}>
          <div className="mb-3">
            <label htmlFor="channel-add-team" className="form-label">{t('channels.addTeam')}</label>
            <select id="channel-add-team" className="form-select" value={teamId} onChange={(e) => setTeamId(e.target.value)}>
              <option value="">{t('channels.chooseTeam')}</option>
              {unlinked.map((team) => <option key={team.id} value={team.id}>{team.name}</option>)}
            </select>
          </div>
          <button type="submit" className="btn btn-outline-primary" disabled={!teamId}>{t('channels.link')}</button>
        </form>
      </div>
      {c.teams.length > 0 && (
        <>
          <h3 className="h6">{t('channels.linkedTeams')}</h3>
          <p className="cx-meta">{t('channels.linkedTeamsHint')}</p>
          <ul className="list-unstyled cx-session-list mb-0">
            {c.teams.map((team) => (
              <li key={team.id} className="cx-session">
                <span>{team.name}</span>
                <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void api.unlinkTeam(c.id, team.id).then(onAdded, setError)}>
                  {t('channels.unlink')}
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}

function InviteGuest({ organizationId, channelId }: { organizationId: string; channelId: string }) {
  const { t } = useTranslation();
  const [email, setEmail] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [sent, setSent] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setSent(false);
    try {
      await api.inviteGuest(organizationId, channelId, email.trim());
      setEmail('');
      setSent(true);
    } catch (err) {
      setError(err);
    }
  };

  return (
    <section className="cx-card mt-3" aria-labelledby="invite-guest-title">
      <h2 id="invite-guest-title" className="cx-card-title">{t('channels.inviteGuest')}</h2>
      <p className="cx-muted">{t('channels.inviteGuestHint')}</p>
      {sent && <FormAlert tone="success">{t('organizations.invited')}</FormAlert>}
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <form className="cx-inline-form" onSubmit={(e) => void submit(e)} noValidate>
        <Field label={t('auth.fields.email')} type="email" value={email} onChange={(e) => setEmail(e.target.value)} className="flex-grow-1" />
        <button type="submit" className="btn btn-primary" disabled={!email.trim()}>{t('organizations.sendInvite')}</button>
      </form>
    </section>
  );
}
