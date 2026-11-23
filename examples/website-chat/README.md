# Example: website chat on your own site

A dependency-free Node.js site (`server.mjs`) that embeds the Audorix.ChatX website chat. It shows:

- the loader snippet;
- identifying a signed-in customer with a token **your server** signs;
- ending the visitor session when the customer signs out.

## 1. Create a widget

In Audorix.ChatX, as an owner or administrator of an organization:

1. Open **Organizations → your organization → Website chat** and create a widget.
2. Copy the **signing secret** shown once (you can replace it later).
3. Add `http://localhost:3000` to **Website addresses allowed to show the chat**.
4. Choose who answers, and switch the chat on.

## 2. Run the example

```sh
cd examples/website-chat
CHATX_URL=http://localhost:5173 CHATX_WIDGET_ID=<widget id> CHATX_WIDGET_SECRET=<signing secret> node server.mjs
```

PowerShell:

```powershell
$env:CHATX_URL='http://localhost:5173'; $env:CHATX_WIDGET_ID='<widget id>'; $env:CHATX_WIDGET_SECRET='<signing secret>'; node server.mjs
```

`CHATX_URL` is the address people use to open Audorix.ChatX:

- `http://localhost:5173` with the development servers;
- `http://localhost:8080` with the Docker development profiles;
- your `https://` address in production.

Open <http://localhost:3000> and use the chat button.

- **Anonymous visitor:** chat without signing in (if the widget allows anonymous visitors).
- **Identified customer:** sign in with any name. The page passes a freshly signed token. Your team
  sees the customer's name, and the same customer continues the same conversation on other devices.
- **Sign out** ends the visitor session (`ChatXWidget.logout()`).

## What to copy into your site

```html
<script>
  window.ChatXWidget = window.ChatXWidget || [];
  window.ChatXWidget.push(['init', { widgetId: 'WIDGET-ID', identityToken: TOKEN_FROM_YOUR_SERVER_OR_NULL }]);
</script>
<script src="https://chat.example.org/widget.js" async></script>
```

…and a server-side function like `identityToken()` in `server.mjs`. Any JWT library works if it
uses **HS256** and sets these claims:

- `sub`: your stable user id;
- `aud`: the widget id;
- `iat` and `exp`: at most one hour apart;
- optionally `name` and `email`.

For React apps, use the `ChatXWidget` component from [`web/packages/chat-embed`](../../web/packages/chat-embed).

The full loader API (`identify`, `logout`, `open`, `close`, `toggle`, `destroy`, `on`) is
documented at the top of [`widget.js`](../../web/apps/chat-web/public/widget.js).

## Security notes

- Never put the signing secret in browser code. Anyone holding it can impersonate any customer.
- Only the website addresses you list can show the chat. Browsers enforce this through
  `frame-ancestors`, and the server checks it again when a session starts.
- The chat runs in its own frame. Your page's scripts and styles cannot read or change it.
