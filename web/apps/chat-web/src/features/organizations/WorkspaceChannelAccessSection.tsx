import { useEffect, useState } from 'react';
import type { WorkspaceMemberResponse } from '../../shared/api/contracts';
import { FormAlert } from '../../shared/components/Field';
import * as api from './organizationsApi';

export function WorkspaceChannelAccessSection({ organizationId, workspaceId, members, canManage }: {
  organizationId: string; workspaceId: string; members: WorkspaceMemberResponse[]; canManage: boolean;
}) {
  const [roles, setRoles] = useState<api.OrganizationAccessRole[]>([]);
  const [groups, setGroups] = useState<api.OrganizationAccessGroup[]>([]);
  const [name, setName] = useState('');
  const [groupName, setGroupName] = useState('');
  const [createEffect, setCreateEffect] = useState<'allow' | 'deny' | ''>('');
  const [manageEffect, setManageEffect] = useState<'allow' | 'deny' | ''>('');
  const [selectedRole, setSelectedRole] = useState('');
  const [selectedGroup, setSelectedGroup] = useState('');
  const [selectedMember, setSelectedMember] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const reload = () => Promise.all([api.listWorkspaceAccessRoles(organizationId, workspaceId), api.listWorkspaceAccessGroups(organizationId, workspaceId)])
    .then(([r, g]) => { setRoles(r); setGroups(g); setSelectedRole((v) => v || r[0]?.id || ''); setSelectedGroup((v) => v || g[0]?.id || ''); }, setError);
  useEffect(() => { void reload(); }, [organizationId, workspaceId]);
  const act = async (work: () => Promise<unknown>) => {
    setBusy(true); setError(null);
    try { await work(); await reload(); } catch (e) { setError(e); } finally { setBusy(false); }
  };
  if (!canManage) return null;
  return <section className="cx-card p-3 mt-3" aria-labelledby={`workspace-access-${workspaceId}`}>
    <h3 id={`workspace-access-${workspaceId}`} className="h6">Channel permissions</h3>
    <p className="cx-muted">Workspace groups receive roles. Unconfigured permissions keep the current channel policy; Deny overrides Allow.</p>
    {error !== null && <FormAlert tone="danger">{error instanceof Error ? error.message : 'Could not update workspace access.'}</FormAlert>}
    <form className="row g-2 align-items-end" onSubmit={(event) => {
      event.preventDefault();
      const permissions = [createEffect && { key: 'channels.create', effect: createEffect }, manageEffect && { key: 'channels.manage', effect: manageEffect }]
        .filter((p): p is { key: string; effect: 'allow' | 'deny' } => Boolean(p));
      void act(() => api.createWorkspaceAccessRole(organizationId, workspaceId, name, permissions)); setName('');
    }}>
      <div className="col-md-4"><label className="form-label" htmlFor={`channel-role-${workspaceId}`}>Role name</label><input id={`channel-role-${workspaceId}`} className="form-control" value={name} onChange={(e) => setName(e.target.value)} required maxLength={80} /></div>
      <div className="col-md-3"><label className="form-label" htmlFor={`channel-create-${workspaceId}`}>Create channels</label><select id={`channel-create-${workspaceId}`} className="form-select" value={createEffect} onChange={(e) => setCreateEffect(e.target.value as 'allow' | 'deny' | '')}><option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option></select></div>
      <div className="col-md-3"><label className="form-label" htmlFor={`channel-manage-${workspaceId}`}>Manage channels</label><select id={`channel-manage-${workspaceId}`} className="form-select" value={manageEffect} onChange={(e) => setManageEffect(e.target.value as 'allow' | 'deny' | '')}><option value="">Unassigned</option><option value="allow">Allow</option><option value="deny">Deny</option></select></div>
      <div className="col-md-2"><button className="btn btn-primary" disabled={busy || !name.trim()}>Create role</button></div>
    </form>
    <div className="row g-2 mt-2">
      <div className="col-md-4"><form className="d-flex gap-2" onSubmit={(e) => { e.preventDefault(); void act(() => api.createWorkspaceAccessGroup(organizationId, workspaceId, groupName)); setGroupName(''); }}><input className="form-control" aria-label="New workspace group" placeholder="New group name" value={groupName} onChange={(e) => setGroupName(e.target.value)} required maxLength={80} /><button className="btn btn-outline-primary" disabled={busy || !groupName.trim()}>Create group</button></form></div>
      <div className="col-md-4 d-flex gap-2"><select className="form-select" aria-label="Workspace role" value={selectedRole} onChange={(e) => setSelectedRole(e.target.value)}><option value="">Choose role</option>{roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}</select><select className="form-select" aria-label="Workspace group" value={selectedGroup} onChange={(e) => setSelectedGroup(e.target.value)}><option value="">Choose group</option>{groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}</select><button className="btn btn-outline-primary" disabled={busy || !selectedRole || !selectedGroup} onClick={() => void act(() => api.assignWorkspaceRole(organizationId, workspaceId, selectedRole, selectedGroup))}>Assign</button></div>
      <div className="col-md-4 d-flex gap-2"><select className="form-select" aria-label="Workspace member" value={selectedMember} onChange={(e) => setSelectedMember(e.target.value)}><option value="">Choose member</option>{members.map((m) => <option key={m.user.id} value={m.user.id}>{m.user.displayName}</option>)}</select><select className="form-select" aria-label="Workspace group for member" value={selectedGroup} onChange={(e) => setSelectedGroup(e.target.value)}><option value="">Choose group</option>{groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}</select><button className="btn btn-outline-primary" disabled={busy || !selectedMember || !selectedGroup} onClick={() => void act(() => api.addWorkspaceGroupMember(organizationId, workspaceId, selectedGroup, selectedMember))}>Add</button></div>
    </div>
    <div className="d-grid gap-2 mt-3">{roles.map((role) => <article key={role.id} className="cx-admin-role-card"><span><strong>{role.name}</strong><span className="cx-muted d-block">{role.permissions.map((p) => `${p.key}: ${p.effect}`).join(', ') || 'No permission decisions'}</span></span><button type="button" className="btn btn-sm btn-outline-danger ms-auto" disabled={busy} onClick={() => void act(() => api.deleteWorkspaceAccessRole(organizationId, workspaceId, role.id))}>Delete</button></article>)}
      {groups.map((group) => <article key={group.id} className="cx-admin-role-card"><span><strong>{group.name}</strong><span className="cx-muted d-block">Roles: {group.roleIds.map((id) => roles.find((r) => r.id === id)?.name ?? 'Unknown role').join(', ') || 'None'} · Members: {group.memberIds.map((id) => members.find((m) => m.user.id === id)?.user.displayName ?? 'Unknown member').join(', ') || 'None'}</span></span><button type="button" className="btn btn-sm btn-outline-danger ms-auto" disabled={busy} onClick={() => void act(() => api.deleteWorkspaceAccessGroup(organizationId, workspaceId, group.id))}>Delete</button></article>)}</div>
  </section>;
}
