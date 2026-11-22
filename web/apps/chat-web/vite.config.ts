/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// Development: the browser talks only to the Vite origin; Vite forwards API paths to the
// ASP.NET Core host. No CORS is configured on the API. In Docker, nginx does the same job.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'CHATX_');
  const apiTarget = env.CHATX_DEV_API_URL || 'http://localhost:5080';
  const proxied = { target: apiTarget, changeOrigin: false, secure: true };

  return {
    plugins: [react()],
    server: {
      port: 5173,
      strictPort: true,
      proxy: {
        '/api': proxied,
        '/health': proxied,
        '/openapi': proxied,
        '/swagger': proxied,
      },
    },
    preview: { port: 4173, strictPort: true },
    build: { sourcemap: false, target: 'es2022' },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      restoreMocks: true,
      css: false,
    },
  };
});
