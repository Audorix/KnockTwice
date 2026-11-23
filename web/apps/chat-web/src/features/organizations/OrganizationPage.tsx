import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type {
  OrganizationMembersResponse,
  OrganizationResponse,
  OrganizationRole,
  WorkspaceMemberResponse,
  WorkspaceResponse,
} from '../../shared/api/contracts';
import { Field, FormAlert } from '../../shared/components/Field';
import { ApiError } from '../../shared/api/httpClient';
import { useAuth } from '../auth/AuthContext';
import { errorMessage, fieldErrors } from '../auth/errors';
import { RequireUser, useAction } from '../auth/RequireUser';
import { PeopleList } from '../contacts/ContactsPage';
import * as api from './organizationsApi';
import { WidgetsSection } from '../widgets/WidgetsSection';
import { GuestsSection, OrganizationSettingsSection, SupportAgentToggle, TeamsSection } from '../business/OrganizationBusiness';

/** One organization: members, invitations and workspaces, with controls shown only when permitted. */
export function OrganizationPage() {
  return (
    <RequireUser>
      <Organization />
    </RequireUser>
  );
}

function Organization() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const { query } = useLocation();
  const organizationId = query.get('id') ?? '';
  const [organization, setOrganization] = useState<OrganizationResponse | null>(null);
  const [members, setMembers] = useState<OrganizationMembersResponse | null>(null);
  const [workspaces, setWorkspaces] = useState<WorkspaceResponse[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [missing, setMissing] = useState(false);

  const reload = useCallback(() => {
    Promise.all([api.getOrganization(organizationId), api.listMembers(organizationId), api.listWorkspaces(organizationId)]).then(
      ([o, m, w]) => {
        setOrganization(o);
        setMembers(m);
        setWorkspaces(w);
      },
      () => setMissing(true),
    );
  }, [organizationId]);
  useEffect(reload, [reload]);
  const act = useAction(setError, reload);

  if (missing) {
    return (
      <>
        <FormAlert tone="danger">{t('errors.organization_not_found')}</FormAlert>
        <Link to="/organizations">{t('organizations.back')}</Link>
      </>
    );
  }

  if (!organization || !members || !user) return <p className="cx-muted">{t('auth.working')}</p>;

  const can = (permission: string) => organization.myPermissions.includes(permission);
  const isOwner = organization.myRole === 'owner';

  return (
    <>
      <p className="mb-2">
        <Link to="/organizations">{t('organizations.back')}</Link>
      </p>
      <div className="cx-account-header mb-3">
        <h1 className="cx-page-title mb-0">{organization.name}</h1>
        <span className="badge text-bg-light border">{t(`organizations.roles.${organization.myRole}`)}</span>
        <button
          type="button"
          className="btn btn-sm btn-outline-danger ms-auto"
          onClick={() =>
            window.confirm(t('organizations.leaveConfirm', { name: organization.name })) &&
            void act(async () => {
              await api.removeMember(organization.id, user.id);
              navigate('/organizations', { replace: true });
            })
          }
        >
          {t('organizations.leave')}
        </button>
      </div>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      {can('organization.manage') && <RenameForm organization={organization} onSaved={setOrganization} />}

      <section className="cx-card mt-3" aria-labelledby="members-title">
        <h2 id="members-title" className="cx-card-title">
          {t('organizations.members', { count: members.members.length })}
        </h2>
        <PeopleList
          items={members.members.map((m) => ({ key: m.user.id, user: m.user, role: m.role, supportAgent: !!m.supportAgent }))}
          actions={(member) => (
            <>
            {can('organization.members.manage') && (
              <SupportAgentToggle organizationId={organization.id} userId={member.key} name={member.user.displayName} value={member.supportAgent}
                onChanged={(err) => (err ? setError(err) : reload())} />
            )}
            {isOwner && member.key !== user.id ? (
              <>
                <label className="visually-hidden" htmlFor={`role-${member.key}`}>
                  {t('organizations.roleFor', { name: member.user.displayName })}
                </label>
                <select
                  id={`role-${member.key}`}
                  className="form-select form-select-sm w-auto"
                  value={member.role}
                  onChange={(e) => void act(() => api.changeRole(organization.id, member.key, e.target.value as OrganizationRole))}
                >
                  {(['owner', 'administrator', 'member'] as const).map((role) => (
                    <option key={role} value={role}>
                      {t(`organizations.roles.${role}`)}
                    </option>
                  ))}
                </select>
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  aria-label={t('organizations.removeNamed', { name: member.user.displayName })}
                  onClick={() => void act(() => api.removeMember(organization.id, member.key))}
                >
                  {t('contacts.remove')}
                </button>
              </>
            ) : (
              <span className="cx-meta">{t(`organizations.roles.${member.role}`)}</span>
            )}
            </>
          )}
        />
      </section>

      {members.invitations && <InvitationsSection organizationId={organization.id} invitations={members.invitations} act={act} />}

      <WorkspacesSection
        organization={organization}
        workspaces={workspaces}
        organizationMembers={members.members.map((m) => m.user)}
        canManageAll={can('organization.workspaces.manage')}
        act={act}
      />

      {can('organization.manage') && <OrganizationSettingsSection organization={organization} onSaved={setOrganization} />}
      {can('organization.members.manage') && <GuestsSection organizationId={organization.id} />}
      {can('organization.manage') && <WidgetsSection organizationId={organization.id} members={members.members.map((m) => m.user)} />}
      {isOwner && <DeleteOrganizationSection organization={organization} />}
    </>
  );
}

