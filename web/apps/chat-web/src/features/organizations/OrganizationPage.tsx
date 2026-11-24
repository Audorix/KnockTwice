import { useCallback, useEffect, useRef, useState, type FormEvent, type KeyboardEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type {
  OrganizationMembersResponse,
  OrganizationResponse,
  OrganizationRole,
  WorkspaceMemberResponse,
  WorkspaceResponse,
} from '../../shared/api/contracts';
import { useConfirm } from '../../shared/components/ConfirmDialog';
import { Field, FormAlert } from '../../shared/components/Field';
import { NavIcon, type NavIconName } from '../../shared/components/NavIcon';
import { ApiError, apiUrl } from '../../shared/api/httpClient';
import { useAuth } from '../auth/AuthContext';
import { errorMessage, fieldErrors } from '../auth/errors';
import { RequireUser, useAction } from '../auth/RequireUser';
import { PeopleList } from '../contacts/ContactsPage';
import { ImageCropDialog, type ImageCropOptions } from '../media/ImageCropDialog';
import * as api from './organizationsApi';
import { OrganizationMark, organizationInitials } from './OrganizationMark';
import { WidgetsSection } from '../widgets/WidgetsSection';
import { listGuests } from '../business/businessApi';
import { GuestsSection, OrganizationSettingsSection, SupportAgentToggle, TeamsSection } from '../business/OrganizationBusiness';

type Tab = 'profile' | 'members' | 'workspaces' | 'channels' | 'website' | 'settings';
const allTabs: Tab[] = ['profile', 'members', 'workspaces', 'channels', 'website', 'settings'];

/**
 * One organization (redesign 2026-11-24, owner mockup): a header with its background image,
 * picture, name and counts, then tabs for its profile, members, workspaces, channels and guests,
 * website chat and settings. Controls show only when permitted.
 */
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
  const [guests, setGuests] = useState<number | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [missing, setMissing] = useState(false);
  const [editing, setEditing] = useState<api.OrganizationImage | null>(null);

  const reload = useCallback(() => {
    Promise.all([api.getOrganization(organizationId), api.listMembers(organizationId), api.listWorkspaces(organizationId)]).then(
      ([o, m, w]) => {
        setOrganization(o);
        setMembers(m);
        setWorkspaces(w);
        if (o.myPermissions.includes('organization.members.manage')) listGuests(o.id).then((g) => setGuests(g.length), () => setGuests(null));
      },
      () => setMissing(true),
    );
  }, [organizationId]);
  useEffect(reload, [reload]);
  const act = useAction(setError, reload);
  const [confirm, confirmDialog] = useConfirm();

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
  const canManage = can('organization.manage');
  const available = allTabs.filter((x) =>
    x === 'channels' ? can('organization.members.manage') : x === 'website' ? canManage : true);
  const requested = query.get('tab') as Tab | null;
  const tab: Tab = requested && available.includes(requested) ? requested : 'profile';
  const select = (next: Tab) =>
    navigate(`/organization?id=${encodeURIComponent(organization.id)}${next === 'profile' ? '' : `&tab=${next}`}`, { replace: true });

  const leave = () =>
    void confirm({
      title: t('confirm.leaveOrganization.title', { name: organization.name }),
      text: t('confirm.leaveOrganization.text'),
      confirmLabel: t('confirm.leaveOrganization.action'),
    }).then((ok) => { if (ok) void (act(async () => {
      await api.removeMember(organization.id, user.id);
      navigate('/organizations', { replace: true });
    })); });

  const memberCount = members.members.length;
  return (
    <div className="cx-organization-page">
      {confirmDialog}
      <nav aria-label={t('nav.label')} className="cx-breadcrumb">
        <Link to="/organizations">{t('organizations.title')}</Link>
        <span aria-hidden="true">/</span>
        <span aria-current="page">{organization.name}</span>
      </nav>

      <section className="cx-card cx-org-hero" aria-labelledby="organization-title"
        style={organization.bannerUrl ? { backgroundImage: `url("${apiUrl(organization.bannerUrl)}")` } : undefined}>
        <div className={`cx-org-hero-inner${organization.bannerUrl ? ' has-banner' : ''}`}>
          <div className="cx-org-hero-mark">
            <OrganizationMark organization={organization} size="xl" />
            {canManage && (
              <button type="button" className="cx-org-hero-camera" onClick={() => setEditing('photo')} title={t('orgPage.changePicture')}>
                <NavIcon name="camera" size={18} />
                <span className="visually-hidden">{t('orgPage.changePicture')}</span>
              </button>
            )}
          </div>
          <div className="min-w-0 flex-grow-1">
            <div className="cx-org-hero-name">
              <h1 id="organization-title" className="cx-org-hero-title text-truncate">{organization.name}</h1>
              <span className="cx-org-role">{t(`organizations.roles.${organization.myRole}`)}</span>
            </div>
            <p className="cx-org-hero-meta mb-0">
              {t('orgPage.memberCount', { count: memberCount })} <span aria-hidden="true">·</span> {t('orgPage.workspaceCount', { count: workspaces.length })}
            </p>
          </div>
          {canManage && (
            <button type="button" className="btn btn-outline-light cx-icon-button cx-org-hero-action" onClick={() => setEditing('banner')}>
              <NavIcon name="camera" size={18} />
              {t('orgPage.changeBackground')}
            </button>
          )}
        </div>
      </section>

      <div className="cx-tabs cx-org-tabs" role="tablist" aria-label={t('orgPage.tabsLabel')}>
        {available.map((x) => (
          <button key={x} id={`org-tab-${x}`} type="button" role="tab" aria-selected={tab === x} aria-controls="org-panel"
            tabIndex={tab === x ? 0 : -1} className={`cx-tab${tab === x ? ' is-active' : ''}`} onClick={() => select(x)}
            onKeyDown={(e) => moveTab(e, available, x, select)}>
            {t(`orgPage.tabs.${x}`)}
          </button>
        ))}
      </div>

      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      <div role="tabpanel" id="org-panel" aria-labelledby={`org-tab-${tab}`} className="cx-org-panel">
        {tab === 'profile' && (
          <div className="cx-org-profile">
            <ProfileCard organization={organization} canManage={canManage} onSaved={setOrganization} onEdit={setEditing} />
            <aside className="cx-org-side">
              <section className="cx-card" aria-labelledby="org-glance-title">
                <h2 id="org-glance-title" className="cx-card-title">{t('orgPage.glance')}</h2>
                <ul className="list-unstyled cx-org-glance mb-0">
                  <Glance icon="people" label={t('orgPage.tabs.members')} value={memberCount} />
                  <Glance icon="layers" label={t('orgPage.tabs.workspaces')} value={workspaces.length} />
                  {guests !== null && <Glance icon="user-plus" label={t('orgPage.guests')} value={guests} />}
                </ul>
              </section>
              {(can('organization.members.manage') || can('organization.workspaces.manage')) && (
                <section className="cx-card" aria-labelledby="org-actions-title">
                  <h2 id="org-actions-title" className="cx-card-title">{t('orgPage.quickActions')}</h2>
                  <div className="d-grid gap-2">
                    {can('organization.members.manage') && (
                      <button type="button" className="btn btn-outline-secondary cx-icon-button cx-org-quick" onClick={() => select('members')}>
                        <NavIcon name="user-plus" size={20} />
                        {t('organizations.invite')}
                      </button>
                    )}
                    {can('organization.workspaces.manage') && (
                      <button type="button" className="btn btn-outline-secondary cx-icon-button cx-org-quick" onClick={() => select('workspaces')}>
                        <NavIcon name="layers" size={20} />
                        {t('organizations.createWorkspace')}
                      </button>
                    )}
                  </div>
                </section>
              )}
            </aside>
          </div>
        )}

        {tab === 'members' && (
          <>
            {members.invitations && <InvitationsSection organizationId={organization.id} invitations={members.invitations} act={act} />}
            <section className="cx-card" aria-labelledby="members-title">
              <h2 id="members-title" className="cx-card-title">
                {t('organizations.members', { count: memberCount })}
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
          </>
        )}

        {tab === 'workspaces' && (
          <WorkspacesSection
            organization={organization}
            workspaces={workspaces}
            organizationMembers={members.members.map((m) => m.user)}
            canManageAll={can('organization.workspaces.manage')}
            act={act}
          />
        )}

        {tab === 'channels' && (
          <>
            <section className="cx-card cx-org-channels-link">
              <div className="min-w-0">
                <h2 className="cx-card-title">{t('channels.title')}</h2>
                <p className="cx-muted mb-0">{t('orgPage.channelsHint')}</p>
              </div>
              <Link to={`/channels?org=${encodeURIComponent(organization.id)}`} className="btn btn-outline-primary">{t('orgPage.openChannels')}</Link>
            </section>
            <GuestsSection organizationId={organization.id} />
          </>
        )}

        {tab === 'website' && <WidgetsSection organizationId={organization.id} members={members.members.map((m) => m.user)} />}

        {tab === 'settings' && (
          <>
            {canManage && <OrganizationSettingsSection organization={organization} onSaved={setOrganization} />}
            <section className="cx-card cx-org-leave">
              <div className="min-w-0">
                <h2 className="cx-card-title">{t('organizations.leave')}</h2>
                <p className="cx-muted mb-0">{t('confirm.leaveOrganization.text')}</p>
              </div>
              <button type="button" className="btn btn-outline-danger" onClick={leave}>{t('organizations.leave')}</button>
            </section>
            {isOwner && <DeleteOrganizationSection organization={organization} />}
          </>
        )}
      </div>

      {editing && (
        <ImageCropDialog
          onClose={() => setEditing(null)}
          options={imageOptions(editing, organization, t, setOrganization)}
        />
      )}
    </div>
  );
}

