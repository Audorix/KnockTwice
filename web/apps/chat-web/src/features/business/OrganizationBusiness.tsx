import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { GuestInvitationResponse, GuestResponse, OrganizationResponse, TeamMemberResponse, TeamResponse, WorkspaceMemberResponse } from '../../shared/api/contracts';
import { useConfirm } from '../../shared/components/ConfirmDialog';
import { Field, FormAlert } from '../../shared/components/Field';
import { errorMessage } from '../auth/errors';
import * as api from './businessApi';

/** Organization-specific configuration (Phase 11): applies to this organization only. */
export function OrganizationSettingsSection({ organization, onSaved }: { organization: OrganizationResponse; onSaved: (o: OrganizationResponse) => void }) {
  const { t } = useTranslation();
  const [error, setError] = useState<unknown>(null);
  const save = async (body: { guestsEnabled?: boolean; channelCreation?: 'members' | 'administrators'; privateChatsRequireSharedTeam?: boolean }) => {
    setError(null);
    try {
      onSaved(await api.updateOrganizationSettings(organization.id, body));
    } catch (err) {
      setError(err);
    }
  };

  return (
    <section className="cx-card mt-3" aria-labelledby="business-settings-title">
      <h2 id="business-settings-title" className="cx-card-title">{t('business.settingsTitle')}</h2>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <div className="form-check form-switch mb-2">
        <input id="org-guests" className="form-check-input" type="checkbox" role="switch" checked={organization.guestsEnabled !== false}
          onChange={(e) => void save({ guestsEnabled: e.target.checked })} aria-describedby="org-guests-hint" />
        <label className="form-check-label" htmlFor="org-guests">{t('business.guestsEnabled')}</label>
        <div id="org-guests-hint" className="form-text">{t('business.guestsEnabledHint')}</div>
      </div>
      <div className="mb-0">
        <label htmlFor="org-channel-creation" className="form-label">{t('business.channelCreation')}</label>
        <select id="org-channel-creation" className="form-select w-auto" value={organization.channelCreation ?? 'members'}
          onChange={(e) => void save({ channelCreation: e.target.value as 'members' | 'administrators' })}>
          <option value="members">{t('business.channelCreationMembers')}</option>
          <option value="administrators">{t('business.channelCreationAdministrators')}</option>
        </select>
      </div>
      <div className="form-check form-switch mt-3">
        <input id="org-private-chat-team" className="form-check-input" type="checkbox" role="switch"
          checked={organization.privateChatsRequireSharedTeam ?? false}
          onChange={(e) => void save({ privateChatsRequireSharedTeam: e.target.checked })} aria-describedby="org-private-chat-team-hint" />
        <label className="form-check-label" htmlFor="org-private-chat-team">{t('business.privateChatsRequireSharedTeam')}</label>
        <div id="org-private-chat-team-hint" className="form-text">{t('business.privateChatsRequireSharedTeamHint')}</div>
      </div>
    </section>
  );
}

/** The Support Agent role, granted per member (owners and administrators). */
export function SupportAgentToggle({ organizationId, userId, name, value, onChanged }: { organizationId: string; userId: string; name: string; value: boolean; onChanged: (error: unknown) => void }) {
  const { t } = useTranslation();
  return (
    <div className="form-check mb-0">
      <input id={`agent-${userId}`} className="form-check-input" type="checkbox" checked={value}
        onChange={(e) => void api.setSupportAgent(organizationId, userId, e.target.checked).then(() => onChanged(null), onChanged)} />
      <label className="form-check-label small" htmlFor={`agent-${userId}`} aria-label={t('business.supportAgentFor', { name })}>
        {t('business.supportAgent')}
      </label>
    </div>
  );
}

