import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { NavIcon } from './NavIcon';

/**
 * A confirmation dialog in place of the browser's confirm box (owner mockup 2026-11-24): warning
 * tile, title, explanation, optional extra content, and two buttons. The safe choice has focus, so
 * Enter and Escape keep things as they are; clicking outside does the same. Focus returns to the
 * opener on close. With <paramref name="typeToConfirm"/>, the action stays disabled until that exact
 * text is typed (for deletions that cannot be undone).
 */
export function ConfirmDialog({ title, text, children, keepLabel, confirmLabel, busyLabel, closeLabel, busy = false, tone = 'danger', typeToConfirm, typeLabel, onKeep, onConfirm }: {
  title: string;
  text: ReactNode;
  children?: ReactNode;
  keepLabel: string;
  confirmLabel: string;
  busyLabel: string;
  closeLabel: string;
  busy?: boolean;
  /** danger (red action, warning tile) or primary (for reversible actions). */
  tone?: 'danger' | 'primary';
  typeToConfirm?: string;
  typeLabel?: string;
  onKeep: () => void;
  onConfirm: () => void;
}) {
  const keep = useRef<HTMLButtonElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const opener = useRef<Element | null>(null);
  const [typed, setTyped] = useState('');
  const id = useId();
  useEffect(() => {
    opener.current = document.activeElement;
    (typeToConfirm ? input.current : keep.current)?.focus();
    return () => (opener.current as HTMLElement | null)?.focus?.();
  }, [typeToConfirm]);
  // Same comparison as the server (case-insensitive).
  // toLowerCase, not toLocaleLowerCase: the browser's language must not change the comparison (Turkish "I").
  const ready = !typeToConfirm || typed.trim().toLowerCase() === typeToConfirm.toLowerCase();
  return (
    <div className="cx-dialog-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget && !busy) onKeep(); }}>
      <div className="cx-dialog cx-confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby={`${id}-title`}
        aria-describedby={`${id}-text`} onKeyDown={(e) => {
          // Escape closes only this dialog, not a drawer it was opened from; Tab stays inside it.
          if (e.key === 'Escape') {
            e.stopPropagation();
            if (!busy) onKeep();
          } else {
            trapTab(e);
          }
        }}>
        <button type="button" className="cx-tool cx-confirm-close" onClick={onKeep} aria-label={closeLabel} disabled={busy}>
          <NavIcon name="close" size={20} />
        </button>
        <span className={`cx-confirm-icon${tone === 'primary' ? ' is-primary' : ''}`} aria-hidden="true">
          <NavIcon name={tone === 'primary' ? 'info' : 'warning'} size={28} />
        </span>
        <h2 id={`${id}-title`} className="cx-confirm-title">{title}</h2>
        <p id={`${id}-text`} className="cx-confirm-text">{text}</p>
        {children}
        {typeToConfirm && (
          <div className="mb-3">
            <label htmlFor={`${id}-typed`} className="form-label">{typeLabel}</label>
            <input ref={input} id={`${id}-typed`} className="form-control" autoComplete="off" spellCheck={false} value={typed}
              onChange={(e) => setTyped(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter' && ready && !busy) onConfirm(); }} />
          </div>
        )}
        <div className="cx-confirm-actions">
          <button ref={keep} type="button" className="btn btn-outline-primary" onClick={onKeep} disabled={busy}>{keepLabel}</button>
          <button type="button" className={`btn ${tone === 'primary' ? 'btn-primary' : 'btn-danger'}`} onClick={onConfirm} disabled={busy || !ready}>
            {busy ? busyLabel : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

/** Keeps Tab and Shift+Tab inside the element handling the key (a modal dialog). */
export function trapTab(event: KeyboardEvent<HTMLElement>) {
  if (event.key !== 'Tab') return;
  const focusable = Array.from(event.currentTarget.querySelectorAll<HTMLElement>(
    'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
  ));
  if (focusable.length === 0) return;
  const first = focusable[0]!;
  const last = focusable[focusable.length - 1]!;
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

export interface ConfirmRequest {
  title: string;
  text: ReactNode;
  confirmLabel: string;
  /** Defaults to "Cancel". */
  keepLabel?: string;
  tone?: 'danger' | 'primary';
  typeToConfirm?: string;
  typeLabel?: string;
}

/**
 * <c>const [confirm, dialog] = useConfirm()</c>; render <c>{dialog}</c>, then
 * <c>if (await confirm({...})) …</c> wherever the browser's <c>window.confirm</c> was used.
 */
export function useConfirm(): [(request: ConfirmRequest) => Promise<boolean>, ReactNode] {
  const { t } = useTranslation();
  const [pending, setPending] = useState<{ request: ConfirmRequest; resolve: (ok: boolean) => void } | null>(null);
  const confirm = useCallback(
    (request: ConfirmRequest) => new Promise<boolean>((resolve) => setPending({ request, resolve })),
    [],
  );
  const close = (ok: boolean) => {
    pending?.resolve(ok);
    setPending(null);
  };
  const dialog = pending ? (
    <ConfirmDialog
      title={pending.request.title}
      text={pending.request.text}
      keepLabel={pending.request.keepLabel ?? t('confirm.cancel')}
      confirmLabel={pending.request.confirmLabel}
      busyLabel={t('auth.working')}
      closeLabel={t('confirm.close')}
      tone={pending.request.tone ?? 'danger'}
      {...(pending.request.typeToConfirm ? { typeToConfirm: pending.request.typeToConfirm, typeLabel: pending.request.typeLabel ?? '' } : {})}
      onKeep={() => close(false)}
      onConfirm={() => close(true)}
    />
  ) : null;
  return [confirm, dialog];
}
