/** Audorix.ChatX mark: two interlocking message bubbles. Decorative; the name is rendered as text. */
export function BrandMark({ size = 40 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" aria-hidden="true" focusable="false" className="cx-mark">
      <rect x="2" y="6" width="30" height="24" rx="8" fill="currentColor" opacity="0.35" />
      <path d="M9 30 l-3 8 l10 -8 z" fill="currentColor" opacity="0.35" />
      <rect x="16" y="16" width="30" height="24" rx="8" fill="currentColor" />
      <path d="M39 40 l3 7 l-10 -7 z" fill="currentColor" />
      <path d="M25 23 l12 10 M37 23 l-12 10" stroke="var(--cx-mark-ink)" strokeWidth="3.2" strokeLinecap="round" />
    </svg>
  );
}
