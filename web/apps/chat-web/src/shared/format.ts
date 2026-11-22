/** Locale-aware formatting helpers; all user-visible dates and numbers go through Intl. */

export function formatTime(value: Date, language: string): string {
  return new Intl.DateTimeFormat(language, { timeStyle: 'medium' }).format(value);
}

export function formatDuration(milliseconds: number, language: string): string {
  return new Intl.NumberFormat(language, {
    style: 'unit',
    unit: 'millisecond',
    unitDisplay: 'short',
    maximumFractionDigits: 0,
  }).format(milliseconds);
}
