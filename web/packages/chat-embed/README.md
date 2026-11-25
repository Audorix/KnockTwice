# @audorix/chatx-embed

React integration for the website chat of an [Knock Twice](../../../README.md) installation.

The chat runs in an iframe served by your installation. This package only loads the
installation's `widget.js` and drives it, so no chat code runs in your page and your page never
sees the visitor's session.

```tsx
import { ChatXWidget } from '@audorix/chatx-embed';

export function Layout({ identityToken }: { identityToken: string | null }) {
  return (
    <>
      {/* … your app … */}
      <ChatXWidget
        baseUrl="https://chat.example.org"
        widgetId="01a205e7-68c1-76f1-bcaf-e17aba6f1c76"
        identityToken={identityToken} // null for visitors who are not signed in
        onUnreadChange={(count) => console.log(count)}
      />
    </>
  );
}
```

| Prop | Notes |
| --- | --- |
| `baseUrl` | Your installation's origin. Its `widget.js` is loaded once per page. |
| `widgetId` | From the organization's **Website chat** settings. |
| `identityToken` | Optional. A JWT your **server** signs with the widget's signing secret: HS256, claims `sub` (your user id), `name`, `email`, `aud` (the widget id), `iat`, `exp` (at most one hour later by default). Changing it from a token to `null` ends the visitor session; do that when your user signs out. |
| `language` | `en` or `es`. Defaults to the widget's setting or the visitor's browser. |
| `open` | Controls whether the panel is open. |
| `onReady`, `onUnreadChange`, `onOpenChange`, `onError` | Callbacks. |

`loadChatXWidget(baseUrl)` returns the loader's API (`identify`, `logout`, `open`, `close`,
`toggle`, `destroy`, `on`) if you prefer to drive it yourself.

Your website's origin must be listed in the widget's allowed addresses; other sites cannot frame it.
Never put the signing secret in browser code.

## Build and publish

The package is plain TypeScript with React as a peer dependency. It is not an npm workspace
member; inside this repository it uses the TypeScript and React types installed for `web/`.

```sh
cd web/packages/chat-embed
npm run build            # writes dist/
npm pack --dry-run       # builds (prepack) and lists what would be published
```

Maintainers publish a release with `npm publish` from this folder (the `@audorix` npm scope is
required; `publishConfig.access` is public). The version follows the Knock Twice release.