/** Owners delete the organization and everything in it after typing its name (Phase 13). */
function DeleteOrganizationSection({ organization }: { organization: OrganizationResponse }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.deleteOrganization(organization.id, name);
      navigate('/organizations', { replace: true });
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="cx-card cx-card-danger mt-3" aria-labelledby="delete-organization-title">
      <div className="cx-card-header">
        <h2 id="delete-organization-title" className="cx-card-title">
          {t('organizations.deletion.title')}
        </h2>
        {!open && (
          <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => setOpen(true)}>
            {t('organizations.deletion.start')}
          </button>
        )}
      </div>
      <p className="cx-muted">{t('organizations.deletion.summary')}</p>
      {open && (
        <form onSubmit={submit} noValidate>
          {error !== null && !(error instanceof ApiError && error.code === 'validation_failed') && (
            <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>
          )}
          <Field
            label={t('organizations.deletion.typeName', { name: organization.name })}
            autoComplete="off"
            value={name}
            onChange={(e) => setName(e.target.value)}
            errors={fieldErrors(error, 'confirmName', t)}
          />
          <div className="d-flex gap-2">
            <button type="submit" className="btn btn-danger" disabled={busy || name.trim() === ''}>
              {t('organizations.deletion.submit')}
            </button>
            <button type="button" className="btn btn-outline-secondary" onClick={() => { setOpen(false); setName(''); setError(null); }}>
              {t('organizations.deletion.cancel')}
            </button>
          </div>
        </form>
      )}
    </section>
  );
}

function RenameForm({ organization, onSaved }: { organization: OrganizationResponse; onSaved: (o: OrganizationResponse) => void }) {
  const { t } = useTranslation();
  const [name, setName] = useState(organization.name);
  const [error, setError] = useState<unknown>(null);
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      onSaved(await api.renameOrganization(organization.id, name));
    } catch (err) {
      setError(err);
    }
  };
  return (
    <section className="cx-card" aria-labelledby="settings-title">
      <h2 id="settings-title" className="cx-card-title">
        {t('organizations.settings')}
      </h2>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <form onSubmit={submit} className="cx-inline-form" noValidate>
        <Field label={t('organizations.name')} maxLength={100} value={name} onChange={(e) => setName(e.target.value)} className="flex-grow-1" />
        <button type="submit" className="btn btn-primary" disabled={!name.trim() || name.trim() === organization.name}>
          {t('account.save')}
        </button>
      </form>
    </section>
  );
}

type Act = (action: () => Promise<unknown>) => Promise<void>;

