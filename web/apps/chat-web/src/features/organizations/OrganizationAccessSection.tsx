import { useEffect, useState } from 'react';
import type { OrganizationMembersResponse } from '../../shared/api/contracts';
import { FormAlert } from '../../shared/components/Field';
import * as api from './organizationsApi';

export function OrganizationAccessSection({ organizationId, members }: { organizationId: string; members: OrganizationMembersResponse }) {
  const [roles, setRoles] = useState<api.OrganizationAccessRole[]>([]);
  const [groups, setGroups] = useState<api.OrganizationAccessGroup[]>([]);
  const [roleName, setRoleName] = useState('');
  const [permissionEffect, setPermissionEffect] = useState<'allow' | 'deny' | ''>('allow');
  const [supportAccessEffect, setSupportAccessEffect] = useState<'allow' | 'deny' | ''>('');
  const [supportReadEffect, setSupportReadEffect] = useState<'allow' | 'deny' | ''>('');
  const [supportManageEffect, setSupportManageEffect] = useState<'allow' | 'deny' | ''>('');
  const [organizationDiscoverEffect, setOrganizationDiscoverEffect] = useState<'allow' | 'deny' | ''>('');
  const [organizationManageEffect, setOrganizationManageEffect] = useState<'allow' | 'deny' | ''>('');
  const [organizationMembersEffect, setOrganizationMembersEffect] = useState<'allow' | 'deny' | ''>('');
  const [organizationRolesEffect, setOrganizationRolesEffect] = useState<'allow' | 'deny' | ''>('');
  const [organizationWorkspacesEffect, setOrganizationWorkspacesEffect] = useState<'allow' | 'deny' | ''>('');
  const [groupName, setGroupName] = useState('');
  const [selectedRole, setSelectedRole] = useState('');
  const [selectedGroup, setSelectedGroup] = useState('');
  const [selectedMember, setSelectedMember] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const reload = () => Promise.all([api.listOrganizationAccessRoles(organizationId), api.listOrganizationAccessGroups(organizationId)])
    .then(([r, g]) => { setRoles(r); setGroups(g); setSelectedRole((current) => current || r[0]?.id || ''); setSelectedGroup((current) => current || g[0]?.id || ''); }, setError);
  useEffect(() => { void reload(); }, [organizationId]);
  const act = async (work: () => Promise<unknown>) => {
    setBusy(true); setError(null);
    try { await work(); await reload(); }
    catch (e) { setError(e); }
    finally { setBusy(false); }
  };

  return (
    <section className="cx-card cx-org-access" aria-labelledby="org-access-title">
      <h2 id="org-access-title" className="cx-card-title">Organization roles and groups</h2>
      <p className="cx-muted">Users belong to groups; groups receive organization roles. Unconfigured permissions keep the existing policy. An explicit Deny overrides Allows from other group roles.</p>
      {error !== null && <FormAlert tone="danger">{error instanceof Error ? error.message : 'Could not update organization access.'}</FormAlert>}

      <div className="row g-3">
        <div className="col-lg-6">
          <form className="cx-card p-3" onSubmit={(event) => {
            event.preventDefault();
            const permissions = [
              permissionEffect ? { key: 'private_chats.use', effect: permissionEffect } : null,
              supportAccessEffect ? { key: 'support.access', effect: supportAccessEffect } : null,
              supportReadEffect ? { key: 'support.cases.read', effect: supportReadEffect } : null,
              supportManageEffect ? { key: 'support.cases.manage', effect: supportManageEffect } : null,
              organizationDiscoverEffect ? { key: 'organization.discover', effect: organizationDiscoverEffect } : null,
              organizationManageEffect ? { key: 'organization.manage', effect: organizationManageEffect } : null,
              organizationMembersEffect ? { key: 'organization.members.manage', effect: organizationMembersEffect } : null,
              organizationRolesEffect ? { key: 'organization.roles.manage', effect: organizationRolesEffect } : null,
              organizationWorkspacesEffect ? { key: 'organization.workspaces.manage', effect: organizationWorkspacesEffect } : null,
            ].filter((permission): permission is { key: string; effect: 'allow' | 'deny' } => permission !== null);
            void act(() => api.createOrganizationAccessRole(organizationId, roleName, permissions));
            setRoleName('');
          }}>
            <h3 className="h6">Create an organization role</h3>
            <label className="form-label" htmlFor="org-access-role-name">Role name</label>
            <input id="org-access-role-name" className="form-control mb-2" value={roleName} onChange={(event) => setRoleName(event.target.value)} required maxLength={80} />
            <label className="form-label" htmlFor="org-private-chat-effect">Private chats in this organization</label>
            <select id="org-private-chat-effect" className="form-select mb-3" value={permissionEffect} onChange={(event) => setPermissionEffect(event.target.value as 'allow' | 'deny' | '')}>
              <option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option>
            </select>
            <label className="form-label" htmlFor="org-support-access-effect">Support workspace access</label>
            <select id="org-support-access-effect" className="form-select mb-2" value={supportAccessEffect} onChange={(event) => setSupportAccessEffect(event.target.value as 'allow' | 'deny' | '')}>
              <option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option>
            </select>
            <label className="form-label" htmlFor="org-support-read-effect">Read support cases</label>
            <select id="org-support-read-effect" className="form-select mb-2" value={supportReadEffect} onChange={(event) => setSupportReadEffect(event.target.value as 'allow' | 'deny' | '')}>
              <option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option>
            </select>
            <label className="form-label" htmlFor="org-support-manage-effect">Manage support cases</label>
            <select id="org-support-manage-effect" className="form-select mb-3" value={supportManageEffect} onChange={(event) => setSupportManageEffect(event.target.value as 'allow' | 'deny' | '')}>
              <option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option>
            </select>
            <label className="form-label" htmlFor="org-discover-effect">Organization access</label>
            <select id="org-discover-effect" className="form-select mb-2" value={organizationDiscoverEffect} onChange={(event) => setOrganizationDiscoverEffect(event.target.value as 'allow' | 'deny' | '')}>
              <option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option>
            </select>
            <label className="form-label" htmlFor="org-manage-effect">Manage organization settings</label>
            <select id="org-manage-effect" className="form-select mb-2" value={organizationManageEffect} onChange={(event) => setOrganizationManageEffect(event.target.value as 'allow' | 'deny' | '')}>
              <option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option>
            </select>
            <label className="form-label" htmlFor="org-members-manage-effect">Manage members and invitations</label>
            <select id="org-members-manage-effect" className="form-select mb-2" value={organizationMembersEffect} onChange={(event) => setOrganizationMembersEffect(event.target.value as 'allow' | 'deny' | '')}>
              <option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option>
            </select>
            <label className="form-label" htmlFor="org-roles-manage-effect">Manage roles and groups</label>
            <select id="org-roles-manage-effect" className="form-select mb-2" value={organizationRolesEffect} onChange={(event) => setOrganizationRolesEffect(event.target.value as 'allow' | 'deny' | '')}>
              <option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option>
            </select>
            <label className="form-label" htmlFor="org-workspaces-manage-effect">Manage workspaces</label>
            <select id="org-workspaces-manage-effect" className="form-select mb-3" value={organizationWorkspacesEffect} onChange={(event) => setOrganizationWorkspacesEffect(event.target.value as 'allow' | 'deny' | '')}>
              <option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option>
            </select>
            <button type="submit" className="btn btn-primary" disabled={busy || !roleName.trim()}>Create role</button>
          </form>
          <div className="d-grid gap-2 mt-3">
            {roles.map((role) => <article key={role.id} className="cx-admin-role-card">
              <span><strong>{role.name}</strong><span className="cx-muted d-block">{role.permissions.map((permission) => `${permission.key}: ${permission.effect}`).join(', ') || 'No permission decisions'} · {role.groups} groups</span></span>
              <button type="button" className="btn btn-sm btn-outline-danger ms-auto" disabled={busy} onClick={() => void act(() => api.deleteOrganizationAccessRole(organizationId, role.id))}>Delete</button>
            </article>)}
          </div>
        </div>

        <div className="col-lg-6">
          <form className="cx-card p-3" onSubmit={(event) => { event.preventDefault(); void act(() => api.createOrganizationAccessGroup(organizationId, groupName)); setGroupName(''); }}>
            <h3 className="h6">Create a group</h3>
            <label className="form-label" htmlFor="org-access-group-name">Group name</label>
            <input id="org-access-group-name" className="form-control mb-3" value={groupName} onChange={(event) => setGroupName(event.target.value)} required maxLength={80} />
            <button type="submit" className="btn btn-primary" disabled={busy || !groupName.trim()}>Create group</button>
          </form>

          <div className="cx-card p-3 mt-3">
            <h3 className="h6">Assign a role to a group</h3>
            <div className="d-flex gap-2">
              <select className="form-select" aria-label="Role" value={selectedRole} onChange={(event) => setSelectedRole(event.target.value)}>
                {roles.map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}
              </select>
              <select className="form-select" aria-label="Group" value={selectedGroup} onChange={(event) => setSelectedGroup(event.target.value)}>
                {groups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}
              </select>
              <button type="button" className="btn btn-outline-primary" disabled={busy || !selectedRole || !selectedGroup}
                onClick={() => void act(() => api.assignOrganizationRole(organizationId, selectedRole, selectedGroup))}>Assign</button>
            </div>
          </div>

          <div className="cx-card p-3 mt-3">
            <h3 className="h6">Add a member to a group</h3>
            <div className="d-flex gap-2">
              <select className="form-select" aria-label="Member" value={selectedMember} onChange={(event) => setSelectedMember(event.target.value)}>
                <option value="">Select a member</option>
                {members.members.map((member) => <option key={member.user.id} value={member.user.id}>{member.user.displayName}</option>)}
              </select>
              <select className="form-select" aria-label="Group" value={selectedGroup} onChange={(event) => setSelectedGroup(event.target.value)}>
                {groups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}
              </select>
              <button type="button" className="btn btn-outline-primary" disabled={busy || !selectedMember || !selectedGroup}
                onClick={() => void act(() => api.addOrganizationGroupMember(organizationId, selectedGroup, selectedMember))}>Add</button>
            </div>
          </div>

          <div className="d-grid gap-2 mt-3">
            {groups.map((group) => <article key={group.id} className="cx-admin-role-card align-items-start">
              <span className="min-w-0"><strong>{group.name}</strong>
                <span className="cx-muted d-block">Roles: {group.roleIds.length ? group.roleIds.map((id) => {
                  const role = roles.find((item) => item.id === id);
                  return <span key={id} className="cx-admin-pill me-1">{role?.name ?? 'Unknown role'}
                    {role && <button type="button" className="btn-close ms-2" aria-label={`Remove ${role.name} from ${group.name}`}
                      onClick={() => void act(() => api.unassignOrganizationRole(organizationId, role.id, group.id))} />}</span>;
                }) : 'None'}</span>
                <span className="cx-muted d-block">Members: {group.memberIds.length ? group.memberIds.map((id) => {
                  const member = members.members.find((item) => item.user.id === id)?.user;
                  return <span key={id} className="cx-admin-pill me-1">{member?.displayName ?? 'Unknown member'}
                    {member && <button type="button" className="btn-close ms-2" aria-label={`Remove ${member.displayName} from ${group.name}`}
                      onClick={() => void act(() => api.removeOrganizationGroupMember(organizationId, group.id, id))} />}</span>;
                }) : 'None'}</span>
              </span>
              <button type="button" className="btn btn-sm btn-outline-danger ms-auto" disabled={busy} onClick={() => void act(() => api.deleteOrganizationAccessGroup(organizationId, group.id))}>Delete</button>
            </article>)}
          </div>
        </div>
      </div>
    </section>
  );
}
