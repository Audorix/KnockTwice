import i18n, { type BackendModule, type ResourceKey } from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next } from 'react-i18next';
import en from './locales/en.json';
import { setDeletedUserLabelProvider, setLanguageProvider } from '../shared/api/httpClient';

export const supportedLanguages = ['en', 'es'] as const;
export type SupportedLanguage = (typeof supportedLanguages)[number];
export const fallbackLanguage: SupportedLanguage = 'en';

/** localStorage key holding the user's explicit language choice. */
export const languageStorageKey = 'chatx.language';

/**
 * Only the fallback language is bundled up front. Other languages are separate chunks, fetched
 * the first time they are needed (detected at start-up, selected, or set by the user's server
 * preference); i18next waits for them before switching, so no untranslated keys are shown.
 */
export const resources = {
  en: { translation: en },
} as const;

type LazyLanguage = Exclude<SupportedLanguage, typeof fallbackLanguage>;

const lazyTranslations: Record<LazyLanguage, () => Promise<{ default: ResourceKey }>> = {
  es: () => import('./locales/es.json'),
};

const lazyLocales: BackendModule = {
  type: 'backend',
  init: () => {},
  read: (language, namespace, callback) => {
    if (namespace !== 'translation') return callback(null, {});
    if (language === fallbackLanguage) return callback(null, en);
    const load = lazyTranslations[language as LazyLanguage];
    if (!load) return callback(null, {});
    load().then(
      (module) => callback(null, module.default),
      (error: unknown) => callback(error instanceof Error ? error : new Error(String(error)), null),
    );
  },
};

export function isSupportedLanguage(value: string | undefined): value is SupportedLanguage {
  return supportedLanguages.includes(value as SupportedLanguage);
}

/** Maps any detected tag ("es-MX", "EN") to a supported base language, or the fallback. */
export function toSupportedLanguage(tag: string | undefined): SupportedLanguage {
  const base = tag?.toLowerCase().split('-')[0];
  return isSupportedLanguage(base) ? base : fallbackLanguage;
}

export function currentLanguage(): SupportedLanguage {
  return toSupportedLanguage(i18n.resolvedLanguage ?? i18n.language);
}

function syncDocument(language: string): void {
  if (typeof document === 'undefined') return;
  document.documentElement.lang = toSupportedLanguage(language);
  document.title = i18n.t('app.documentTitle');
}

/**
 * Detection order: an explicit stored choice, then the browser language, then English.
 * Only an explicit selection (see LanguageSelector) is written to storage.
 */
export async function initI18n(): Promise<typeof i18n> {
  if (i18n.isInitialized) return i18n;

  setLanguageProvider(currentLanguage);
  setDeletedUserLabelProvider(() => (i18n.isInitialized ? i18n.t('users.deleted') : undefined));
  i18n.on('languageChanged', syncDocument);
  await i18n
    .use(lazyLocales)
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
      resources,
      partialBundledLanguages: true,
      supportedLngs: [...supportedLanguages],
      fallbackLng: fallbackLanguage,
      load: 'languageOnly',
      interpolation: { escapeValue: false },
      returnNull: false,
      detection: {
        order: ['localStorage', 'navigator'],
        lookupLocalStorage: languageStorageKey,
        caches: [],
        convertDetectedLanguage: (lng: string) => toSupportedLanguage(lng),
      },
    });
  syncDocument(i18n.language);
  return i18n;
}

/** Changes the UI language and persists the explicit choice. */
export async function selectLanguage(language: SupportedLanguage): Promise<void> {
  try {
    window.localStorage.setItem(languageStorageKey, language);
  } catch {
    // Storage can be unavailable (private mode, blocked site data); the choice still applies now.
  }
  await i18n.changeLanguage(language);
}

export default i18n;
