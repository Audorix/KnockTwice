export type NavIconName =
  | 'chats' | 'lock' | 'people' | 'building' | 'back' | 'shield' | 'hash' | 'headset' | 'search' | 'at'
  | 'reply' | 'smile' | 'thread' | 'dots' | 'edit' | 'trash' | 'clip' | 'send'
  | 'settings' | 'user' | 'block' | 'plus' | 'message' | 'chevron-left' | 'chevron-right' | 'close'
  | 'chevron-down' | 'download' | 'pin' | 'check' | 'check-double' | 'bookmark' | 'clock' | 'bell' | 'calendar' | 'external'
  | 'file' | 'info' | 'archive' | 'leave';

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
      {name === 'file' && (
        <>
          <path {...common} d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z" />
          <path {...common} d="M14 3v5h5" />
        </>
      )}
      {name === 'archive' && (
        <>
          <rect {...common} x="3" y="4" width="18" height="4.5" rx="1" />
          <path {...common} d="M5 8.5V18a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8.5M10 12.5h4" />
        </>
      )}
      {name === 'leave' && (
        <>
          <path {...common} d="M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4" />
          <path {...common} d="M10 16l-4-4 4-4M6 12h10" />
        </>
      )}
      {name === 'info' && (
        <>
          <circle {...common} cx="12" cy="12" r="9" />
          <path {...common} d="M12 11v5M12 7.75v.01" />
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
      {name === 'settings' && (
        <>
          <circle {...common} cx="12" cy="12" r="3" />
          <path {...common} d="M19.4 13.5a7.6 7.6 0 0 0 0-3l2-1.6-2-3.4-2.4.9a7.6 7.6 0 0 0-2.6-1.5L14 2.5h-4l-.4 2.4A7.6 7.6 0 0 0 7 6.4l-2.4-.9-2 3.4 2 1.6a7.6 7.6 0 0 0 0 3l-2 1.6 2 3.4 2.4-.9a7.6 7.6 0 0 0 2.6 1.5l.4 2.4h4l.4-2.4a7.6 7.6 0 0 0 2.6-1.5l2.4.9 2-3.4-2-1.6z" />
        </>
      )}
      {name === 'user' && (
        <>
          <circle {...common} cx="12" cy="8" r="4" />
          <path {...common} d="M4 21a8 8 0 0 1 16 0" />
        </>
      )}
      {name === 'block' && (
        <>
          <circle {...common} cx="12" cy="12" r="8.5" />
          <path {...common} d="M6 18 18 6" />
        </>
      )}
      {name === 'plus' && <path {...common} d="M12 5v14M5 12h14" />}
      {name === 'message' && <path {...common} d="M5 5h14a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-7l-5 4v-4H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z" />}
      {name === 'chevron-left' && <path {...common} d="m14.5 6-6 6 6 6" />}
      {name === 'chevron-right' && <path {...common} d="m9.5 6 6 6-6 6" />}
      {name === 'close' && <path {...common} d="M6 6l12 12M18 6 6 18" />}
      {name === 'chevron-down' && <path {...common} d="m6 9.5 6 6 6-6" />}
      {name === 'download' && <path {...common} d="M12 4v11M7 10.5l5 5 5-5M5 20h14" />}
      {name === 'pin' && <path {...common} d="M9 4h6l-1 6 3 3v1H7v-1l3-3-1-6zM12 14v6" />}
      {name === 'check' && <path {...common} d="m5 12.5 4.5 4.5L19 7.5" />}
      {name === 'check-double' && <path {...common} d="m2.5 12.5 4.5 4.5 9-9.5M11 16.5l.5.5 10-10.5" />}
      {name === 'bookmark' && <path {...common} d="M7 4h10v17l-5-4-5 4V4z" />}
      {name === 'clock' && (
        <>
          <circle {...common} cx="12" cy="12" r="8.5" />
          <path {...common} d="M12 7.5V12l3 2" />
        </>
      )}
      {name === 'bell' && <path {...common} d="M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15L6 16zM10 20.5a2 2 0 0 0 4 0" />}
      {name === 'calendar' && (
        <>
          <rect {...common} x="3.5" y="5" width="17" height="15" rx="2" />
          <path {...common} d="M3.5 10h17M8 3v4M16 3v4" />
        </>
      )}
      {name === 'external' && <path {...common} d="M7 17 17 7M9 7h8v8" />}
      {name === 'shield' && (
        <>
          <path {...common} d="M12 3l7 3v5c0 4.5-3 8.3-7 10-4-1.7-7-5.5-7-10V6l7-3z" />
          <path {...common} d="M9 12l2 2 4-4" />
        </>
      )}
    </svg>
  );
}
