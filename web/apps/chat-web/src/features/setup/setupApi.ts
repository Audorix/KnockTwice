import { getJson, postJson } from '../../shared/api/httpClient';
import type {
  SetupCompleteRequest,
  SetupCompleteResponse,
  SetupConfigurationResponse,
  SetupDatabaseInput,
  SetupDatabaseTestResponse,
  SetupEmailInput,
  SetupStatusResponse,
} from '../../shared/api/contracts';

/** Setup wizard API. Every call except status carries the setup code from the server log. */

const tokenHeader = 'X-Setup-Token';
const auth = (token: string) => ({ headers: { [tokenHeader]: token } });

export async function getSetupStatus(signal?: AbortSignal): Promise<SetupStatusResponse> {
  return (await getJson<SetupStatusResponse>('/api/v1/setup/status', signal ? { signal } : {})).body!;
}

export async function verifySetupCode(token: string): Promise<void> {
  await postJson('/api/v1/setup/token', {}, auth(token));
}

export async function getSetupConfiguration(token: string): Promise<SetupConfigurationResponse> {
  return (await getJson<SetupConfigurationResponse>('/api/v1/setup/configuration', auth(token))).body!;
}

export async function testDatabase(token: string, database: SetupDatabaseInput): Promise<SetupDatabaseTestResponse> {
  // A PostgreSQL probe may wait for its own 5-second connection timeout twice.
  return (await postJson<SetupDatabaseTestResponse>('/api/v1/setup/database/test', database, { ...auth(token), timeoutMs: 20000 }))!;
}

export async function sendTestEmail(token: string, email: SetupEmailInput, to: string): Promise<void> {
  await postJson('/api/v1/setup/email/test', { email, to }, { ...auth(token), timeoutMs: 45000 });
}

export async function completeSetup(token: string, request: SetupCompleteRequest): Promise<SetupCompleteResponse> {
  // Migrating a new database can take a while.
  return (await postJson<SetupCompleteResponse>('/api/v1/setup/complete', request, { ...auth(token), timeoutMs: 120000 }))!;
}
