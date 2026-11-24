export type NavIconName =
  | 'chats' | 'lock' | 'people' | 'building' | 'back' | 'shield' | 'hash' | 'headset' | 'search' | 'at'
  | 'reply' | 'smile' | 'thread' | 'dots' | 'edit' | 'trash' | 'clip' | 'send';

/** Line icons for the navigation and message tools (drawn here; no icon font or package). */
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
      {name === 'reply' && <path {...common} d="M9 7 4 12l5 5M4 12h10a6 6 0 0 1 6 6v1" />}
      {name === 'smile' && (
        <>
          <circle {...common} cx="12" cy="12" r="8.5" />
          <path {...common} d="M8.5 14.2a4.2 4.2 0 0 0 7 0M9.2 9.8h.01M14.8 9.8h.01" />
        </>
      )}
      {name === 'thread' && (
        <>
          <path {...common} d="M5 5h14a1.5 1.5 0 0 1 1.5 1.5v8A1.5 1.5 0 0 1 19 16h-7l-4.5 3.5V16H5a1.5 1.5 0 0 1-1.5-1.5v-8A1.5 1.5 0 0 1 5 5z" />
          <path {...common} d="M8 9.5h8M8 12.5h5" />
        </>
      )}
      {name === 'dots' && <path {...common} strokeWidth={2.6} d="M12 5.5h.01M12 12h.01M12 18.5h.01" />}
      {name === 'edit' && <path {...common} d="M4 20h4L19 9l-4-4L4 16v4zM13.5 6.5l4 4" />}
      {name === 'trash' && <path {...common} d="M4.5 7h15M10 4h4M6.5 7l1 13h9l1-13M10 11v5.5M14 11v5.5" />}
      {name === 'clip' && <path {...common} d="m20 11.5-8.2 8.2a5 5 0 0 1-7.1-7.1l8.5-8.5a3.3 3.3 0 0 1 4.7 4.7l-8.4 8.4a1.7 1.7 0 0 1-2.4-2.4l7.7-7.7" />}
      {name === 'send' && <path {...common} d="M4 12 20 4l-4.5 16-3.5-6.5L4 12zM12 13.5 20 4" />}
      {name === 'shield' && (
        <>
          <path {...common} d="M12 3l7 3v5c0 4.5-3 8.3-7 10-4-1.7-7-5.5-7-10V6l7-3z" />
          <path {...common} d="M9 12l2 2 4-4" />
        </>
      )}
    </svg>
  );
}
