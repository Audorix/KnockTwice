import type {
  MyInvitationResponse,
  OrganizationMembersResponse,
  OrganizationResponse,
  OrganizationRole,
  WorkspaceMemberResponse,
  WorkspaceResponse,
} from '../../shared/api/contracts';
import { getJson, sendJson, uploadFile } from '../../shared/api/httpClient';

/** Typed calls to /api/v1/organizations. */

const base = (organizationId: string) => `/api/v1/organizations/${encodeURIComponent(organizationId)}`;

export async function listOrganizations(): Promise<OrganizationResponse[]> {
  return (await getJson<OrganizationResponse[]>('/api/v1/organizations')).body ?? [];
}

export async function createOrganization(name: string): Promise<OrganizationResponse> {
  return (await sendJson<OrganizationResponse>('POST', '/api/v1/organizations', { name }))!;
}

export async function getOrganization(organizationId: string): Promise<OrganizationResponse> {
  return (await getJson<OrganizationResponse>(base(organizationId))).body!;
}

export async function renameOrganization(organizationId: string, name: string): Promise<OrganizationResponse> {
  return (await sendJson<OrganizationResponse>('PUT', base(organizationId), { name }))!;
}

/** The square picture ("photo") or the wide background image ("banner"), already cropped. */
export type OrganizationImage = 'photo' | 'banner';

export function uploadOrganizationImage(organizationId: string, kind: OrganizationImage, image: Blob, fileName: string): Promise<OrganizationResponse> {
  return uploadFile<OrganizationResponse>(`${base(organizationId)}/${kind}`, image, fileName);
}

export async function removeOrganizationImage(organizationId: string, kind: OrganizationImage): Promise<OrganizationResponse> {
  return (await sendJson<OrganizationResponse>('DELETE', `${base(organizationId)}/${kind}`))!;
}

export interface OrganizationDomain {
  id: string; domain: string; txtRecordName: string; txtRecordValue: string;
  verified: boolean; createdAtUtc: string; verifiedAtUtc: string | null;
}

export async function listOrganizationDomains(organizationId: string): Promise<OrganizationDomain[]> {
  return (await getJson<OrganizationDomain[]>(`${base(organizationId)}/domains`)).body ?? [];
}

export async function addOrganizationDomain(organizationId: string, domain: string): Promise<OrganizationDomain> {
  return (await sendJson<OrganizationDomain>('POST', `${base(organizationId)}/domains`, { domain }))!;
}

export async function verifyOrganizationDomain(organizationId: string, domainId: string): Promise<OrganizationDomain> {
  return (await sendJson<OrganizationDomain>('POST', `${base(organizationId)}/domains/${encodeURIComponent(domainId)}/verify`))!;
}

export async function getAutomaticEnrollment(organizationId: string): Promise<boolean> {
  return (await getJson<{ enabled: boolean }>(`${base(organizationId)}/domains/automatic-enrollment`)).body!.enabled;
}

export async function setAutomaticEnrollment(organizationId: string, enabled: boolean): Promise<boolean> {
  return (await sendJson<{ enabled: boolean }>('PUT', `${base(organizationId)}/domains/automatic-enrollment`, { enabled }))!.enabled;
}

export interface OrganizationSso { configured: boolean; issuer: string | null; clientId: string | null; clientSecretConfigured: boolean }
export const getOrganizationSso = async (organizationId: string) =>
  (await getJson<OrganizationSso>(`${base(organizationId)}/sso`)).body!;
export const setOrganizationSso = async (organizationId: string, values: { issuer: string; clientId: string; clientSecret?: string }) =>
  (await sendJson<OrganizationSso>('PUT', `${base(organizationId)}/sso`, values))!;
export const removeOrganizationSso = (organizationId: string) => sendJson('DELETE', `${base(organizationId)}/sso`);

export const removeOrganizationDomain = (organizationId: string, domainId: string) =>
  sendJson('DELETE', `${base(organizationId)}/domains/${encodeURIComponent(domainId)}`);

export async function listMembers(organizationId: string): Promise<OrganizationMembersResponse> {
  return (await getJson<OrganizationMembersResponse>(`${base(organizationId)}/members`)).body!;
}

