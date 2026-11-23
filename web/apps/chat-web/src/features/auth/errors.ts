import type { TFunction } from 'i18next';
import { ApiError } from '../../shared/api/httpClient';

/** Localized message for a failed request, keyed by the API's stable error code. */
export function errorMessage(error: unknown, t: TFunction): string {
  if (error instanceof ApiError) {
    if (error.kind !== 'http') return t('errors.network');
    // A validation failure about one specific problem (for example a rejected file) says which.
    if (error.code === 'validation_failed') {
      for (const code of Object.values(error.fieldErrors).flat()) {
        const text = t(`errors.${code}`);
        if (text !== `errors.${code}`) return text;
      }
    }
    if (error.code) {
      const key = `errors.${error.code}`;
      const text = t(key);
      if (text !== key) return text;
    }
  }
  return t('errors.unexpected');
}

/** Localized messages for one form field from a validation_failed response. */
export function fieldErrors(error: unknown, field: string, t: TFunction): string[] {
  if (!(error instanceof ApiError)) return [];
  return (error.fieldErrors[field] ?? []).map((code) => {
    const key = `fieldErrors.${code}`;
    const text = t(key);
    return text === key ? t('fieldErrors.invalid') : text;
  });
}
