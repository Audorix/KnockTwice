import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';

/** Phase 20: quick times for "send later" and "remind me", plus a custom date and time. */

export interface TimeChoice {
  key: string;
  at: Date;
}

/** In 20 min, 1 h, 3 h; tomorrow 9:00; next Monday 9:00 (local time). */
export function presetTimes(now = new Date()): TimeChoice[] {
  const minutes = (n: number) => new Date(now.getTime() + n * 60_000);
  const tomorrow = new Date(now);
  tomorrow.setDate(now.getDate() + 1);
  tomorrow.setHours(9, 0, 0, 0);
  const monday = new Date(now);
  monday.setDate(now.getDate() + (((8 - now.getDay()) % 7) || 7));
  monday.setHours(9, 0, 0, 0);
  return [
    { key: 'in20', at: minutes(20) },
    { key: 'in60', at: minutes(60) },
    { key: 'in180', at: minutes(180) },
    { key: 'tomorrow', at: tomorrow },
    { key: 'monday', at: monday },
  ];
}

/** A value for an <input type="datetime-local"> in local time. */
export function localInputValue(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/**
 * A button that opens a list of quick times and a custom date/time field. Picking calls
 * <paramref name="onPick"/> with the chosen moment.
 */
export function TimeMenu({
  label,
  buttonClassName,
  buttonContent,
  disabled,
  onPick,
}: {
  label: string;
  buttonClassName?: string;
  /** Shown instead of the label (which then names the button for assistive technology and the tooltip). */
  buttonContent?: React.ReactNode;
  disabled?: boolean;
  onPick: (at: Date) => void;
}) {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const [custom, setCustom] = useState('');
  const box = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (!open) return;
    setCustom(localInputValue(new Date(Date.now() + 60 * 60_000)));
    const close = (event: MouseEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  const pick = (at: Date) => {
    setOpen(false);
    onPick(at);
  };

  const submitCustom = (event: FormEvent) => {
    event.preventDefault();
    const at = new Date(custom);
    if (!Number.isNaN(at.getTime())) pick(at);
  };

  const format = (date: Date) => date.toLocaleString(i18n.language, { weekday: 'short', hour: '2-digit', minute: '2-digit' });

  return (
    <span className="cx-reaction-picker" ref={box}>
      <button type="button" className={buttonClassName ?? 'btn btn-sm btn-link p-0'} aria-expanded={open} aria-haspopup="menu" disabled={disabled}
        title={buttonContent ? label : undefined} onClick={() => setOpen((o) => !o)}>
        {buttonContent ? (
          <>
            {buttonContent}
            <span className="visually-hidden">{label}</span>
          </>
        ) : (
          label
        )}
      </button>
      {open && (
        <span className="cx-reaction-menu cx-tools-menu cx-time-menu" role="menu" onKeyDown={(e) => e.key === 'Escape' && setOpen(false)}>
          {presetTimes().map((choice) => (
            <button key={choice.key} type="button" role="menuitem" className="cx-tools-item" onClick={() => pick(choice.at)}>
              {t(`planning.presets.${choice.key}`)} <span className="cx-meta">{format(choice.at)}</span>
            </button>
          ))}
          <form className="cx-time-custom" onSubmit={submitCustom}>
            <label className="visually-hidden" htmlFor={`${label}-custom`}>
              {t('planning.customTime')}
            </label>
            <input
              id={`${label}-custom`}
              type="datetime-local"
              className="form-control form-control-sm"
              value={custom}
              onChange={(e) => setCustom(e.target.value)}
            />
            <button type="submit" className="btn btn-sm btn-outline-primary">
              {t('planning.set')}
            </button>
          </form>
        </span>
      )}
    </span>
  );
}
