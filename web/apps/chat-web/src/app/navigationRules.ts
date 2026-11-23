import type { OrganizationResponse } from '../shared/api/contracts';

/**
 * Rules the navigation needs without loading the pages themselves (Phase 22: pages are loaded on
 * demand, so the shell must not import them).
 */

/** Permissions that open at least one administration tab. */
export const adminPermissions = ['installation.read', 'users.read', 'organizations.read', 'audit.read'] as const;

/** Whether the user may open the administration at all. */
export function canAdminister(permissions: readonly string[] | undefined): boolean {
  return adminPermissions.some((p) => permissions?.includes(p));
}

/** Organizations whose support inbox the user works (support agent, or organization manager). */
export function supportOrganizations(organizations: OrganizationResponse[]): OrganizationResponse[] {
  return organizations.filter((o) => o.supportAgent || o.myPermissions.includes('organization.manage'));
}