function InvitationsSection({ organizationId, invitations, act }: { organizationId: string; invitations: NonNullable<OrganizationMembersResponse['invitations']>; act: Act }) {
  const { t } = useTranslation();
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<OrganizationRole>('member');
  const [error, setError] = useState<unknown>(null);
  const [sent, setSent] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setSent(false);
    await act(async () => {
      try {
        await api.invite(organizationId, email.trim(), role);
        setEmail('');
        setSent(true);
      } catch (err) {
        setError(err);
      }
    });
  };

  return (
    <section className="cx-card mt-3" aria-labelledby="invite-title">
      <h2 id="invite-title" className="cx-card-title">
        {t('organizations.invite')}
      </h2>
      {sent && <FormAlert tone="success">{t('organizations.invited')}</FormAlert>}
      {error !== null && fieldErrors(error, 'email', t).length === 0 && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <form onSubmit={submit} className="cx-inline-form" noValidate>
        <Field
          label={t('auth.fields.email')}
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          errors={fieldErrors(error, 'email', t)}
          hint={t('organizations.inviteHint')}
          className="flex-grow-1"
        />
        <div className="mb-3">
          <label htmlFor="invite-role" className="form-label">
            {t('organizations.role')}
          </label>
          <select id="invite-role" className="form-select" value={role} onChange={(e) => setRole(e.target.value as OrganizationRole)}>
            <option value="member">{t('organizations.roles.member')}</option>
            <option value="administrator">{t('organizations.roles.administrator')}</option>
          </select>
        </div>
        <button type="submit" className="btn btn-primary" disabled={!email.trim()}>
          {t('organizations.sendInvite')}
        </button>
      </form>
      {invitations.length > 0 && (
        <>
          <h3 className="h6 mt-2">{t('organizations.pendingInvitations')}</h3>
          <ul className="list-unstyled mb-0 cx-session-list">
            {invitations.map((invitation) => (
              <li key={invitation.id} className="cx-session">
                <span className="text-truncate">
                  {invitation.email} <span className="cx-meta">· {t(`organizations.roles.${invitation.role}`)}</span>
                </span>
                <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.revokeInvitation(organizationId, invitation.id))}>
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

function WorkspacesSection({
  organization,
  workspaces,
  organizationMembers,
  canManageAll,
  act,
}: {
  organization: OrganizationResponse;
  workspaces: WorkspaceResponse[];
  organizationMembers: { id: string; displayName: string; avatar: { initials: string } }[];
  canManageAll: boolean;
  act: Act;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const [open, setOpen] = useState<string | null>(null);
  const [members, setMembers] = useState<WorkspaceMemberResponse[]>([]);
  const [addUser, setAddUser] = useState('');

  const loadMembers = useCallback(
    (workspaceId: string) => api.listWorkspaceMembers(organization.id, workspaceId).then(setMembers, () => setMembers([])),
    [organization.id],
  );

  useEffect(() => {
    if (open) void loadMembers(open);
  }, [open, loadMembers]);

  const openWorkspace = workspaces.find((w) => w.id === open);
  const canManageOpen = canManageAll || openWorkspace?.myRole === 'administrator';
  const candidates = organizationMembers.filter((m) => !members.some((w) => w.user.id === m.id));

  return (
    <section className="cx-card mt-3" aria-labelledby="workspaces-title">
      <h2 id="workspaces-title" className="cx-card-title">
        {t('organizations.workspaces')}
      </h2>
      {workspaces.length === 0 ? (
        <p className="cx-muted">{t('organizations.noWorkspaces')}</p>
      ) : (
        <ul className="list-unstyled cx-session-list">
          {workspaces.map((workspace) => (
            <li key={workspace.id} className="cx-session">
              <span>
                <strong>{workspace.name}</strong>
                {workspace.myRole && <span className="cx-meta"> · {t(`organizations.workspaceRoles.${workspace.myRole}`)}</span>}
              </span>
              <button
                type="button"
                className="btn btn-sm btn-outline-secondary"
                aria-expanded={open === workspace.id}
                aria-controls="workspace-members"
                onClick={() => setOpen(open === workspace.id ? null : workspace.id)}
              >
                {t('organizations.showMembers')}
              </button>
            </li>
          ))}
        </ul>
      )}

      {openWorkspace && (
        <div id="workspace-members" className="cx-subpanel">
          <h3 className="h6">{t('organizations.workspaceMembers', { name: openWorkspace.name })}</h3>
          <PeopleList
            items={members.map((m) => ({ key: m.user.id, user: m.user }))}
            actions={(member) =>
              canManageOpen ? (
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  onClick={() => void act(() => api.removeWorkspaceMember(organization.id, openWorkspace.id, member.key)).then(() => loadMembers(openWorkspace.id))}
                >
                  {t('contacts.remove')}
                </button>
              ) : null
            }
          />
          {canManageOpen && candidates.length > 0 && (
            <form
              className="cx-inline-form mt-2"
              onSubmit={(e) => {
                e.preventDefault();
                void act(() => api.addWorkspaceMember(organization.id, openWorkspace.id, addUser)).then(() => loadMembers(openWorkspace.id));
                setAddUser('');
              }}
            >
              <div className="mb-3 flex-grow-1">
                <label htmlFor="add-workspace-member" className="form-label">
                  {t('organizations.addToWorkspace')}
                </label>
                <select id="add-workspace-member" className="form-select" value={addUser} onChange={(e) => setAddUser(e.target.value)}>
                  <option value="">{t('organizations.chooseMember')}</option>
                  {candidates.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.displayName}
                    </option>
                  ))}
                </select>
              </div>
              <button type="submit" className="btn btn-primary" disabled={!addUser}>
                {t('contacts.add')}
              </button>
            </form>
          )}
          <TeamsSection organizationId={organization.id} workspaceId={openWorkspace.id} canManageWorkspace={canManageOpen} workspaceMembers={members} />
        </div>
      )}

      {canManageAll && (
        <form
          className="cx-inline-form mt-3"
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            void act(() => api.createWorkspace(organization.id, name));
            setName('');
          }}
        >
          <Field label={t('organizations.newWorkspace')} maxLength={80} value={name} onChange={(e) => setName(e.target.value)} className="flex-grow-1" />
          <button type="submit" className="btn btn-primary" disabled={!name.trim()}>
            {t('organizations.createWorkspace')}
          </button>
        </form>
      )}
    </section>
  );
}
