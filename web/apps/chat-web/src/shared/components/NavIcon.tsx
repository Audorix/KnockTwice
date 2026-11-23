export type NavIconName = 'chats' | 'lock' | 'people' | 'building' | 'back' | 'shield' | 'hash' | 'headset' | 'search' | 'at';

/** Line icons for the navigation (drawn here; no icon font or package). */
export function NavIcon({ name, size = 22 }: { name: NavIconName; size?: number }) {
  const common = { fill: 'none', stroke: 'currentColor', strokeWidth: 1.8, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const };
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} aria-hidden="true" focusable="false">
      {name === 'chats' && (
        <>
          <path {...common} d="M4 5h11a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H9l-4 3v-3H4a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z" />
          <path {...common} d="M19 9h1a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2h-1v3l-4-3h-3" />
        </>
      )}
      {name === 'lock' && (
        <>
          <rect {...common} x="5" y="10.5" width="14" height="10" rx="2" />
          <path {...common} d="M8 10.5V7.5a4 4 0 0 1 8 0v3" />
        </>
      )}
      {name === 'people' && (
        <>
          <circle {...common} cx="9" cy="8" r="3.5" />
          <path {...common} d="M2.5 20a6.5 6.5 0 0 1 13 0" />
          <path {...common} d="M16 4.6a3.5 3.5 0 0 1 0 6.8M18 14a6.5 6.5 0 0 1 3.5 6" />
        </>
      )}
      {name === 'building' && (
        <>
          <rect {...common} x="4" y="3" width="11" height="18" rx="1.5" />
          <path {...common} d="M15 9h4a1 1 0 0 1 1 1v11M7.5 7h4M7.5 11h4M7.5 15h4M2 21h20" />
        </>
      )}
      {name === 'back' && <path {...common} d="M15 5l-7 7 7 7" />}
      {name === 'search' && (
        <>
          <circle {...common} cx="10.5" cy="10.5" r="6.5" />
          <path {...common} d="M15.5 15.5 21 21" />
        </>
      )}
      {name === 'at' && (
        <>
          <circle {...common} cx="12" cy="12" r="3.8" />
          <path {...common} d="M15.8 8.5v5a2.6 2.6 0 0 0 5.2 0V12a9 9 0 1 0-3.6 7.2" />
        </>
      )}
      {name === 'hash' &&<path {...common} d="M9 3 7 21M17 3l-2 18M4 8.5h17M3 15.5h17" />}
      {name === 'headset' && (
        <>
          <path {...common} d="M4 14v-2a8 8 0 0 1 16 0v2" />
          <rect {...common} x="3" y="13" width="4" height="6" rx="1.5" />
          <rect {...common} x="17" y="13" width="4" height="6" rx="1.5" />
          <path {...common} d="M19 19c0 1.5-2 2.5-5 2.5h-1.5" />
        </>
      )}
      {name === 'shield' && (
        <>
          <path {...common} d="M12 3l7 3v5c0 4.5-3 8.3-7 10-4-1.7-7-5.5-7-10V6l7-3z" />
          <path {...common} d="M9 12l2 2 4-4" />
        </>
      )}
    </svg>
  );
}
