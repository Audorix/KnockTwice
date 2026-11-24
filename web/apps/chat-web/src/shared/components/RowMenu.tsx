import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { NavIcon, type NavIconName } from './NavIcon';

export interface RowMenuItem {
  icon: NavIconName;
  label: string;
  onSelect: () => void;
  danger?: boolean;
  separated?: boolean;
}

/** The "…" menu of a table row: a menu button with arrow-key navigation, closed by Escape or a click outside. */
export function RowMenu({ label, items }: { label: string; items: RowMenuItem[] }) {
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    box.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus();
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  const onKey = (event: KeyboardEvent<HTMLDivElement>) => {
    const entries = Array.from(box.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]') ?? []);
    const index = entries.indexOf(document.activeElement as HTMLButtonElement);
    if (event.key === 'Escape') {
      setOpen(false);
      button.current?.focus();
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      entries[(index + (event.key === 'ArrowDown' ? 1 : -1) + entries.length) % entries.length]?.focus();
    }
  };

  return (
    <div className="cx-row-menu" ref={box} onKeyDown={onKey}>
      <button ref={button} type="button" className="cx-tool cx-row-menu-button" aria-haspopup="menu" aria-expanded={open} title={label} onClick={() => setOpen((o) => !o)}>
        <NavIcon name="dots" size={18} />
        <span className="visually-hidden">{label}</span>
      </button>
      {open && (
        <div className="cx-menu" role="menu" aria-label={label}>
          {items.map((item) => (
            <button
              key={item.label}
              type="button"
              role="menuitem"
              className={`cx-menu-item${item.danger ? ' is-danger' : ''}${item.separated ? ' is-separated' : ''}`}
              onClick={() => {
                setOpen(false);
                // Focus goes back to the menu button first, so a dialog the item opens returns it there.
                button.current?.focus();
                item.onSelect();
              }}
            >
              <NavIcon name={item.icon} size={18} />
              {item.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
