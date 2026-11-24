/**
 * Knock Twice mark (owner logo of 2026-11-24): a dark teal bubble behind a light teal one with two
 * white strokes, the "knock knock". Brand colours in both themes. Decorative; the name is text.
 */
export function BrandMark({ size = 40 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" aria-hidden="true" focusable="false" className="cx-mark">
      <path d="M13 7h14a10 10 0 0 1 10 10v5H23a9 9 0 0 0-9 9v-1H11l-6 4 1.4-6A9.6 9.6 0 0 1 3 21v-4A10 10 0 0 1 13 7z" fill="#127677" />
      <path d="M23 17h14a9 9 0 0 1 9 9v8a9 9 0 0 1-5.2 8.2L42 47l-7-4H23a9 9 0 0 1-9-9v-8a9 9 0 0 1 9-9z" fill="#2aaeb0" />
      <path d="M26.5 35.5l4.5-7M32.5 35.5l4.5-7" stroke="#ffffff" strokeWidth="3.4" strokeLinecap="round" />
    </svg>
  );
}
