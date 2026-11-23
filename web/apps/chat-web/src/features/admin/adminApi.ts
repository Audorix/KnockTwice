import { getJson, sendJson } from '../../shared/api/httpClient';

/** Typed calls to /api/v1/admin (Phase 9). Each one needs its own permission; the server checks it. */

export interface AdminUser {
  id: string;
  email: string;
  displayName: string;
  initials: string;
  emailConfirmed: boolean;
  disabled: boolean;
  lockedOut: boolean;
  roles: string[];
  createdAtUtc: string;
  lastSignInAtUtc: string | null;
  activeSessions: number;
  organizations: number;
}

export interface Page<T> {
  items: T[];
  total: number;
}

export interface AdminOrganization {
  id: string;
  name: string;
  createdAtUtc: string;
  members: number;
  owners: number;
}

export interface AdminOrganizationMember {
  userId: string;
  displayName: string;
  initials: string;
  email: string;
  role: 'owner' | 'administrator' | 'member';
  joinedAtUtc: string;
}

export interface AdminOrganizationDetail {
  organization: AdminOrganization;
  members: AdminOrganizationMember[];
}

export interface AdminRole {
  name: string;
  permissions: string[];
  members: number;
}

export interface AuditEntry {
  id: string;
  occurredAtUtc: string;
  action: string;
  succeeded: boolean;
  actorUserId: string | null;
  actorName: string | null;
  targetType: string | null;
  targetId: string | null;
  ipAddress: string | null;
  details: string | null;
}

export type SettingKind = 'boolean' | 'integer' | 'bytes' | 'duration' | 'choice' | 'languages' | 'extensions';

export interface RuntimeSetting {
  key: string;
  group: 'access' | 'features' | 'limits' | 'localization' | 'audit';
  kind: SettingKind;
  value: boolean | number | string | string[] | null;
  source: string;
  locked: boolean;
  min: number | null;
  max: number | null;
  choices: string[] | null;
}

export interface ProviderField {
  key: string;
  kind: 'text' | 'secret' | 'integer' | 'boolean' | 'choice';
  value: string | number | boolean | null;
  hasValue: boolean;
  source: string;
  locked: boolean;
  choices: string[] | null;
}

export interface ProviderGroup {
  name: 'email' | 'captcha' | 'storage' | 'scanning';
  restartRequired: boolean;
  fields: ProviderField[];
}

export interface AdminOverview {
  installationId: string | null;
  version: string;
  environment: string;
  users: number;
  disabledUsers: number;
  organizations: number;
  databaseProvider: string;
  storageProvider: string;
  scanningEnabled: boolean;
  registrationMode: string;
  restartRequiredFor: string[];
}

const q = (params: Record<string, string | number | boolean | null | undefined>) => {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) if (value !== null && value !== undefined && value !== '') search.set(key, String(value));
  const text = search.toString();
  return text ? `?${text}` : '';
};

export const getOverview = async () => (await getJson<AdminOverview>('/api/v1/admin/overview')).body!;

export const searchUsers = async (query: string, filter: string, skip: number, take = 25) =>
  (await getJson<Page<AdminUser>>(`/api/v1/admin/users${q({ q: query, filter, skip, take })}`)).body!;

const user = (id: string) => `/api/v1/admin/users/${encodeURIComponent(id)}`;
export const disableUser = async (id: string) => (await sendJson<AdminUser>('POST', `${user(id)}/disable`))!;
export const enableUser = async (id: string) => (await sendJson<AdminUser>('POST', `${user(id)}/enable`))!;
export const revokeUserSessions = (id: string) => sendJson<{ revoked: number }>('POST', `${user(id)}/revoke-sessions`);
export const confirmUserEmail = async (id: string) => (await sendJson<AdminUser>('POST', `${user(id)}/confirm-email`))!;
export const resendConfirmation = (id: string) => sendJson('POST', `${user(id)}/resend-confirmation`);
export const grantRole = async (id: string, role: string) => (await sendJson<AdminUser>('POST', `${user(id)}/roles`, { role }))!;
export const revokeRole = async (id: string, role: string) => (await sendJson<AdminUser>('DELETE', `${user(id)}/roles/${encodeURIComponent(role)}`))!;
/** Deletes the account (Phase 13): personal data erased, messages kept as "Deleted user". */
export const deleteUser = (id: string) => sendJson('DELETE', user(id));

export const getRoles = async () => (await getJson<AdminRole[]>('/api/v1/admin/roles')).body ?? [];

export const searchOrganizations = async (query: string, skip: number, take = 25) =>
  (await getJson<Page<AdminOrganization>>(`/api/v1/admin/organizations${q({ q: query, skip, take })}`)).body!;
const organization = (id: string) => `/api/v1/admin/organizations/${encodeURIComponent(id)}`;
export const getOrganization = async (id: string) => (await getJson<AdminOrganizationDetail>(organization(id))).body!;
export const renameOrganization = async (id: string, name: string) => (await sendJson<AdminOrganizationDetail>('PUT', organization(id), { name }))!;
export const setMemberRole = async (id: string, userId: string, role: string) =>
  (await sendJson<AdminOrganizationDetail>('PUT', `${organization(id)}/members/${encodeURIComponent(userId)}`, { role }))!;
export const removeMember = (id: string, userId: string) => sendJson('DELETE', `${organization(id)}/members/${encodeURIComponent(userId)}`);
/** Deletes the organization and everything in it; `confirmName` repeats its name. */
export const deleteOrganization = (id: string, confirmName: string) => sendJson('POST', `${organization(id)}/delete`, { confirmName });

export const getSettings = async () => (await getJson<RuntimeSetting[]>('/api/v1/admin/settings')).body ?? [];
export const saveSettings = async (values: Record<string, unknown>) => (await sendJson<RuntimeSetting[]>('PUT', '/api/v1/admin/settings', { values }))!;

export const getProviders = async () => (await getJson<ProviderGroup[]>('/api/v1/admin/providers')).body ?? [];
export const saveProvider = async (group: string, values: Record<string, unknown>) =>
  (await sendJson<ProviderGroup>('PUT', `/api/v1/admin/providers/${encodeURIComponent(group)}`, { values }))!;
export const testProvider = async (group: string, values: Record<string, unknown>) =>
  (await sendJson<{ outcome: string }>('POST', `/api/v1/admin/providers/${encodeURIComponent(group)}/test`, { values }))!;

export const getSearchStatus = async () =>
  (await getJson<{ provider: string; enabled: boolean; pending: number }>('/api/v1/admin/search')).body!;
/** Empties the search index and indexes every message again. */
export const rebuildSearchIndex = async () => (await sendJson<{ queued: number }>('POST', '/api/v1/admin/search/rebuild'))!;

export const getAudit = async (filters: { action?: string; succeeded?: boolean | null; before?: string | null }) =>
  (await getJson<{ items: AuditEntry[]; nextBefore: string | null }>(
    `/api/v1/admin/audit${q({ action: filters.action, succeeded: filters.succeeded ?? null, before: filters.before ?? null, limit: 50 })}`,
  )).body!;