export const changeRole = (organizationId: string, userId: string, role: OrganizationRole) =>
  sendJson('PUT', `${base(organizationId)}/members/${encodeURIComponent(userId)}`, { role });

export const removeMember = (organizationId: string, userId: string) =>
  sendJson('DELETE', `${base(organizationId)}/members/${encodeURIComponent(userId)}`);

/** Deletes the organization and everything in it (owners; Phase 13). `confirmName` repeats its name. */
export const deleteOrganization = (organizationId: string, confirmName: string) =>
  sendJson('POST', `${base(organizationId)}/delete`, { confirmName });

export const invite = (organizationId: string, email: string, role: OrganizationRole) =>
  sendJson('POST', `${base(organizationId)}/invitations`, { email, role });

export const revokeInvitation = (organizationId: string, invitationId: string) =>
  sendJson('DELETE', `${base(organizationId)}/invitations/${encodeURIComponent(invitationId)}`);

export async function listMyInvitations(): Promise<MyInvitationResponse[]> {
  return (await getJson<MyInvitationResponse[]>('/api/v1/organizations/invitations')).body ?? [];
}

export const acceptInvitation = (invitationId: string) => sendJson('POST', `/api/v1/organizations/invitations/${encodeURIComponent(invitationId)}/accept`);
export const declineInvitation = (invitationId: string) => sendJson('POST', `/api/v1/organizations/invitations/${encodeURIComponent(invitationId)}/decline`);

export async function listWorkspaces(organizationId: string): Promise<WorkspaceResponse[]> {
  return (await getJson<WorkspaceResponse[]>(`${base(organizationId)}/workspaces`)).body ?? [];
}

export const createWorkspace = (organizationId: string, name: string) => sendJson('POST', `${base(organizationId)}/workspaces`, { name });

export async function listWorkspaceMembers(organizationId: string, workspaceId: string): Promise<WorkspaceMemberResponse[]> {
  return (await getJson<WorkspaceMemberResponse[]>(`${base(organizationId)}/workspaces/${encodeURIComponent(workspaceId)}/members`)).body ?? [];
}

export const addWorkspaceMember = (organizationId: string, workspaceId: string, userId: string) =>
  sendJson('POST', `${base(organizationId)}/workspaces/${encodeURIComponent(workspaceId)}/members`, { userId });

export const removeWorkspaceMember = (organizationId: string, workspaceId: string, userId: string) =>
  sendJson('DELETE', `${base(organizationId)}/workspaces/${encodeURIComponent(workspaceId)}/members/${encodeURIComponent(userId)}`);

export interface OrganizationAccessPermission { key: string; effect: 'allow' | 'deny' }
export interface OrganizationAccessRole { id: string; name: string; description: string; scope: string; organizationId: string; workspaceId: string | null; permissions: OrganizationAccessPermission[]; groups: number }
export interface OrganizationAccessGroup { id: string; name: string; description: string; memberIds: string[]; roleIds: string[]; scope: string; organizationId: string; workspaceId: string | null }

const accessBase = (organizationId: string) => `${base(organizationId)}/access-control`;
export async function listOrganizationAccessRoles(organizationId: string): Promise<OrganizationAccessRole[]> {
  return (await getJson<OrganizationAccessRole[]>(`${accessBase(organizationId)}/roles`)).body ?? [];
}
export async function listOrganizationAccessGroups(organizationId: string): Promise<OrganizationAccessGroup[]> {
  return (await getJson<OrganizationAccessGroup[]>(`${accessBase(organizationId)}/groups`)).body ?? [];
}
export const createOrganizationAccessRole = (organizationId: string, name: string, permissions: OrganizationAccessPermission[]) =>
  sendJson<OrganizationAccessRole>('POST', `${accessBase(organizationId)}/roles`, { name, description: '', permissions });
export const deleteOrganizationAccessRole = (organizationId: string, roleId: string) =>
  sendJson('DELETE', `${accessBase(organizationId)}/roles/${encodeURIComponent(roleId)}`);
export const createOrganizationAccessGroup = (organizationId: string, name: string) =>
  sendJson<OrganizationAccessGroup>('POST', `${accessBase(organizationId)}/groups`, { name, description: '' });
