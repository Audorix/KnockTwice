/// <reference types="vitest/config" />
import { defineConfig, loadEnv, type Connect, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath } from 'node:url';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * The embedded chat page may only be framed by the widget's allowed origins (ADR-024). nginx does
 * this in Docker with auth_request; in development and preview this middleware asks the API for
 * the widget's frame policy and sends it as `Content-Security-Policy: frame-ancestors`. Unknown,
 * disabled or unreachable widgets get `frame-ancestors 'none'`.
 */
function widgetFramePolicy(apiTarget: string): Plugin {
  const middleware: Connect.NextHandleFunction = (req, res, next) => {
    const url = new URL(req.url ?? '/', 'http://localhost');
    if (url.pathname !== '/embed.html' && url.pathname !== '/embed') return next();
    const widget = url.searchParams.get('widget') ?? '';
    const deny = () => {
      res.setHeader('Content-Security-Policy', "frame-ancestors 'none'");
      next();
    };
    if (!GUID.test(widget)) return deny();
    fetch(`${apiTarget}/api/v1/widget/${widget}/frame-policy`, { signal: AbortSignal.timeout(3000) })
      .then((response) => {
        const ancestors = response.ok ? response.headers.get('x-chatx-frame-ancestors') : null;
        res.setHeader('Content-Security-Policy', `frame-ancestors ${ancestors || "'none'"}`);
        res.setHeader('Cache-Control', 'no-store');
        next();
      })
      .catch(deny);
  };
  return {
    name: 'chatx-widget-frame-policy',
    configureServer: (server) => void server.middlewares.use(middleware),
    configurePreviewServer: (server) => void server.middlewares.use(middleware),
  };
}

// Development: the browser talks only to the Vite origin; Vite forwards API paths to the
// ASP.NET Core host. No CORS is configured on the API. In Docker, nginx does the same job.
export default defineConfig(({ mode }) => {
  // Read .env files next to this config, regardless of the directory Vite was started from.
  const env = loadEnv(mode, fileURLToPath(new URL('.', import.meta.url)), 'CHATX_');
  const apiTarget = env.CHATX_DEV_API_URL || 'http://localhost:5080';
  const proxied = { target: apiTarget, changeOrigin: false, secure: true };

  return {
    plugins: [react(), widgetFramePolicy(apiTarget)],
    server: {
      port: 5173,
      strictPort: true,
      proxy: {
        '/api': proxied,
        '/health': proxied,
        '/openapi': proxied,
        '/swagger': proxied,
        // SignalR: WebSocket upgrade plus the negotiate/long-polling fallback.
        '/hubs': { ...proxied, ws: true },
      },
    },
    preview: { port: 4173, strictPort: true },
    build: {
      sourcemap: false,
      target: 'es2022',
      // Two pages: the application and the embedded website chat (framed by widget.js).
      rollupOptions: {
        input: {
          main: fileURLToPath(new URL('./index.html', import.meta.url)),
          embed: fileURLToPath(new URL('./embed.html', import.meta.url)),
        },
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      restoreMocks: true,
      css: false,
    },
  };
});
