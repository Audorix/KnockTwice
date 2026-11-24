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
