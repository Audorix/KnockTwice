import type {
  ChannelDetailResponse,
  ChannelResponse,
  ChannelVisibility,
  GuestInvitationResponse,
  GuestResponse,
  MyGuestInvitationResponse,
  OrganizationMemberResponse,
  OrganizationResponse,
  SupportConversationResponse,
  SupportStatus,
  TeamMemberResponse,
  TeamResponse,
} from '../../shared/api/contracts';
import { getJson, sendJson } from '../../shared/api/httpClient';

/** Typed calls for channels, teams, guests, support and organization settings (Phase 11). */

const id = encodeURIComponent;
const org = (organizationId: string) => `/api/v1/organizations/${id(organizationId)}`;
const ws = (organizationId: string, workspaceId: string) => `${org(organizationId)}/workspaces/${id(workspaceId)}`;

// ---- Channels -------------------------------------------------------------------------------

export async function listChannels(organizationId: string, workspaceId: string): Promise<ChannelResponse[]> {
  return (await getJson<ChannelResponse[]>(`${ws(organizationId, workspaceId)}/channels`)).body ?? [];
}

export async function createChannel(
  organizationId: string,
  workspaceId: string,
  body: { name: string; description?: string; visibility: ChannelVisibility; userIds?: string[]; teamIds?: string[] },
): Promise<ChannelDetailResponse> {
  return (await sendJson<ChannelDetailResponse>('POST', `${ws(organizationId, workspaceId)}/channels`, body))!;
}

export async function getChannel(channelId: string): Promise<ChannelDetailResponse> {
  return (await getJson<ChannelDetailResponse>(`/api/v1/channels/${id(channelId)}`)).body!;
}

export async function updateChannel(channelId: string, body: { name?: string; description?: string; visibility?: ChannelVisibility }): Promise<ChannelDetailResponse> {
  return (await sendJson<ChannelDetailResponse>('PUT', `/api/v1/channels/${id(channelId)}`, body))!;
}

export async function joinChannel(channelId: string): Promise<ChannelDetailResponse> {
  return (await sendJson<ChannelDetailResponse>('POST', `/api/v1/channels/${id(channelId)}/join`))!;
}

export const leaveChannel = (channelId: string) => sendJson('POST', `/api/v1/channels/${id(channelId)}/leave`);

export async function addChannelMembers(channelId: string, body: { userIds?: string[]; teamIds?: string[] }): Promise<ChannelDetailResponse> {
  return (await sendJson<ChannelDetailResponse>('POST', `/api/v1/channels/${id(channelId)}/members`, body))!;
}

export const removeChannelMember = (channelId: string, userId: string) => sendJson('DELETE', `/api/v1/channels/${id(channelId)}/members/${id(userId)}`);

export async function setChannelManager(channelId: string, userId: string, manager: boolean): Promise<ChannelDetailResponse> {
  return (await sendJson<ChannelDetailResponse>('PUT', `/api/v1/channels/${id(channelId)}/members/${id(userId)}`, { manager }))!;
}

export async function unlinkTeam(channelId: string, teamId: string): Promise<ChannelDetailResponse> {
  return (await sendJson<ChannelDetailResponse>('DELETE', `/api/v1/channels/${id(channelId)}/teams/${id(teamId)}`))!;
}

// ---- Teams ----------------------------------------------------------------------------------

export async function listTeams(organizationId: string, workspaceId: string): Promise<TeamResponse[]> {
  return (await getJson<TeamResponse[]>(`${ws(organizationId, workspaceId)}/teams`)).body ?? [];
}

export const createTeam = (organizationId: string, workspaceId: string, name: string, description?: string) =>
  sendJson<TeamResponse>('POST', `${ws(organizationId, workspaceId)}/teams`, { name, description });

export const deleteTeam = (organizationId: string, workspaceId: string, teamId: string) =>
  sendJson('DELETE', `${ws(organizationId, workspaceId)}/teams/${id(teamId)}`);

export async function listTeamMembers(organizationId: string, workspaceId: string, teamId: string): Promise<TeamMemberResponse[]> {
  return (await getJson<TeamMemberResponse[]>(`${ws(organizationId, workspaceId)}/teams/${id(teamId)}/members`)).body ?? [];
}

