import { beforeAll, describe, expect, it } from 'vitest';
import i18n, { initI18n, languageStorageKey, selectLanguage, toSupportedLanguage } from './i18n';
import en from './locales/en.json';
import es from './locales/es.json';

function keysOf(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null) return [prefix];
  return Object.entries(value).flatMap(([key, child]) => keysOf(child, prefix ? `${prefix}.${key}` : key));
}

describe('localization', () => {
  beforeAll(async () => {
    await initI18n();
  });

  it('English and Spanish resources define exactly the same keys', () => {
    expect(keysOf(es).sort()).toEqual(keysOf(en).sort());
  });

  it('maps regional and unsupported tags to a supported language', () => {
    expect(toSupportedLanguage('es-MX')).toBe('es');
    expect(toSupportedLanguage('EN-gb')).toBe('en');
    expect(toSupportedLanguage('fr')).toBe('en');
    expect(toSupportedLanguage(undefined)).toBe('en');
  });

  it('persists an explicit selection and updates the document language', async () => {
    await selectLanguage('es');

    expect(window.localStorage.getItem(languageStorageKey)).toBe('es');
    expect(i18n.resolvedLanguage).toBe('es');
    expect(document.documentElement.lang).toBe('es');
    expect(document.title).toBe(es.app.documentTitle);

    await selectLanguage('en');
    expect(window.localStorage.getItem(languageStorageKey)).toBe('en');
  });

  it('falls back to English for a missing key in another language', async () => {
    await i18n.changeLanguage('es');
    i18n.addResource('en', 'translation', 'test.onlyEnglish', 'English only');

    expect(i18n.t('test.onlyEnglish')).toBe('English only');
  });

  it('uses locale plural rules', async () => {
    await i18n.changeLanguage('es');
    expect(i18n.t('foundation.languageCount', { count: 1 })).toBe('1 idioma de interfaz disponible');
    expect(i18n.t('foundation.languageCount', { count: 2 })).toBe('2 idiomas de interfaz disponibles');
  });
});