function imageOptions(
  kind: api.OrganizationImage,
  organization: OrganizationResponse,
  t: (key: string) => string,
  onSaved: (o: OrganizationResponse) => void,
): ImageCropOptions {
  const current = kind === 'photo' ? organization.photoUrl : organization.bannerUrl;
  return {
    title: t(kind === 'photo' ? 'orgPage.pictureTitle' : 'orgPage.backgroundTitle'),
    subtitle: t(kind === 'photo' ? 'orgPage.pictureSubtitle' : 'orgPage.backgroundSubtitle'),
    shape: kind === 'photo' ? 'rounded' : 'rect',
    output: kind === 'photo' ? { width: 512, height: 512 } : { width: 1600, height: 400 },
    previews: kind === 'photo' ? [120, 48, 32] : [320, 160],
    currentUrl: current ? apiUrl(current) : null,
    placeholder: kind === 'photo' ? organizationInitials(organization.name) : '',
    upload: async (blob, name) => onSaved(await api.uploadOrganizationImage(organization.id, kind, blob, name.replace('image.', `${kind}.`))),
    remove: current ? async () => onSaved(await api.removeOrganizationImage(organization.id, kind)) : undefined,
    removeLabel: t(kind === 'photo' ? 'orgPage.removePicture' : 'orgPage.removeBackground'),
    removeHint: t(kind === 'photo' ? 'orgPage.removePictureHint' : 'orgPage.removeBackgroundHint'),
    saveLabel: t('orgPage.saveImage'),
    currentLabel: t('orgPage.currentImage'),
  };
}

