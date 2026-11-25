// Example website that embeds an Knock Twice website chat (no dependencies; Node.js 22+).
//
//   CHATX_URL=https://chat.example.org CHATX_WIDGET_ID=<widget id> CHATX_WIDGET_SECRET=<signing secret> node server.mjs
//
// It serves a page on http://localhost:3000 with a pretend sign-in. Signed-in "customers" are
// identified to the chat with a token this server signs (HS256, the widget's secret); anonymous
// visitors chat without one if the widget allows it. The secret never leaves this server.
import { createServer } from 'node:http';
import { createHmac, randomUUID } from 'node:crypto';

const port = Number(process.env.PORT ?? 3000);
const chatUrl = (process.env.CHATX_URL ?? '').replace(/\/$/, '');
const widgetId = process.env.CHATX_WIDGET_ID ?? '';
const secret = process.env.CHATX_WIDGET_SECRET ?? '';
if (!chatUrl || !widgetId) {
  console.error('Set CHATX_URL and CHATX_WIDGET_ID (and CHATX_WIDGET_SECRET to identify signed-in visitors).');
  process.exit(1);
}

// A real site has its own accounts; here a cookie holds the pretend customer's name.
const sessions = new Map();

const base64url = (value) => Buffer.from(value).toString('base64url');

/** An identity token as Knock Twice expects it: HS256 JWT, aud = widget id, short-lived. */
function identityToken(customer) {
  const now = Math.floor(Date.now() / 1000);
  const header = base64url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const payload = base64url(JSON.stringify({
    sub: customer.id, // your stable user id: the same id continues the same conversation
    name: customer.name,
    email: customer.email,
    aud: widgetId,
    iat: now,
    exp: now + 10 * 60, // at most 1 hour (Widgets:IdentityTokenMaxLifetime)
  }));
  const signature = createHmac('sha256', secret).update(`${header}.${payload}`).digest('base64url');
  return `${header}.${payload}.${signature}`;
}

const escape = (text) => String(text).replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);

function page(customer) {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Example shop</title>
  <style>
    body { font-family: system-ui, sans-serif; margin: 2rem auto; max-width: 42rem; padding: 0 1rem; line-height: 1.5; }
    form { display: flex; gap: .5rem; flex-wrap: wrap; }
  </style>
</head>
<body>
  <h1>Example shop</h1>
  <p>This page embeds the website chat of <code>${escape(chatUrl)}</code>. Open the chat button at the bottom right.</p>
  ${customer
    ? `<p>Signed in as <strong>${escape(customer.name)}</strong>. The chat knows who you are.</p>
       <form method="post" action="/logout"><button>Sign out</button></form>`
    : `<p>You are browsing anonymously. Sign in to chat as a known customer${secret ? '' : ' (set CHATX_WIDGET_SECRET to enable this)'}.</p>
       <form method="post" action="/login"><input name="name" placeholder="Your name" required maxlength="60"><button ${secret ? '' : 'disabled'}>Sign in</button></form>`}

  <script>
    // 1. Queue the configuration; it runs as soon as the loader has loaded.
    window.ChatXWidget = window.ChatXWidget || [];
    window.ChatXWidget.push(['init', {
      widgetId: ${JSON.stringify(widgetId)},
      identityToken: ${customer && secret ? JSON.stringify(identityToken(customer)) : 'null'}
    }]);
    ${customer ? '' : `// After signing out, end the visitor session so the next person on this device starts fresh.
    if (location.search.includes('signedout')) window.ChatXWidget.push(['logout']);`}
  </script>
  <!-- 2. The loader, served by your Knock Twice installation. -->
  <script src="${escape(chatUrl)}/widget.js" async></script>
</body>
</html>`;
}

function readForm(req) {
  return new Promise((resolve) => {
    let body = '';
    req.on('data', (chunk) => { body += chunk; if (body.length > 10_000) req.destroy(); });
    req.on('end', () => resolve(new URLSearchParams(body)));
  });
}

createServer(async (req, res) => {
  const url = new URL(req.url, `http://localhost:${port}`);
  const sid = /(?:^|;\s*)shop=([\w-]+)/.exec(req.headers.cookie ?? '')?.[1];
  const customer = sid ? sessions.get(sid) : undefined;

  if (req.method === 'POST' && url.pathname === '/login') {
    const name = (await readForm(req)).get('name')?.trim().slice(0, 60) || 'Customer';
    const id = randomUUID();
    sessions.set(id, { id: `customer-${id}`, name, email: undefined });
    res.writeHead(303, { location: '/', 'set-cookie': `shop=${id}; HttpOnly; SameSite=Lax; Path=/` }).end();
    return;
  }
  if (req.method === 'POST' && url.pathname === '/logout') {
    if (sid) sessions.delete(sid);
    res.writeHead(303, { location: '/?signedout', 'set-cookie': 'shop=; Max-Age=0; Path=/' }).end();
    return;
  }
  if (req.method === 'GET' && url.pathname === '/') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }).end(page(customer));
    return;
  }
  res.writeHead(404).end();
}).listen(port, () => console.log(`Example shop on http://localhost:${port}`));
