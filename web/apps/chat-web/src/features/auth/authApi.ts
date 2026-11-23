import type {
  AccountDeletionCheckResponse,
  AuthConfigurationResponse,
  CurrentUserResponse,
  SessionResponse,
  UserPreferences,
} from '../../shared/api/contracts';
import { ApiError, getJson, resetCsrfToken, sendJson } from '../../shared/api/httpClient';

/** Typed calls to /api/v1/auth and /api/v1/users/me. */

export async function getAuthConfiguration(): Promise<AuthConfigurationResponse> {
  return (await getJson<AuthConfigurationResponse>('/api/v1/auth/config')).body!;
}

/** The signed-in user, or null when not signed in. */
export async function getCurrentUser(): Promise<CurrentUserResponse | null> {
  try {
    return (await getJson<CurrentUserResponse>('/api/v1/users/me')).body ?? null;
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null;
    throw error;
  }
}

export async function login(email: string, password: string, rememberMe: boolean): Promise<CurrentUserResponse> {
  const user = await sendJson<CurrentUserResponse>('POST', '/api/v1/auth/login', { email, password, rememberMe });
  resetCsrfToken();
  return user!;
}

export async function logout(): Promise<void> {
  try {
    await sendJson('POST', '/api/v1/auth/logout');
  } finally {
    resetCsrfToken();
  }
}

export interface RegistrationInput {
  email: string;
  password: string;
  displayName: string;
  language: string;
  captchaToken: string | null;
}

export async function register(input: RegistrationInput): Promise<void> {
  await sendJson('POST', '/api/v1/auth/register', input);
}

export async function confirmEmail(userId: string, token: string): Promise<void> {
  await sendJson('POST', '/api/v1/auth/confirm-email', { userId, token });
}

export async function resendConfirmation(email: string, captchaToken: string | null): Promise<void> {
  await sendJson('POST', '/api/v1/auth/resend-confirmation', { email, captchaToken });
}

export async function forgotPassword(email: string, captchaToken: string | null): Promise<void> {
  await sendJson('POST', '/api/v1/auth/forgot-password', { email, captchaToken });
}

export async function resetPassword(userId: string, token: string, newPassword: string): Promise<void> {
  await sendJson('POST', '/api/v1/auth/reset-password', { userId, token, newPassword });
}

export async function updateProfile(displayName: string): Promise<CurrentUserResponse> {
  return (await sendJson<CurrentUserResponse>('PUT', '/api/v1/users/me', { displayName }))!;
}

export async function updatePreferences(uiLanguage: string): Promise<CurrentUserResponse> {
  return (await sendJson<CurrentUserResponse>('PUT', '/api/v1/users/me/preferences', { uiLanguage }))!;
}

/** Changes some preferences; omitted ones stay as they are. */
export async function savePreferences(changes: Partial<Omit<UserPreferences, 'uiLanguage'>>): Promise<CurrentUserResponse> {
  return (await sendJson<CurrentUserResponse>('PUT', '/api/v1/users/me/preferences', changes))!;
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  await sendJson('POST', '/api/v1/users/me/password', { currentPassword, newPassword });
}

export async function listSessions(): Promise<SessionResponse[]> {
  return (await getJson<SessionResponse[]>('/api/v1/auth/sessions')).body ?? [];
}

export async function revokeSession(id: string): Promise<void> {
  await sendJson('DELETE', `/api/v1/auth/sessions/${encodeURIComponent(id)}`);
}

export async function revokeOtherSessions(): Promise<number> {
  return (await sendJson<{ revoked: number }>('POST', '/api/v1/auth/sessions/revoke-others'))?.revoked ?? 0;
}

export async function getAccountDeletionCheck(): Promise<AccountDeletionCheckResponse> {
  return (await getJson<AccountDeletionCheckResponse>('/api/v1/users/me/deletion')).body!;
}

/** Deletes the signed-in account (Phase 13); the server signs out everywhere. */
export async function deleteAccount(confirmation: { password?: string; confirmEmail?: string }): Promise<void> {
  try {
    await sendJson('POST', '/api/v1/users/me/deletion', confirmation);
  } finally {
    resetCsrfToken();
  }
}

/** Browser navigation (not fetch) to start an OAuth redirect flow. */
export function externalSignInUrl(provider: string, returnUrl = '/account'): string {
  return `/api/v1/auth/external/${encodeURIComponent(provider)}?returnUrl=${encodeURIComponent(returnUrl)}`;
}
