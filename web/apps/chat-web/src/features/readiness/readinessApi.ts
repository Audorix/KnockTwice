import type { HealthReportResponse } from '../../shared/api/contracts';
import { ApiError, getJson } from '../../shared/api/httpClient';

export type ReadinessResult =
  | { state: 'available'; checkedAt: Date; durationMs: number }
  | { state: 'notReady'; checkedAt: Date; durationMs: number }
  | { state: 'unreachable'; checkedAt: Date };

/**
 * Queries GET /health/ready. 200 = available; 503 = the API answered but is not ready
 * (database or schema); anything else, including network errors and timeouts = unreachable.
 */
export async function checkReadiness(signal?: AbortSignal, language?: string): Promise<ReadinessResult> {
  const options = { acceptStatuses: [503] as const, ...(signal ? { signal } : {}), ...(language ? { language } : {}) };
  try {
    const response = await getJson<HealthReportResponse>('/health/ready', options);
    const checkedAt = new Date();
    if (response.status === 200 && response.body?.status === 'healthy') {
      return { state: 'available', checkedAt, durationMs: response.durationMs };
    }
    if (response.status === 503) {
      return { state: 'notReady', checkedAt, durationMs: response.durationMs };
    }
    return { state: 'unreachable', checkedAt };
  } catch (error) {
    if (error instanceof ApiError) return { state: 'unreachable', checkedAt: new Date() };
    throw error;
  }
}