/** Arrow keys, Home and End move between the tabs. */
function moveTab(event: KeyboardEvent<HTMLButtonElement>, tabs: Tab[], current: Tab, select: (tab: Tab) => void) {
  const index = tabs.indexOf(current);
  const next = event.key === 'ArrowRight' ? tabs[(index + 1) % tabs.length]
    : event.key === 'ArrowLeft' ? tabs[(index - 1 + tabs.length) % tabs.length]
      : event.key === 'Home' ? tabs[0] : event.key === 'End' ? tabs[tabs.length - 1] : undefined;
  if (!next) return;
  event.preventDefault();
  select(next);
  requestAnimationFrame(() => document.getElementById(`org-tab-${next}`)?.focus());
}

function Glance({ icon, label, value }: { icon: NavIconName; label: string; value: number }) {
  return (
    <li>
      <NavIcon name={icon} size={22} />
      <span className="flex-grow-1">{label}</span>
      <strong>{value}</strong>
    </li>
  );
}

/** The Profile tab's main card: name, picture and background image. */
function ProfileCard({ organization, canManage, onSaved, onEdit }: {
  organization: OrganizationResponse;
  canManage: boolean;
  onSaved: (o: OrganizationResponse) => void;
  onEdit: (kind: api.OrganizationImage) => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState(organization.name);
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);
  const first = useRef(true);
  useEffect(() => {
    if (first.current) {
      first.current = false;
      return;
    }
    setName(organization.name);
  }, [organization.name]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setSaved(false);
    try {
      onSaved(await api.renameOrganization(organization.id, name));
      setSaved(true);
    } catch (err) {
      setError(err);
    }
  };

  const imageRow = (kind: api.OrganizationImage, preview: ReactNode) => (
    <div className="cx-org-image-row">
      {preview}
      {canManage && (
        <button type="button" className="btn btn-outline-secondary cx-icon-button" onClick={() => onEdit(kind)}>
          <NavIcon name="camera" size={18} />
          {t(kind === 'photo' ? 'orgPage.changePicture' : 'orgPage.changeBackground')}
        </button>
      )}
    </div>
  );

  return (
    <section className="cx-card cx-org-profile-card" aria-labelledby="org-profile-title">
      <h2 id="org-profile-title" className="cx-card-title">{t('orgPage.profileTitle')}</h2>
      {error !== null && fieldErrors(error, 'name', t).length === 0 && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {saved && <FormAlert tone="success">{t('orgPage.saved')}</FormAlert>}
      {canManage ? (
        <form onSubmit={(e) => void submit(e)} className="cx-org-name-form" noValidate>
          <Field label={t('organizations.name')} maxLength={100} value={name} onChange={(e) => { setName(e.target.value); setSaved(false); }}
            errors={fieldErrors(error, 'name', t)} className="flex-grow-1 mb-0" />
          <button type="submit" className="btn btn-primary" disabled={!name.trim() || name.trim() === organization.name}>
            {t('orgPage.saveChanges')}
          </button>
        </form>
      ) : (
        <p className="mb-0"><span className="cx-muted d-block small">{t('organizations.name')}</span>{organization.name}</p>
      )}

      <hr className="cx-org-divider" />
      <h3 className="cx-org-subtitle">{t('orgPage.pictureHeading')}</h3>
      <p className="cx-muted">{t('orgPage.pictureHint')}</p>
      {imageRow('photo', <OrganizationMark organization={organization} size="lg" />)}

      <hr className="cx-org-divider" />
      <h3 className="cx-org-subtitle">{t('orgPage.backgroundHeading')}</h3>
      <p className="cx-muted">{t('orgPage.backgroundHint')}</p>
      {imageRow('banner', (
        <span className={`cx-org-banner-preview${organization.bannerUrl ? '' : ' is-empty'}`} aria-hidden="true"
          style={organization.bannerUrl ? { backgroundImage: `url("${apiUrl(organization.bannerUrl)}")` } : undefined} />
      ))}
    </section>
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