export const addTeamMember = (organizationId: string, workspaceId: string, teamId: string, userId: string, role: 'manager' | 'member') =>
  sendJson('POST', `${ws(organizationId, workspaceId)}/teams/${id(teamId)}/members`, { userId, role });

export const changeTeamRole = (organizationId: string, workspaceId: string, teamId: string, userId: string, role: 'manager' | 'member') =>
  sendJson('PUT', `${ws(organizationId, workspaceId)}/teams/${id(teamId)}/members/${id(userId)}`, { role });

export const removeTeamMember = (organizationId: string, workspaceId: string, teamId: string, userId: string) =>
  sendJson('DELETE', `${ws(organizationId, workspaceId)}/teams/${id(teamId)}/members/${id(userId)}`);

// ---- Organization settings, support agents and workspaces ----------------------------------------

export async function updateOrganizationSettings(
  organizationId: string,
  body: { guestsEnabled?: boolean; channelCreation?: 'members' | 'administrators' },
): Promise<OrganizationResponse> {
  return (await sendJson<OrganizationResponse>('PUT', `${org(organizationId)}/settings`, body))!;
}

export const setSupportAgent = (organizationId: string, userId: string, supportAgent: boolean) =>
  sendJson<OrganizationMemberResponse>('PUT', `${org(organizationId)}/members/${id(userId)}/support-agent`, { supportAgent });

export const renameWorkspace = (organizationId: string, workspaceId: string, name: string) => sendJson('PUT', ws(organizationId, workspaceId), { name });

// ---- Guests -----------------------------------------------------------------------------------

export async function listGuests(organizationId: string): Promise<GuestResponse[]> {
  return (await getJson<GuestResponse[]>(`${org(organizationId)}/guests`)).body ?? [];
}

export const removeGuest = (organizationId: string, userId: string) => sendJson('DELETE', `${org(organizationId)}/guests/${id(userId)}`);

export async function listGuestInvitations(organizationId: string): Promise<GuestInvitationResponse[]> {
  return (await getJson<GuestInvitationResponse[]>(`${org(organizationId)}/guests/invitations`)).body ?? [];
}

export const inviteGuest = (organizationId: string, channelId: string, email: string) =>
  sendJson('POST', `${org(organizationId)}/guests/invitations`, { channelId, email });

export const revokeGuestInvitation = (organizationId: string, invitationId: string) =>
  sendJson('DELETE', `${org(organizationId)}/guests/invitations/${id(invitationId)}`);

export async function listMyGuestInvitations(): Promise<MyGuestInvitationResponse[]> {
  return (await getJson<MyGuestInvitationResponse[]>('/api/v1/guest-invitations')).body ?? [];
}

export async function acceptGuestInvitation(invitationId: string): Promise<string> {
  return (await sendJson<{ conversationId: string }>('POST', `/api/v1/guest-invitations/${id(invitationId)}/accept`))!.conversationId;
}

export const declineGuestInvitation = (invitationId: string) => sendJson('POST', `/api/v1/guest-invitations/${id(invitationId)}/decline`);

// ---- Support ------------------------------------------------------------------------------------

export async function listSupportInbox(organizationId: string, status: SupportStatus | 'all', mine: boolean): Promise<SupportConversationResponse[]> {
  const query = new URLSearchParams({ status });
  if (mine) query.set('mine', 'true');
  return (await getJson<SupportConversationResponse[]>(`${org(organizationId)}/support?${query.toString()}`)).body ?? [];
}

export async function claimSupportConversation(conversationId: string): Promise<SupportConversationResponse> {
  return (await sendJson<SupportConversationResponse>('POST', `/api/v1/support/conversations/${id(conversationId)}/claim`))!;
}

export async function assignSupportConversation(conversationId: string, assigneeId: string | null): Promise<SupportConversationResponse> {
  return (await sendJson<SupportConversationResponse>('PUT', `/api/v1/support/conversations/${id(conversationId)}/assignee`, { assigneeId }))!;
}

export async function setSupportStatus(conversationId: string, status: SupportStatus): Promise<SupportConversationResponse> {
  return (await sendJson<SupportConversationResponse>('PUT', `/api/v1/support/conversations/${id(conversationId)}/status`, { status }))!;
}
