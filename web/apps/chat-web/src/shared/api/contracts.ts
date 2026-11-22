/**
 * TypeScript mirrors of Audorix.ChatX.Contracts wire types. Keep in sync with
 * src/Core/Audorix.ChatX.Contracts (until an OpenAPI-generated client replaces them).
 */

export type HealthStatus = 'healthy' | 'degraded' | 'unhealthy';

export interface HealthCheckEntry {
  name: string;
  status: HealthStatus;
}

/** Body of GET /health/live and GET /health/ready. */
export interface HealthReportResponse {
  status: HealthStatus;
  checks: HealthCheckEntry[];
}

/** RFC 9457 problem document returned for API errors. */
export interface ProblemDocument {
  type?: string;
  title?: string;
  status?: number;
  code: string;
  traceId?: string;
}
