import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './embed.css';
import i18n, { initI18n, toSupportedLanguage } from '../i18n/i18n';
import { EmbedApp } from './EmbedApp';
import { HostBridge, resolveParentOrigin } from './hostBridge';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

await initI18n();

const search = new URLSearchParams(window.location.search);
const widgetId = search.get('widget') ?? '';
const parentOrigin = resolveParentOrigin(search);
const requested = search.get('lang');
const language = requested ? toSupportedLanguage(requested) : toSupportedLanguage(navigator.language);
await i18n.changeLanguage(language);

const container = document.getElementById('root');
if (container && GUID.test(widgetId) && parentOrigin) {
  createRoot(container).render(
    <StrictMode>
      <EmbedApp widgetId={widgetId} parentOrigin={parentOrigin} language={language} languageExplicit={!!requested} bridge={new HostBridge(parentOrigin)} />
    </StrictMode>,
  );
}