export const deleteOrganizationAccessGroup = (organizationId: string, groupId: string) =>
  sendJson('DELETE', `${accessBase(organizationId)}/groups/${encodeURIComponent(groupId)}`);
export const assignOrganizationRole = (organizationId: string, roleId: string, groupId: string) =>
  sendJson('POST', `${accessBase(organizationId)}/roles/${encodeURIComponent(roleId)}/groups/${encodeURIComponent(groupId)}`);
export const unassignOrganizationRole = (organizationId: string, roleId: string, groupId: string) =>
  sendJson('DELETE', `${accessBase(organizationId)}/roles/${encodeURIComponent(roleId)}/groups/${encodeURIComponent(groupId)}`);
export const addOrganizationGroupMember = (organizationId: string, groupId: string, userId: string) =>
  sendJson('POST', `${accessBase(organizationId)}/groups/${encodeURIComponent(groupId)}/members/${encodeURIComponent(userId)}`);
export const removeOrganizationGroupMember = (organizationId: string, groupId: string, userId: string) =>
  sendJson('DELETE', `${accessBase(organizationId)}/groups/${encodeURIComponent(groupId)}/members/${encodeURIComponent(userId)}`);

const workspaceAccessBase = (organizationId: string, workspaceId: string) => `${base(organizationId)}/workspaces/${encodeURIComponent(workspaceId)}/access-control`;
export async function listWorkspaceAccessRoles(organizationId: string, workspaceId: string): Promise<OrganizationAccessRole[]> {
  return (await getJson<OrganizationAccessRole[]>(`${workspaceAccessBase(organizationId, workspaceId)}/roles`)).body ?? [];
}
export async function listWorkspaceAccessGroups(organizationId: string, workspaceId: string): Promise<OrganizationAccessGroup[]> {
  return (await getJson<OrganizationAccessGroup[]>(`${workspaceAccessBase(organizationId, workspaceId)}/groups`)).body ?? [];
}
export const createWorkspaceAccessRole = (organizationId: string, workspaceId: string, name: string, permissions: OrganizationAccessPermission[]) =>
  sendJson<OrganizationAccessRole>('POST', `${workspaceAccessBase(organizationId, workspaceId)}/roles`, { name, description: '', permissions });
export const deleteWorkspaceAccessRole = (organizationId: string, workspaceId: string, roleId: string) =>
  sendJson('DELETE', `${workspaceAccessBase(organizationId, workspaceId)}/roles/${encodeURIComponent(roleId)}`);
export const createWorkspaceAccessGroup = (organizationId: string, workspaceId: string, name: string) =>
  sendJson<OrganizationAccessGroup>('POST', `${workspaceAccessBase(organizationId, workspaceId)}/groups`, { name, description: '' });
export const deleteWorkspaceAccessGroup = (organizationId: string, workspaceId: string, groupId: string) =>
  sendJson('DELETE', `${workspaceAccessBase(organizationId, workspaceId)}/groups/${encodeURIComponent(groupId)}`);
export const assignWorkspaceRole = (organizationId: string, workspaceId: string, roleId: string, groupId: string) =>
  sendJson('POST', `${workspaceAccessBase(organizationId, workspaceId)}/roles/${encodeURIComponent(roleId)}/groups/${encodeURIComponent(groupId)}`);
export const unassignWorkspaceRole = (organizationId: string, workspaceId: string, roleId: string, groupId: string) =>
  sendJson('DELETE', `${workspaceAccessBase(organizationId, workspaceId)}/roles/${encodeURIComponent(roleId)}/groups/${encodeURIComponent(groupId)}`);
export const addWorkspaceGroupMember = (organizationId: string, workspaceId: string, groupId: string, userId: string) =>
  sendJson('POST', `${workspaceAccessBase(organizationId, workspaceId)}/groups/${encodeURIComponent(groupId)}/members/${encodeURIComponent(userId)}`);
export const removeWorkspaceGroupMember = (organizationId: string, workspaceId: string, groupId: string, userId: string) =>
  sendJson('DELETE', `${workspaceAccessBase(organizationId, workspaceId)}/groups/${encodeURIComponent(groupId)}/members/${encodeURIComponent(userId)}`);
