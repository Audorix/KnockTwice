import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import 'bootstrap/dist/css/bootstrap.min.css';
import './styles/theme.css';
import { initI18n } from './i18n/i18n';
import { AuthProvider } from './features/auth/AuthContext';
import { AppShell } from './app/AppShell';
import { SetupGate } from './features/setup/SetupGate';
import { RealtimeProvider } from './features/realtime/RealtimeContext';
import { NotificationsProvider } from './features/notifications/NotificationsContext';
import { initTheme } from './shared/theme';

// Follow the operating system colour scheme until the signed-in user's saved theme is known.
initTheme();

await initI18n();

const container = document.getElementById('root');
if (!container) throw new Error('Root element #root is missing.');

createRoot(container).render(
  <StrictMode>
    <SetupGate>
      <AuthProvider>
        <RealtimeProvider>
          <NotificationsProvider>
            <AppShell />
          </NotificationsProvider>
        </RealtimeProvider>
      </AuthProvider>
    </SetupGate>
  </StrictMode>,
);
