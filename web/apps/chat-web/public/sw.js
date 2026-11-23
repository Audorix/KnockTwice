/*
 * Audorix.ChatX service worker (Phase 15): shows Web Push notifications while ChatX is closed and
 * opens the conversation when one is clicked. It caches nothing and handles no requests.
 * The payload is { title, body, url, tag }, encrypted by the server for this browser only.
 */
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

self.addEventListener('push', (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = {};
  }

  const title = typeof data.title === 'string' && data.title ? data.title : 'Audorix.ChatX';
  event.waitUntil(
    self.registration.showNotification(title, {
      body: typeof data.body === 'string' ? data.body : '',
      tag: typeof data.tag === 'string' ? data.tag : undefined,
      renotify: true,
      icon: '/favicon.svg',
      badge: '/favicon.svg',
      data: { url: typeof data.url === 'string' && data.url.startsWith('/') ? data.url : '/conversations' },
    }),
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const url = new URL(event.notification.data?.url || '/conversations', self.location.origin).href;
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windows) => {
      for (const client of windows) {
        if (new URL(client.url).origin === self.location.origin && 'focus' in client) {
          client.navigate(url).catch(() => undefined);
          return client.focus();
        }
      }
      return self.clients.openWindow(url);
    }),
  );
});
