import { getJson, sendJson } from '../../shared/api/httpClient';

/** Web Push in this browser (Phase 15): the service worker shows notifications while ChatX is closed. */

export interface PushConfiguration {
  enabled: boolean;
  publicKey: string | null;
}

export function pushSupported(): boolean {
  return typeof window !== 'undefined' && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window && window.isSecureContext;
}

export async function getPushConfiguration(): Promise<PushConfiguration> {
  return (await getJson<PushConfiguration>('/api/v1/notifications/push')).body ?? { enabled: false, publicKey: null };
}

async function registration(): Promise<ServiceWorkerRegistration> {
  return navigator.serviceWorker.register('/sw.js', { scope: '/' });
}

/** The current subscription of this browser, if any. */
export async function currentSubscription(): Promise<PushSubscription | null> {
  if (!pushSupported()) return null;
  const existing = await navigator.serviceWorker.getRegistration('/');
  return (await existing?.pushManager.getSubscription()) ?? null;
}

/** Asks for permission, subscribes this browser and registers it with the server. */
export async function enablePush(publicKey: string): Promise<'enabled' | 'blocked'> {
  const permission = Notification.permission === 'default' ? await Notification.requestPermission() : Notification.permission;
  if (permission !== 'granted') return 'blocked';
  const worker = await registration();
  await navigator.serviceWorker.ready;
  const subscription =
    (await worker.pushManager.getSubscription()) ??
    (await worker.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(publicKey) }));
  await sendJson('POST', '/api/v1/notifications/push/subscriptions', subscription.toJSON());
  return 'enabled';
}

/** Stops push to this browser (server first, then the browser's subscription). Never throws. */
export async function disablePush(): Promise<void> {
  try {
    const subscription = await currentSubscription();
    if (!subscription) return;
    await sendJson('POST', '/api/v1/notifications/push/subscriptions/remove', { endpoint: subscription.endpoint }).catch(() => undefined);
    await subscription.unsubscribe();
  } catch {
    // Best effort: the server also drops subscriptions the push service reports gone.
  }
}

function keyBytes(base64Url: string): Uint8Array<ArrayBuffer> {
  const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(base64Url.length / 4) * 4, '=');
  const raw = atob(base64);
  const bytes = new Uint8Array(new ArrayBuffer(raw.length));
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  return bytes;
}
