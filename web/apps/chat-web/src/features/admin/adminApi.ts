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
  /** The person's profile photo, or null when they show initials. */
  photoUrl?: string | null;
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
  /** The organization's picture for administrators (2026-11-24), or null for its initials. */
  photoUrl?: string | null;
}

export interface AdminOrganizationMember {
  userId: string;
  displayName: string;
  initials: string;
  email: string;
  role: 'owner' | 'administrator' | 'member';
  joinedAtUtc: string;
  /** The member's profile photo, or null when they show initials. */
  photoUrl?: string | null;
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

export interface AdminPermissionDefinition {
  key: string;
  area: string;
  scope: 'installation' | 'organization' | 'workspace';
  state: 'implemented' | 'planned';
  description: string;
}

export interface AccessControlRole {
  id: string;
  name: string;
  description: string;
  scope: 'installation';
  permissions: { key: string; effect: 'allow' | 'deny' }[];
  groups: number;
}

export interface AccessControlGroup {
  id: string;
  name: string;
  description: string;
  memberIds: string[];
  roleIds: string[];
}

export interface EffectiveAccessReview {
  userId: string;
  scope: 'installation' | 'organization' | 'workspace';
  organizationId: string | null;
  workspaceId: string | null;
  groups: { id: string; name: string; scope: string; roles: string[] }[];
  permissions: { key: string; outcome: 'allowed' | 'denied' | 'not_granted'; source: string; decisions: string[] }[];
}
export interface EffectiveAccessReviewContext { scope: 'installation' | 'organization' | 'workspace'; organizationId: string | null; workspaceId: string | null; name: string }
export interface StorageMigrationDryRun {
  sourceProvider: string; destinationProvider: string; totalFiles: number; totalBytes: number;
  files: { id: string; fileName: string; size: number; status: string; blockers: string[] }[];
  filesTruncated: boolean; destinationReachable: boolean; capacityAssessment: string; blockers: string[]; warnings: string[];
}
export interface StorageMigrationJob {
  id: string; sourceProvider: string; destinationProvider: string;
  state: 'Queued' | 'Running' | 'Paused' | 'Completed' | 'CompletedWithErrors' | 'CuttingOver' | 'AwaitingRestart' | 'CutoverComplete' | 'RollbackCuttingOver' | 'RollbackAwaitingRestart' | 'RolledBack';
  totalFiles: number; copiedFiles: number; failedFiles: number; skippedFiles: number; pendingFiles: number;
  createdAtUtc: string; updatedAtUtc: string; lastError: string | null;
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
  /** The actor's profile photo, or null when they show initials or are gone. */
  actorPhotoUrl?: string | null;
}

export type SettingKind = 'boolean' | 'integer' | 'bytes' | 'duration' | 'choice' | 'languages' | 'extensions';

export interface RuntimeSetting {
  key: string;
  group: 'access' | 'features' | 'limits' | 'localization' | 'audit' | 'retention';
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
  name: 'email' | 'captcha' | 'storage' | 'scanning' | 'search' | 'queue' | 'ai';
  restartRequired: boolean;
  fields: ProviderField[];
}

export interface AiTranslationProviderProfile {
  id: string;
  name: string;
  provider: 'OpenAICompatible' | 'Ollama' | string;
  endpoint: string;
  model: string;
  hasApiKey: boolean;
  maxOutputTokens: number;
  timeoutSeconds: number;
  active: boolean;
  legacy: boolean;
}

export interface AiTranslationProviderProfileRequest {
  name: string;
  provider: 'OpenAICompatible' | 'Ollama';
  endpoint: string;
  model: string;
  apiKey: string;
  clearApiKey?: boolean;
  maxOutputTokens: number;
  timeoutSeconds: number;
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

/** Defaults (role "all", sort "nameAsc") are left out of the address. */
const optional = (value: string, fallback: string) => (value === fallback ? null : value);

export const searchUsers = async (query: string, filter: string, skip: number, take = 25, role = 'all', sort = 'nameAsc') =>
  (await getJson<Page<AdminUser>>(`/api/v1/admin/users${q({ q: query, filter, role: optional(role, 'all'), sort: optional(sort, 'nameAsc'), skip, take })}`)).body!;

const user = (id: string) => `/api/v1/admin/users/${encodeURIComponent(id)}`;
export const getEffectiveAccessReviewContexts = async (id: string) =>
  (await getJson<EffectiveAccessReviewContext[]>(`${user(id)}/access-review-contexts`)).body ?? [];
export const getEffectiveAccessReview = async (id: string, context: EffectiveAccessReviewContext) =>
  (await getJson<EffectiveAccessReview>(`${user(id)}/access-review${q({ scope: context.scope, organizationId: context.organizationId, workspaceId: context.workspaceId })}`)).body!;
export const disableUser = async (id: string) => (await sendJson<AdminUser>('POST', `${user(id)}/disable`))!;
export const enableUser = async (id: string) => (await sendJson<AdminUser>('POST', `${user(id)}/enable`))!;
export const revokeUserSessions = (id: string) => sendJson<{ revoked: number }>('POST', `${user(id)}/revoke-sessions`);
export const confirmUserEmail = async (id: string) => (await sendJson<AdminUser>('POST', `${user(id)}/confirm-email`))!;
export const resendConfirmation = (id: string) => sendJson('POST', `${user(id)}/resend-confirmation`);
/** Deletes the account (Phase 13): personal data erased, messages kept as "Deleted user". */
export const deleteUser = (id: string) => sendJson('DELETE', user(id));

export const getRoles = async () => (await getJson<AdminRole[]>('/api/v1/admin/roles')).body ?? [];
export const getPermissionCatalog = async () => (await getJson<AdminPermissionDefinition[]>('/api/v1/admin/permissions/catalog')).body ?? [];
export const getAccessRoles = async () => (await getJson<AccessControlRole[]>('/api/v1/admin/access-control/roles')).body ?? [];
export const createAccessRole = async (role: { name: string; description: string; permissions: { key: string; effect: 'allow' | 'deny' }[] }) =>
  (await sendJson<AccessControlRole>('POST', '/api/v1/admin/access-control/roles', role))!;
export const updateAccessRole = async (id: string, role: { name: string; description: string; permissions: { key: string; effect: 'allow' | 'deny' }[] }) =>
  (await sendJson<AccessControlRole>('PUT', `/api/v1/admin/access-control/roles/${encodeURIComponent(id)}`, role))!;
export const getAccessGroups = async () => (await getJson<AccessControlGroup[]>('/api/v1/admin/access-control/groups')).body ?? [];
export const createAccessGroup = async (group: { name: string; description: string }) =>
  (await sendJson<AccessControlGroup>('POST', '/api/v1/admin/access-control/groups', group))!;
export const addAccessGroupMember = async (groupId: string, userId: string) =>
  (await sendJson<AccessControlGroup>('POST', `/api/v1/admin/access-control/groups/${encodeURIComponent(groupId)}/members/${encodeURIComponent(userId)}`))!;
export const removeAccessGroupMember = async (groupId: string, userId: string) =>
  (await sendJson<AccessControlGroup>('DELETE', `/api/v1/admin/access-control/groups/${encodeURIComponent(groupId)}/members/${encodeURIComponent(userId)}`))!;
export const assignAccessRoleToGroup = async (roleId: string, groupId: string) =>
  (await sendJson<AccessControlGroup>('POST', `/api/v1/admin/access-control/roles/${encodeURIComponent(roleId)}/groups/${encodeURIComponent(groupId)}`))!;
export const removeAccessRoleFromGroup = async (roleId: string, groupId: string) =>
  (await sendJson<AccessControlGroup>('DELETE', `/api/v1/admin/access-control/roles/${encodeURIComponent(roleId)}/groups/${encodeURIComponent(groupId)}`))!;

export const searchOrganizations = async (query: string, skip: number, take = 25, sort = 'nameAsc') =>
  (await getJson<Page<AdminOrganization>>(`/api/v1/admin/organizations${q({ q: query, sort: optional(sort, 'nameAsc'), skip, take })}`)).body!;
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
const aiTranslationProvidersPath = '/api/v1/admin/providers/ai/translation';
export const getAiTranslationProviders = async () => (await getJson<AiTranslationProviderProfile[]>(aiTranslationProvidersPath)).body ?? [];
export const createAiTranslationProvider = async (values: AiTranslationProviderProfileRequest) =>
  (await sendJson<AiTranslationProviderProfile>('POST', aiTranslationProvidersPath, values))!;
export const updateAiTranslationProvider = async (id: string, values: AiTranslationProviderProfileRequest) =>
  (await sendJson<AiTranslationProviderProfile>('PUT', `${aiTranslationProvidersPath}/${encodeURIComponent(id)}`, values))!;
export const activateAiTranslationProvider = (id: string) =>
  sendJson('POST', `${aiTranslationProvidersPath}/${encodeURIComponent(id)}/activate`, {});
export const deleteAiTranslationProvider = (id: string) =>
  sendJson('DELETE', `${aiTranslationProvidersPath}/${encodeURIComponent(id)}`);
export const testAiTranslationProvider = async (id: string) =>
  (await sendJson<{ outcome: string }>('POST', `${aiTranslationProvidersPath}/${encodeURIComponent(id)}/test`, {}))!;
export const dryRunStorageMigration = async (sourceProvider: string, destinationProvider: string) =>
  (await sendJson<StorageMigrationDryRun>('POST', '/api/v1/admin/storage-migrations/dry-run', { sourceProvider, destinationProvider }))!;
export const startStorageMigration = async (sourceProvider: string, destinationProvider: string) =>
  (await sendJson<StorageMigrationJob>('POST', '/api/v1/admin/storage-migrations', { sourceProvider, destinationProvider }))!;
export const getStorageMigrations = async () => (await getJson<StorageMigrationJob[]>('/api/v1/admin/storage-migrations')).body!;
export const pauseStorageMigration = async (id: string) => (await sendJson<StorageMigrationJob>('POST', `/api/v1/admin/storage-migrations/${id}/pause`))!;
export const resumeStorageMigration = async (id: string) => (await sendJson<StorageMigrationJob>('POST', `/api/v1/admin/storage-migrations/${id}/resume`))!;
export const cutoverStorageMigration = async (id: string) => (await sendJson<{ jobId: string; state: string; activeProvider: string; referencesMoved: number; restartRequired: boolean }>('POST', `/api/v1/admin/storage-migrations/${id}/cutover`))!;
export const rollbackStorageMigration = async (id: string, reverseCopyJobId: string) => (await sendJson<{ jobId: string; state: string; activeProvider: string; referencesMoved: number; restartRequired: boolean }>('POST', `/api/v1/admin/storage-migrations/${id}/rollback`, { reverseCopyJobId }))!;

export const getSearchStatus = async () =>
  (await getJson<{ provider: string; enabled: boolean; pending: number }>('/api/v1/admin/search')).body!;
/** Empties the search index and indexes every message again. */
export const rebuildSearchIndex = async () => (await sendJson<{ queued: number }>('POST', '/api/v1/admin/search/rebuild'))!;

/** One page of the audit log with its total (redesign 2026-11): search, action prefix, outcome, dates, order. */
export const getAuditPage = async (filters: {
  q: string; action: string; succeeded: boolean | null; from: string | null; to: string | null; order: 'newest' | 'oldest'; skip: number; take: number;
}) =>
  (await getJson<{ items: AuditEntry[]; nextBefore: string | null; total: number | null }>(
    `/api/v1/admin/audit${q({
      q: filters.q, action: filters.action, succeeded: filters.succeeded, from: filters.from, to: filters.to,
      order: filters.order === 'oldest' ? 'oldest' : null, skip: filters.skip, limit: filters.take,
    })}`,
  )).body!;
