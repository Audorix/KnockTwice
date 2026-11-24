import { useEffect, useId, useRef, type ReactNode } from 'react';
import { NavIcon } from './NavIcon';

/**
 * A confirmation dialog in place of the browser's confirm box (owner mockup 2026-11-24): warning
 * tile, title, explanation, optional extra content, and two buttons. The safe choice has focus, so
 * Enter and Escape keep things as they are; clicking outside does the same. Focus returns to the
 * opener on close.
 */
export function ConfirmDialog({ title, text, children, keepLabel, confirmLabel, busyLabel, closeLabel, busy = false, onKeep, onConfirm }: {
  title: string;
  text: ReactNode;
  children?: ReactNode;
  keepLabel: string;
  confirmLabel: string;
  busyLabel: string;
  closeLabel: string;
  busy?: boolean;
  onKeep: () => void;
  onConfirm: () => void;
}) {
  const keep = useRef<HTMLButtonElement>(null);
  const opener = useRef<Element | null>(null);
  const id = useId();
  useEffect(() => {
    opener.current = document.activeElement;
    keep.current?.focus();
    return () => (opener.current as HTMLElement | null)?.focus?.();
  }, []);
  return (
    <div className="cx-dialog-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget && !busy) onKeep(); }}>
      <div className="cx-dialog cx-confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby={`${id}-title`}
        aria-describedby={`${id}-text`} onKeyDown={(e) => { if (e.key === 'Escape' && !busy) onKeep(); }}>
        <button type="button" className="cx-tool cx-confirm-close" onClick={onKeep} aria-label={closeLabel} disabled={busy}>
          <NavIcon name="close" size={20} />
        </button>
        <span className="cx-confirm-icon" aria-hidden="true"><NavIcon name="warning" size={28} /></span>
        <h2 id={`${id}-title`} className="cx-confirm-title">{title}</h2>
        <p id={`${id}-text`} className="cx-confirm-text">{text}</p>
        {children}
        <div className="cx-confirm-actions">
          <button ref={keep} type="button" className="btn btn-outline-primary" onClick={onKeep} disabled={busy}>{keepLabel}</button>
          <button type="button" className="btn btn-danger" onClick={onConfirm} disabled={busy}>{busy ? busyLabel : confirmLabel}</button>
        </div>
      </div>
    </div>
  );
}
