import type { UpdateWidgetRequest, WidgetResponse, WidgetSecretResponse } from '../../shared/api/contracts';
import { getJson, sendJson } from '../../shared/api/httpClient';

/** Typed calls to /api/v1/organizations/{id}/widgets (owners and administrators). */

const base = (organizationId: string) => `/api/v1/organizations/${encodeURIComponent(organizationId)}/widgets`;

export async function listWidgets(organizationId: string): Promise<WidgetResponse[]> {
  return (await getJson<WidgetResponse[]>(base(organizationId))).body ?? [];
}

export async function createWidget(organizationId: string, name: string): Promise<WidgetSecretResponse> {
  return (await sendJson<WidgetSecretResponse>('POST', base(organizationId), { name }))!;
}

export async function updateWidget(organizationId: string, widgetId: string, changes: UpdateWidgetRequest): Promise<WidgetResponse> {
  return (await sendJson<WidgetResponse>('PUT', `${base(organizationId)}/${encodeURIComponent(widgetId)}`, changes))!;
}

export async function rotateSecret(organizationId: string, widgetId: string): Promise<WidgetSecretResponse> {
  return (await sendJson<WidgetSecretResponse>('POST', `${base(organizationId)}/${encodeURIComponent(widgetId)}/secret`))!;
}

/** The snippet a website adds to embed the widget (the loader is served by this installation). */
export function embedSnippet(origin: string, widgetId: string): string {
  return `<script src="${origin}/widget.js" data-widget-id="${widgetId}" async></script>`;
}