/** Guests and pending guest invitations (owners and administrators). Guests are invited from a channel's page. */
export function GuestsSection({ organizationId }: { organizationId: string }) {
  const { t, i18n } = useTranslation();
  const [guests, setGuests] = useState<GuestResponse[]>([]);
  const [invitations, setInvitations] = useState<GuestInvitationResponse[]>([]);
  const [error, setError] = useState<unknown>(null);
  const reload = useCallback(() => {
    api.listGuests(organizationId).then(setGuests, setError);
    api.listGuestInvitations(organizationId).then(setInvitations, setError);
  }, [organizationId]);
  useEffect(reload, [reload]);

  const act = (action: () => Promise<unknown>) => {
    setError(null);
    void action().then(reload, setError);
  };
  const [confirm, confirmDialog] = useConfirm();

  return (
    <section className="cx-card mt-3" aria-labelledby="guests-title">
      {confirmDialog}
      <h2 id="guests-title" className="cx-card-title">{t('business.guestsTitle')}</h2>
      <p className="cx-muted">{t('business.guestsIntro')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {guests.length === 0 ? (
        <p className="cx-muted">{t('business.noGuests')}</p>
      ) : (
        <ul className="list-unstyled cx-session-list">
          {guests.map((g) => (
            <li key={g.user.id} className="cx-session">
              <span>
                {g.user.displayName} <span className="cx-meta">· {g.channels.map((c) => `#${c.name}`).join(', ') || t('business.noChannels')}</span>
              </span>
              <span className="cx-meta">{t('business.guestAccessExpires', { date: new Date(g.accessExpiresAtUtc).toLocaleDateString(i18n.language) })}</span>
              <button type="button" className="btn btn-sm btn-outline-danger" aria-label={t('business.removeGuestNamed', { name: g.user.displayName })}
                onClick={() => void confirm({
                  title: t('confirm.removeGuest.title', { name: g.user.displayName }),
                  text: t('confirm.removeGuest.text'),
                  confirmLabel: t('confirm.removeGuest.action'),
                }).then((ok) => { if (ok) void (act(() => api.removeGuest(organizationId, g.user.id))); })}>
                {t('contacts.remove')}
              </button>
            </li>
          ))}
        </ul>
      )}
      {invitations.length > 0 && (
        <>
          <h3 className="h6">{t('organizations.pendingInvitations')}</h3>
          <ul className="list-unstyled cx-session-list mb-0">
            {invitations.map((i) => (
              <li key={i.id} className="cx-session">
                <span className="text-truncate">{i.email} <span className="cx-meta">· #{i.channelName}</span><br /><span className="cx-meta">{t('business.invitationExpires', { date: new Date(i.expiresAtUtc).toLocaleDateString(i18n.language) })}</span></span>
                <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => act(() => api.revokeGuestInvitation(organizationId, i.id))}>
                  {t('organizations.revoke')}
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

/** Teams of one workspace: create (administrators) and manage members (team managers and administrators). */
export function TeamsSection({ organizationId, workspaceId, canManageWorkspace, workspaceMembers }: {
  organizationId: string;
  workspaceId: string;
  canManageWorkspace: boolean;
  workspaceMembers: WorkspaceMemberResponse[];
}) {
  const { t } = useTranslation();
  const [teams, setTeams] = useState<TeamResponse[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [error, setError] = useState<unknown>(null);
  const reload = useCallback(() => {
    api.listTeams(organizationId, workspaceId).then(setTeams, setError);
  }, [organizationId, workspaceId]);
  useEffect(reload, [reload]);

  const create = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      await api.createTeam(organizationId, workspaceId, name.trim());
      setName('');
      reload();
    } catch (err) {
      setError(err);
    }
  };

  const [confirm, confirmDialog] = useConfirm();
  const openTeam = teams.find((team) => team.id === open);
  return (
    <div className="mt-3">
      {confirmDialog}
      <h3 className="h6">{t('business.teamsTitle')}</h3>
      <p className="cx-meta">{t('business.teamsIntro')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {teams.length === 0 ? (
        <p className="cx-muted">{t('business.noTeams')}</p>
      ) : (
        <ul className="list-unstyled cx-session-list">
          {teams.map((team) => (
            <li key={team.id} className="cx-session">
              <span>
                <strong>{team.name}</strong>
                <span className="cx-meta"> · {t('business.teamMembers', { count: team.memberCount })}{team.myRole ? ` · ${t(`business.teamRoles.${team.myRole}`)}` : ''}</span>
              </span>
              <span className="d-flex gap-2">
                <button type="button" className="btn btn-sm btn-outline-secondary" aria-expanded={open === team.id} onClick={() => setOpen(open === team.id ? null : team.id)}>
                  {t('organizations.showMembers')}
                </button>
                {canManageWorkspace && (
                  <button type="button" className="btn btn-sm btn-outline-danger" aria-label={t('business.deleteTeamNamed', { name: team.name })}
                    onClick={() => void confirm({
                      title: t('confirm.deleteTeam.title', { name: team.name }),
                      text: t('confirm.deleteTeam.text'),
                      confirmLabel: t('confirm.deleteTeam.action'),
                    }).then((ok) => { if (ok) void (api.deleteTeam(organizationId, workspaceId, team.id).then(reload, setError)); })}>
                    {t('business.deleteTeam')}
                  </button>
                )}
              </span>
            </li>
          ))}
        </ul>
      )}
      {openTeam && (
        <TeamMembers key={openTeam.id} organizationId={organizationId} workspaceId={workspaceId} team={openTeam} workspaceMembers={workspaceMembers} onChanged={reload} />
      )}
      {canManageWorkspace && (
        <form className="cx-inline-form mt-2" onSubmit={(e) => void create(e)} noValidate>
          <Field label={t('business.newTeam')} maxLength={80} value={name} onChange={(e) => setName(e.target.value)} className="flex-grow-1" />
          <button type="submit" className="btn btn-primary" disabled={!name.trim()}>{t('business.createTeam')}</button>
        </form>
      )}
    </div>
  );
}

function TeamMembers({ organizationId, workspaceId, team, workspaceMembers, onChanged }: {
  organizationId: string;
  workspaceId: string;
  team: TeamResponse;
  workspaceMembers: WorkspaceMemberResponse[];
  onChanged: () => void;
}) {
  const { t } = useTranslation();
  const [members, setMembers] = useState<TeamMemberResponse[]>([]);
  const [addUser, setAddUser] = useState('');
  const [error, setError] = useState<unknown>(null);
  const reload = useCallback(() => {
    api.listTeamMembers(organizationId, workspaceId, team.id).then(setMembers, setError);
  }, [organizationId, workspaceId, team.id]);
  useEffect(reload, [reload]);

  const act = (action: () => Promise<unknown>) => {
    setError(null);
    void action().then(() => {
      reload();
      onChanged();
    }, setError);
  };
  const candidates = workspaceMembers.filter((w) => !members.some((m) => m.user.id === w.user.id));

  return (
    <div className="cx-subpanel">
      <h4 className="h6">{t('business.teamMembersOf', { name: team.name })}</h4>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <ul className="list-unstyled cx-session-list">
        {members.map((m) => (
          <li key={m.user.id} className="cx-session">
            <span>{m.user.displayName} <span className="cx-meta">· {t(`business.teamRoles.${m.role}`)}</span></span>
            {team.canManage && (
              <span className="d-flex gap-2">
                <button type="button" className="btn btn-sm btn-outline-secondary"
                  onClick={() => act(() => api.changeTeamRole(organizationId, workspaceId, team.id, m.user.id, m.role === 'manager' ? 'member' : 'manager'))}>
                  {m.role === 'manager' ? t('business.makeTeamMember') : t('business.makeTeamManager')}
                </button>
                <button type="button" className="btn btn-sm btn-outline-secondary" aria-label={t('business.removeFromTeam', { name: m.user.displayName })}
                  onClick={() => act(() => api.removeTeamMember(organizationId, workspaceId, team.id, m.user.id))}>
                  {t('contacts.remove')}
                </button>
              </span>
            )}
          </li>
        ))}
      </ul>
      {team.canManage && candidates.length > 0 && (
        <form className="cx-inline-form" onSubmit={(e) => {
          e.preventDefault();
          act(() => api.addTeamMember(organizationId, workspaceId, team.id, addUser, 'member'));
          setAddUser('');
        }}>
          <div className="mb-3 flex-grow-1">
            <label htmlFor="add-team-member" className="form-label">{t('business.addToTeam')}</label>
            <select id="add-team-member" className="form-select" value={addUser} onChange={(e) => setAddUser(e.target.value)}>
              <option value="">{t('organizations.chooseMember')}</option>
              {candidates.map((c) => <option key={c.user.id} value={c.user.id}>{c.user.displayName}</option>)}
            </select>
          </div>
          <button type="submit" className="btn btn-primary" disabled={!addUser}>{t('contacts.add')}</button>
        </form>
      )}
    </div>
  );
}
