import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import 'bootstrap/dist/css/bootstrap.min.css';
import './styles/theme.css';
import { initI18n } from './i18n/i18n';
import { FoundationPage } from './features/foundation/FoundationPage';

// Follow the operating system colour scheme using Bootstrap's built-in dark mode.
const scheme = window.matchMedia('(prefers-color-scheme: dark)');
const applyScheme = () => document.documentElement.setAttribute('data-bs-theme', scheme.matches ? 'dark' : 'light');
applyScheme();
scheme.addEventListener('change', applyScheme);

await initI18n();

const container = document.getElementById('root');
if (!container) throw new Error('Root element #root is missing.');

createRoot(container).render(
  <StrictMode>
    <FoundationPage />
  </StrictMode>,
);
